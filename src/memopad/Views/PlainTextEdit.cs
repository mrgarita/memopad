using System.ComponentModel;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace Memopad.Views;

/// <summary>
/// 本文の編集コントロール。Windows 標準の RichEdit（msftedit.dll）をプレーン テキスト モードで使う。
///
/// v0.1.0 では WPF の TextBox を使っていたが、キー入力から画面に文字が出るまでに約 50 ms かかり
/// （WPF は UI スレッド→描画スレッド→DWM と経由するため）、メモ帳（約 19 ms）より明らかに遅かった。
/// RichEdit は GDI で直接描くので約 21 ms とメモ帳並みになる（計測は docs/site/step3 を参照）。
/// IME の未確定文字の表示も RichEdit が自前で行うため、日本語入力の追従が良い。
/// </summary>
public sealed class PlainTextEdit : WinForms.RichTextBox
{
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int EM_SETRECT = 0x00B3;
    private const int EM_SETTARGETDEVICE = 0x0448;
    private const int EM_SETUNDOLIMIT = 0x0452;
    private const int EM_SETTEXTMODE = 0x0459;
    private const int EM_SHOWSCROLLBAR = 0x0460;
    private const int SB_HORZ = 0;
    private const int TM_PLAINTEXT = 1;
    private const int TM_MULTILEVELUNDO = 8;
    private const int TM_MULTICODEPAGE = 32;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hWnd, IOleDropTarget target);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private bool _wordWrap = true;
    private bool _darkScrollBars;
    private FileDropTarget? _dropTarget;
    private SmoothWheelScroller? _wheel;   // 起動を遅らせないよう、最初にホイールを回したときに作る

    public PlainTextEdit()
    {
        Multiline = true;
        AcceptsTab = true;
        BorderStyle = WinForms.BorderStyle.None;
        ScrollBars = WinForms.RichTextBoxScrollBars.Both;   // 必要なときだけ出る
        // 折り返しは WrapText（EM_SETTARGETDEVICE）で切り替える。Windows Forms の WordWrap を true のままにすると
        // 横スクロールバー（WS_HSCROLL）とカーソルに合わせた横送り（ES_AUTOHSCROLL）が付かず、
        // 折り返さないときに長い行の右端から先が見えなくなる
        WordWrap = false;
        HideSelection = false;                               // 検索バーへフォーカスが移っても選択を見せる
        DetectUrls = false;
        EnableAutoDragDrop = false;                          // RichEdit 自身のドラッグ＆ドロップは使わない
        // ファイルのドロップは FileDropTarget で受ける（AllowDrop = true にすると RichEdit にも
        // ドロップが渡り、開いた直後の文書が「編集済み」になってしまう）
        AutoWordSelection = false;
        // 日本語など別スクリプトの文字は RichEdit の自動フォント選択（フォント バインディング）で適切なフォントに切り替える
        LanguageOption = WinForms.RichTextBoxLanguageOptions.AutoFont | WinForms.RichTextBoxLanguageOptions.DualFont;
    }

    /// <summary>Ctrl＋ホイール（delta は WM_MOUSEWHEEL の値）。</summary>
    public event Action<int>? ZoomWheel;

    /// <summary>右クリックまたはメニュー キー。</summary>
    public event Action? ContextMenuRequested;

    /// <summary>ファイルがドロップされた。</summary>
    public event Action<string[]>? FilesDropped;

    /// <summary>右端で折り返すか。ハンドルを作り直さずに RichEdit へ直接指示する（元に戻す履歴を保つため）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool WrapText
    {
        get => _wordWrap;
        set
        {
            _wordWrap = value;
            if (IsHandleCreated) ApplyWrap();
        }
    }

    /// <summary>ダーク テーマ用のスクロールバーにするか。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DarkScrollBars
    {
        get => _darkScrollBars;
        set
        {
            _darkScrollBars = value;
            if (IsHandleCreated) ApplyScrollBarTheme();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        Services.PerfLog.Mark("RichEdit の HWND を生成");
        // プレーン テキスト モードは本文が空のときにしか切り替えられないので、基底クラスが本文を復元する前に送る
        SendMessageW(Handle, EM_SETTEXTMODE, (IntPtr)(TM_PLAINTEXT | TM_MULTILEVELUNDO | TM_MULTICODEPAGE), IntPtr.Zero);
        SendMessageW(Handle, EM_SETUNDOLIMIT, (IntPtr)1000, IntPtr.Zero);
        base.OnHandleCreated(e);
        ApplyWrap();
        ApplyScrollBarTheme();
        ApplyInset();
        RegisterFileDropTarget();
        Services.PerfLog.Mark("RichEdit の初期化を完了");
    }

    protected override void CreateHandle()
    {
        Services.PerfLog.Mark("RichEdit CreateHandle 開始（msftedit.dll 読み込み）");
        base.CreateHandle();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated) ApplyInset();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (IsHandleCreated) RevokeDragDrop(Handle);
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _wheel?.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// RichEdit が自分で登録しているドロップ先を外し、ファイルだけを受け取る自前のものに差し替える。
    /// RichEdit に処理させると、ファイルを開いた直後の文書が「編集済み」になってしまうため
    /// （経緯は <see cref="FileDropTarget"/>）。
    /// </summary>
    private void RegisterFileDropTarget()
    {
        WinForms.Application.OleRequired();
        RevokeDragDrop(Handle);
        // ドロップの処理中に開くと、ファイルが大きいときにドラッグ元を待たせてしまうので、
        // メッセージを処理し終えてから開く
        _dropTarget ??= new FileDropTarget(files => BeginInvoke(() => FilesDropped?.Invoke(files)));
        RegisterDragDrop(Handle, _dropTarget);
    }

    protected override void WndProc(ref WinForms.Message m)
    {
        // 本文はウィンドウの左右いっぱいに広がるので、縁のリサイズ判定は親フォームへ譲る
        if (ChromeHitTest.TryPassToFrame(this, ref m)) return;
        switch (m.Msg)
        {
            case WM_MOUSEWHEEL when (WinForms.Control.ModifierKeys & WinForms.Keys.Control) != 0:
                // RichEdit 自身のズームは使わず、アプリのズーム（ステータスバーの % と連動）に回す
                ZoomWheel?.Invoke((short)((long)m.WParam >> 16));
                return;
            case WM_MOUSEWHEEL when (WinForms.Control.ModifierKeys & WinForms.Keys.Shift) == 0:
                // RichEdit 自身のホイール処理は、末尾が改行だと最後の空行まで届かないので自前で動かす
                (_wheel ??= new SmoothWheelScroller(this)).Scroll((short)((long)m.WParam >> 16));
                m.Result = IntPtr.Zero;
                return;
            case WM_CONTEXTMENU:
                ContextMenuRequested?.Invoke();
                return;
        }
        base.WndProc(ref m);
    }

    private void ApplyWrap()
    {
        // 折り返すときは横スクロールバーを使わない。RichEdit は「折り返しなし→あり」に切り替えても
        // 横スクロールバーを自分では片付けず、表示の高さの計算が狂うので、先に明示的に隠す
        // （折り返さないときは、長い行があるときだけ RichEdit が出す）
        SendMessageW(Handle, EM_SHOWSCROLLBAR, (IntPtr)SB_HORZ, (IntPtr)(_wordWrap ? 0 : 1));
        // lParam=0 でウィンドウ幅に折り返し、1 で折り返しなし（横スクロール）
        SendMessageW(Handle, EM_SETTARGETDEVICE, IntPtr.Zero, (IntPtr)(_wordWrap ? 0 : 1));
    }

    private void ApplyScrollBarTheme()
    {
        SetWindowTheme(Handle, _darkScrollBars ? "DarkMode_Explorer" : "Explorer", null);
    }

    /// <summary>本文の描画領域に余白を付ける（メモ帳の左余白に合わせる）。サイズ変更で戻るので都度設定する。</summary>
    private void ApplyInset()
    {
        var c = ClientRectangle;
        if (c.Width <= 0 || c.Height <= 0) return;
        var scale = DeviceDpi / 96.0;
        var r = new RECT
        {
            Left = c.Left + (int)(8 * scale),
            Top = c.Top + (int)(4 * scale),
            Right = c.Right - (int)(4 * scale),
            Bottom = c.Bottom,
        };
        SendMessageW(Handle, EM_SETRECT, IntPtr.Zero, ref r);
    }
}
