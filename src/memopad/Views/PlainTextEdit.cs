using System.ComponentModel;
using System.Runtime.InteropServices;
using ScintillaNET;
using WinForms = System.Windows.Forms;

namespace Memopad.Views;

/// <summary>
/// 本文の編集コントロール。Scintilla（scintilla.org のエディタ部品）をプレーン テキストとして使う。
///
/// v0.2.0〜v0.9.2 は Windows 標準の RichEdit を使っていたが、日本語と英字が混ざった行の
/// レイアウトが極端に遅く、10 MB・12 万行のファイルを開くと 43 秒、その後もリサイズのたびに
/// 28 秒画面が固まった（FB-19）。原因は GDI への文字幅の問い合わせで、RichEdit でも Win32 の
/// Edit コントロールでも、Windows App SDK の WinUI でも避けられなかった。
/// Scintilla は同じ計算を「空き時間に細切れで」行うため、合計時間はかかっても画面が止まらない。
/// 実測では最長の無応答が 0〜22 ms（メモ帳と同等）。経緯は docs/site/step3/index.html の 15 節。
///
/// 改行はエディタ内部で LF（"\n"）1 文字に統一する（保存時に <see cref="Services.TextFileService"/> が
/// ファイルの改行コードへ戻す）。RichEdit 時代と同じ約束なので、周りのコードは変えなくてよい。
/// </summary>
public sealed class PlainTextEdit : Scintilla
{
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_GETOBJECT = 0x003D;
    private const int OBJID_CLIENT = -4;
    private const int WM_TIMER = 0x0113;

    /// <summary>Scintilla が空き時間の処理（折り返しの計算）に使うタイマーの番号。</summary>
    private const int ScintillaIdleTimerId = 2;

