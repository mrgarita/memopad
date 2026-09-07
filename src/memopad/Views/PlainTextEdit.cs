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
    private const int TM_PLAINTEXT = 1;
    private const int TM_MULTILEVELUNDO = 8;
    private const int TM_MULTICODEPAGE = 32;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private bool _wordWrap = true;
    private bool _darkScrollBars;

    public PlainTextEdit()
    {
        Multiline = true;
        AcceptsTab = true;
        BorderStyle = WinForms.BorderStyle.None;
        ScrollBars = WinForms.RichTextBoxScrollBars.Both;   // 必要なときだけ出る
        HideSelection = false;                               // 検索バーへフォーカスが移っても選択を見せる
        DetectUrls = false;
        EnableAutoDragDrop = false;                          // 自前の OLE ドロップを止め、ファイルのドロップを受ける
        AllowDrop = true;
        AutoWordSelection = false;
        // 日本語など別スクリプトの文字は RichEdit の自動フォント選択（フォント バインディング）で適切なフォントに切り替える
        LanguageOption = WinForms.RichTextBoxLanguageOptions.AutoFont | WinForms.RichTextBoxLanguageOptions.DualFont;
    }

    /// <summary>ショートカット キーが押されたとき。true を返すとエディタでは処理しない。</summary>
    public event Func<WinForms.Keys, bool>? CommandKey;

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
        // プレーン テキスト モードは本文が空のときにしか切り替えられないので、基底クラスが本文を復元する前に送る
        SendMessageW(Handle, EM_SETTEXTMODE, (IntPtr)(TM_PLAINTEXT | TM_MULTILEVELUNDO | TM_MULTICODEPAGE), IntPtr.Zero);
        SendMessageW(Handle, EM_SETUNDOLIMIT, (IntPtr)1000, IntPtr.Zero);
        base.OnHandleCreated(e);
        ApplyWrap();
        ApplyScrollBarTheme();
        ApplyInset();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated) ApplyInset();
    }

    protected override void OnDragEnter(WinForms.DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(WinForms.DataFormats.FileDrop) == true ? WinForms.DragDropEffects.Copy : WinForms.DragDropEffects.None;
        base.OnDragEnter(e);
    }

    protected override void OnDragDrop(WinForms.DragEventArgs e)
    {
        if (e.Data?.GetData(WinForms.DataFormats.FileDrop) is string[] files) FilesDropped?.Invoke(files);
        base.OnDragDrop(e);
    }

    protected override bool ProcessCmdKey(ref WinForms.Message msg, WinForms.Keys keyData)
    {
        // Ctrl+F などアプリのショートカットは WPF 側（MainWindow）に判断してもらう
        if (CommandKey?.Invoke(keyData) == true) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref WinForms.Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEWHEEL when (WinForms.Control.ModifierKeys & WinForms.Keys.Control) != 0:
                // RichEdit 自身のズームは使わず、アプリのズーム（ステータスバーの % と連動）に回す
                ZoomWheel?.Invoke((short)((long)m.WParam >> 16));
                return;
            case WM_CONTEXTMENU:
                ContextMenuRequested?.Invoke();
                return;
        }
        base.WndProc(ref m);
    }

    private void ApplyWrap()
    {
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