    /// <summary>本文の左の余白（96 dpi のときの px）。メモ帳の見た目に合わせる。</summary>
    private const int LeftPadding = 8;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hWnd, IOleDropTarget target);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hWnd);

    [DllImport("oleacc.dll")]
    private static extern IntPtr LresultFromObject(ref Guid riid, IntPtr wParam,
        [MarshalAs(UnmanagedType.Interface)] object pAcc);

    private bool _wordWrap = true;
    private bool _darkScrollBars;
    private int _zoomPercent = 100;
    private double _baseFontSize = 11;
    private FileDropTarget? _dropTarget;
    private int _lastSelStart = -1;
    private int _lastSelEnd = -1;

    public PlainTextEdit()
    {
        BorderStyle = ScintillaNET.BorderStyle.None;
        // 構文の色分け・行番号・折りたたみ・自動インデントは使わない（メモ帳と同じ素のエディタ）
        LexerName = null;
        ViewWhitespace = WhitespaceMode.Invisible;
        ViewEol = false;
        IndentationGuides = IndentView.None;
        TabIndents = false;
        BackspaceUnindents = false;
        UseTabs = true;
        TabWidth = 8;
        // 改行は LF に統一し、貼り付けた文字列の改行もそろえる
        EolMode = Eol.Lf;
        PasteConvertEndings = true;
        // 最終行より先へはスクロールさせない（メモ帳と同じ。ホイールで最後の行まで届く）
        EndAtLastLine = true;
        // 右クリック メニューは MemoPad 側で出すので、Scintilla の既定メニューは使わない
        UsePopup(PopupMode.Never);
        // 折り返さないときの横スクロール範囲は、表示した行に合わせて自動で広げる
        ScrollWidth = 1;
        ScrollWidthTracking = true;
    }

    /// <summary>
    /// 支援技術（スクリーン リーダー）と <c>docs\tools\test-tabclick.ps1</c> から本文を見えるようにする。
    /// Scintilla は素の Control なので、既定では UI Automation に Pane としてしか出ない。
    /// </summary>
    protected override WinForms.AccessibleObject CreateAccessibilityInstance() => new EditorAccessibleObject(this);

    private sealed class EditorAccessibleObject(PlainTextEdit owner) : WinForms.Control.ControlAccessibleObject(owner)
    {
        public override WinForms.AccessibleRole Role => WinForms.AccessibleRole.Text;
        public override string? Name { get => "本文"; set { } }
        public override string? Value { get => owner.Text; set => owner.ReplaceAllText(value ?? ""); }
        public override WinForms.AccessibleStates State => WinForms.AccessibleStates.Focusable
            | (owner.Focused ? WinForms.AccessibleStates.Focused : WinForms.AccessibleStates.None);
    }

    /// <summary>支援技術から本文を差し替えられたときに使う（読み取りが主なので通常は呼ばれない）。</summary>
    private void ReplaceAllText(string text) => Text = text;


    /// <summary>
    /// true の間は Scintilla の空き時間の処理（折り返しの計算）を止める。
    ///
    /// Scintilla は折り返し位置の計算を 10 ms ずつに区切り、空き時間（10 ms 間隔のタイマー）で進める。
    /// 12 万行では約 4 秒かかり、その間にウィンドウを動かすと、マウスの移動と計算が同じ順番待ちの列に
    /// 交互に並んで 1 コマごとに最大 10 ms 待たされる（FB-21。実測でウィンドウ位置の更新間隔が
    /// 16.7 ms → 71 ms に落ちた）。移動・リサイズのあいだだけ止めると 16.7 ms に戻る。
    ///
    /// 画面に見えている範囲の折り返しは描画のときに計算されるので、止めていても表示は正しい。
    /// 止めた分の計算は、<see cref="MainForm"/> がフラグを下ろした時点で再開して最後まで終わる。
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool PauseIdleWork { get; set; }

    /// <summary>Ctrl＋ホイール（delta は WM_MOUSEWHEEL の値）。</summary>
    public event Action<int>? ZoomWheel;

    /// <summary>右クリックまたはメニュー キー。</summary>
    public event Action? ContextMenuRequested;

    /// <summary>ファイルがドロップされた。</summary>
    public event Action<string[]>? FilesDropped;

    /// <summary>カーソル位置や選択範囲が変わった。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>右端で折り返すか。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool WrapText
    {
        get => _wordWrap;
        set
        {
            _wordWrap = value;
            WrapMode = value ? WrapMode.Word : WrapMode.None;
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

    /// <summary>選択範囲の長さ（文字数）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectionLength
    {
        get => Math.Max(0, SelectionEnd - SelectionStart);
        set => SelectionEnd = SelectionStart + Math.Max(0, value);
    }

    /// <summary>開始位置と長さで選択する（RichTextBox と同じ呼び出し方にするための別名）。</summary>
    public void Select(int start, int length)
    {
        var max = TextLength;
        start = Math.Clamp(start, 0, max);
        var end = Math.Clamp(start + Math.Max(0, length), start, max);
        SetSelection(end, start);
    }

    /// <summary>カーソルが見える位置までスクロールする（RichTextBox と同じ名前にしておく）。</summary>
    public void ScrollToCaret() => ScrollCaret();

    /// <summary>元に戻す履歴を捨てる。</summary>
    public void ClearUndo() => EmptyUndoBuffer();

    /// <summary>
    /// 今の状態を「保存済み」として記録する（読み込み直後と保存直後に呼ぶ）。
    /// これ以降に編集されると <see cref="Scintilla.SavePointLeft"/> が上がり、
    /// 元に戻して同じ内容へ戻ると <see cref="Scintilla.SavePointReached"/> が上がる。
    /// 「編集済み」の判定はこの仕組みで行う（文字が変わったかどうかで見ると、
    /// コントロールの初期化でも変更とみなされてしまう）。
    /// </summary>
    public void MarkClean() => SetSavePoint();

    /// <summary>選択範囲を置き換える（RichTextBox の SelectedText セッター相当）。</summary>
    public void ReplaceSelectedText(string text) => ReplaceSelection(text ?? "");

    /// <summary>カーソル位置の「論理行（1 始まり）, 列（1 始まり）」。全文を走査せずに求める。</summary>
    public (int Line, int Column) GetCaretPosition()
    {
        var pos = CurrentPosition;
        var line = LineFromPosition(pos);
        return (line + 1, pos - Lines[line].Position + 1);
    }

    /// <summary>指定した論理行（1 始まり）の先頭へカーソルを移動する。</summary>
    public void GoToLine(int line)
    {
        var index = Math.Clamp(line - 1, 0, Math.Max(0, Lines.Count - 1));
        GotoPosition(Lines[index].Position);
        ScrollCaret();
        Focus();
    }

    /// <summary>
    /// フォント・色・ズーム倍率をまとめて反映する。
    /// ズームは Scintilla の Zoom（整数ポイントの増減）ではなく、フォント サイズを倍率で直接計算する
    /// （メモ帳と同じく % 指定なので、こちらのほうが表示と一致する）。
    /// </summary>
    public void ApplyTextAppearance(string family, double sizePoints, bool bold, bool italic,
                                    System.Drawing.Color back, System.Drawing.Color fore, int zoomPercent)
    {
        _baseFontSize = Math.Max(1, sizePoints);
        _zoomPercent = Math.Clamp(zoomPercent, 10, 500);
        var scaled = Math.Max(1, _baseFontSize * _zoomPercent / 100.0);

        var style = Styles[Style.Default];
        style.Font = family;
        style.SizeF = (float)scaled;
        style.Bold = bold;
        style.Italic = italic;
        style.BackColor = back;
        style.ForeColor = fore;
        // 既定スタイルを他のスタイル番号へ複製する（プレーン テキストなので全体が既定スタイル）
        StyleClearAll();

        // 本文の外側（最終行より下など）も同じ背景色にする
        BackColor = back;
        ForeColor = fore;
        CaretForeColor = fore;
        SelectionBackColor = SelectionBackFor(back, fore);
        SelectionTextColor = back;
        ApplyLeftPadding();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        Services.PerfLog.Mark("Scintilla の HWND を生成");
        base.OnHandleCreated(e);
        ApplyScrollBarTheme();
        ApplyLeftPadding();
        RegisterFileDropTarget();
        Services.PerfLog.Mark("Scintilla の初期化を完了");
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (IsHandleCreated) RevokeDragDrop(Handle);
        base.OnHandleDestroyed(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        if (IsHandleCreated) ApplyLeftPadding();
    }

    /// <summary>カーソル・選択範囲が動いたときに <see cref="SelectionChanged"/> を上げる。</summary>
    protected override void OnUpdateUI(UpdateUIEventArgs e)
    {
        base.OnUpdateUI(e);
        var start = SelectionStart;
        var end = SelectionEnd;
        if (start == _lastSelStart && end == _lastSelEnd) return;
        _lastSelStart = start;
        _lastSelEnd = end;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void WndProc(ref WinForms.Message m)
    {
        // 本文はウィンドウの左右いっぱいに広がるので、縁のリサイズ判定は親フォームへ譲る
        if (ChromeHitTest.TryPassToFrame(this, ref m)) return;
        switch (m.Msg)
        {
            // 折り返しの計算（空き時間の処理）を止めているあいだは、そのタイマーを通さない（FB-21）
            case WM_TIMER when PauseIdleWork && (int)m.WParam == ScintillaIdleTimerId:
                return;
            // Scintilla は Windows では支援技術に何も返さない（アクセシビリティ対応は GTK 版だけ）。
            // 本文が読み上げソフトから読めなくなるので、Windows Forms のアクセシブル オブジェクトを自分で返す
            case WM_GETOBJECT when (int)m.LParam == OBJID_CLIENT && AccessibilityObject is { } acc:
                var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");   // IID_IAccessible
                m.Result = LresultFromObject(ref iid, m.WParam, acc);
                return;
            case WM_MOUSEWHEEL when (WinForms.Control.ModifierKeys & WinForms.Keys.Control) != 0:
                // Scintilla 自身のズームは使わず、アプリのズーム（ステータスバーの % と連動）に回す
                ZoomWheel?.Invoke((short)((long)m.WParam >> 16));
                return;
            case WM_CONTEXTMENU:
                ContextMenuRequested?.Invoke();
                return;
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// Scintilla が自分で登録しているドロップ先を外し、ファイルだけを受け取る自前のものに差し替える。
    /// エディタに任せるとファイル名が本文に貼り付けられてしまう（RichEdit のときと同じ理由。
    /// 経緯は <see cref="FileDropTarget"/>）。
    /// </summary>
    private void RegisterFileDropTarget()
    {
        WinForms.Application.OleRequired();
        RevokeDragDrop(Handle);
        // ドロップの処理中に開くと、ファイルが大きいときにドラッグ元を待たせてしまうので、
        // メッセージを処理し終えてから開く
        _dropTarget ??= new FileDropTarget(files => BeginInvoke(() => FilesDropped?.Invoke(files)));
        var hr = RegisterDragDrop(Handle, _dropTarget);
        Services.PerfLog.Write($"RegisterDragDrop hr=0x{hr:X8}");
    }

    private void ApplyScrollBarTheme()
    {
        SetWindowTheme(Handle, _darkScrollBars ? "DarkMode_Explorer" : "Explorer", null);
    }

    /// <summary>本文の左に余白を入れる（メモ帳の左余白に合わせる）。Scintilla の余白は 0 番の欄で作る。</summary>
    private void ApplyLeftPadding()
    {
        var scale = DeviceDpi / 96.0;
        for (var i = 0; i < Margins.Count; i++) Margins[i].Width = 0;
        Margins[0].Type = MarginType.Symbol;
        Margins[0].Mask = 0;
        Margins[0].Sensitive = false;
        Margins[0].Width = (int)(LeftPadding * scale);
    }

    /// <summary>選択範囲の背景色。背景色と文字色の中間に置き、どの配色でも文字が読めるようにする。</summary>
    private static System.Drawing.Color SelectionBackFor(System.Drawing.Color back, System.Drawing.Color fore)
    {
        static int Mix(int a, int b) => (int)Math.Round(a * 0.35 + b * 0.65);
        return System.Drawing.Color.FromArgb(Mix(back.R, fore.R), Mix(back.G, fore.G), Mix(back.B, fore.B));
    }
}
