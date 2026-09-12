using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Memopad.Dialogs;
using Memopad.Models;
using Memopad.Services;

namespace Memopad.Views;

/// <summary>
/// メイン ウィンドウ（v0.8.0 で WPF の MainWindow から Windows Forms に置き換え。起動時間をメモ帳並みにするため）。
/// タブの管理、ファイル操作、検索／置換、表示設定、色の変更をまとめて扱う。
/// 標準のタイトル バーは WM_NCCALCSIZE で外し、TitleBar コントロールにタブとキャプション ボタンを描く。
/// </summary>
public sealed partial class MainForm : Form
{
    private const int ZoomStep = 10;
    private const int ZoomMin = 10;
    private const int ZoomMax = 500;
    private const int TitleHeight = 42;
    private const int MenuHeight = 32;
    private const int StatusHeight = 32;
    private const int ResizeBorder = 6;

    private readonly List<DocumentTab> _tabs = new();
    private readonly string[] _startupFiles;
    private DocumentTab? _current;
    private int _zoomPercent = 100;
    private ThemePalette _palette;
    private bool _firstPaintLogged;

    private readonly TitleBar _titleBar;
    private readonly MenuStrip _menu;
    private readonly FluentMenuRenderer _renderer;
    private readonly Panel _editorHost;
    private readonly StatusBarPanel _statusBar;
    private readonly Panel _findBar;

    private static AppSettings Settings => Program.Settings;

    private DocumentTab? Current => _current;

    public MainForm(string[] args)
    {
        PerfLog.Mark("MainForm ctor 開始");
        _startupFiles = args;
        _palette = ThemeService.Palette(Settings.Theme);
        _renderer = new FluentMenuRenderer(_palette);

        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "MemoPad";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        BackColor = _palette.Window;
        Font = UiStyle.PixelFont(UiStyle.UiFontName, S(13));
        AllowDrop = true;
        MinimumSize = new Size(S(420), S(300));
        // 設定は論理 px。クライアント領域がウィンドウ全体になるので Size をそのまま使う
        Size = new Size(S((int)Settings.WindowWidth), S((int)Settings.WindowHeight));
        Icon = LoadAppIcon(32);

        _editorHost = new ChromePanel { Dock = DockStyle.Fill, BackColor = _palette.Window };
        _findBar = BuildFindBar();
        _menu = BuildMenu();
        _statusBar = new StatusBarPanel
        {
            Dock = DockStyle.Bottom,
            Height = S(StatusHeight),
            Palette = _palette,
            Font = UiStyle.PixelFont(UiStyle.UiFontName, S(12)),
        };
        _statusBar.LineEndingClicked += ShowLineEndingMenu;
        _statusBar.EncodingClicked += ShowEncodingMenu;
        _titleBar = new TitleBar
        {
            Dock = DockStyle.Top,
            Height = S(TitleHeight),
            Palette = _palette,
            AppIcon = LoadAppIcon(16),
        };
        _titleBar.TabSelected += tab => SelectTab(tab);
        _titleBar.TabCloseRequested += tab => CloseTab(tab);
        _titleBar.NewTabRequested += () => AddTab(new Document());
        _titleBar.MinimizeRequested += () => WindowState = FormWindowState.Minimized;
        _titleBar.MaximizeRequested += ToggleMaximize;
        _titleBar.CloseRequested += Close;
        _titleBar.Paint += (_, _) =>
        {
            if (_firstPaintLogged) return;
            _firstPaintLogged = true;
            PerfLog.Mark("最初の描画");
        };

        // Dock は Controls の後ろにあるものから外側に置かれる：タイトル行→ステータスバー→メニュー→検索バー→本文
        Controls.Add(_editorHost);
        Controls.Add(_findBar);
        Controls.Add(_menu);
        Controls.Add(_statusBar);
        Controls.Add(_titleBar);
        MainMenuStrip = _menu;
        ResumeLayout(false);

        UpdateViewMenu();
        UpdateThemeMenu();
        if (Settings.WindowMaximized) WindowState = FormWindowState.Maximized;

        // 起動時は常に新規の空タブから始める（セッション復元は作らない方針）
        AddTab(new Document());
        PerfLog.Mark("MainForm ctor 完了");
    }

    /// <summary>論理 px を現在の表示スケールの物理 px にする。</summary>
    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    /// <summary>exe に埋め込んだアプリ アイコンを指定サイズで取り出す。</summary>
    private static Icon? LoadAppIcon(int size)
    {
        try
        {
            if (Environment.ProcessPath is not { } exe) return null;
            return Icon.ExtractIcon(exe, 0, size) ?? Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            // アイコンが取れなくても動作には影響しない
            return null;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PerfLog.Mark("HWND を生成");
        ThemeService.ApplyWindowFrame(Handle, _palette.IsDark);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PerfLog.Mark("Load");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PerfLog.Mark("Shown");
        // 起動引数に渡されたファイルを開く（エクスプローラーからの「プログラムから開く」に対応）
        foreach (var path in _startupFiles)
        {
            if (File.Exists(path)) OpenFile(path);
        }
        Current?.Editor.FocusEditor();
    }

    // =====================================================================
    // 自前のタイトル行：標準の枠を外す（WM_NCCALCSIZE）とヒット テスト（WM_NCHITTEST）
    // =====================================================================

    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CYSIZEFRAME = 33;
    private const int SM_CXPADDEDBORDER = 92;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_NCCALCSIZE:
                // 標準のタイトル バーと枠を外し、ウィンドウ全体をクライアント領域にする。
                // 最大化中は見えない枠の分だけ画面外にはみ出すので、その分を内側に寄せる
                if (IsZoomed(Handle))
                {
                    var dpi = (uint)DeviceDpi;
                    var fx = GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
                    var fy = GetSystemMetricsForDpi(SM_CYSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
                    // wParam の真偽に関わらず lParam の先頭は対象の矩形
                    var rc = Marshal.PtrToStructure<RECT>(m.LParam);
                    rc.Left += fx;
                    rc.Top += fy;
                    rc.Right -= fx;
                    rc.Bottom -= fy;
                    Marshal.StructureToPtr(rc, m.LParam, false);
                }
                m.Result = IntPtr.Zero;
                return;

            case WM_NCHITTEST:
            {
                var lp = (int)(long)m.LParam;
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                var hit = HitTestChrome(p);
                if (hit != 0)
                {
                    m.Result = hit;
                    return;
                }
                break;
            }
        }
        base.WndProc(ref m);
    }

    /// <summary>ウィンドウの縁ならリサイズ、タイトル行ならキャプション（移動）として答える。0 なら既定の判定。</summary>
    private int HitTestChrome(Point p)
    {
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        if (WindowState != FormWindowState.Maximized)
        {
            var b = S(ResizeBorder);
            var left = p.X < b;
            var right = p.X >= w - b;
            var top = p.Y < b;
            var bottom = p.Y >= h - b;
            if (top && left) return HTTOPLEFT;
            if (top && right) return HTTOPRIGHT;
            if (bottom && left) return HTBOTTOMLEFT;
            if (bottom && right) return HTBOTTOMRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
        }
        if (p.Y >= 0 && p.Y < _titleBar.Height && p.X >= 0 && p.X < w)
        {
            // タブやボタンの上は通常のクリックとして扱う（0 を返すと既定の判定＝HTCLIENT になる）
            return _titleBar.HasInteractiveChildAt(_titleBar.PointToClient(PointToScreen(p))) ? 0 : HTCAPTION;
        }
        return 0;
    }

    /// <summary>
    /// その位置（クライアント座標）がタイトル行の空きかウィンドウの縁か。
    /// 子コントロールが <see cref="ChromeHitTest"/> から呼び、true なら当たり判定を親（このフォーム）へ譲る。
    /// </summary>
    internal bool IsWindowFrameAt(Point clientPoint) => HitTestChrome(clientPoint) != 0;

    private void ToggleMaximize() =>
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_titleBar is not null) _titleBar.IsMaximized = WindowState == FormWindowState.Maximized;
    }

    // =====================================================================
    // タブ
    // =====================================================================

    private DocumentTab AddTab(Document document)
    {
        var tab = new DocumentTab(document);
        tab.Editor.ApplyAppearance(Settings, _zoomPercent);
        tab.Editor.CaretChanged += (_, _) => { if (tab == _current) UpdateCaretStatus(); };
        tab.Editor.ContentChanged += (_, _) => { if (tab == _current) UpdateContentStatus(); };
        // RichEdit はホイールや右クリックを自分で受け取るので、アプリ側の操作へ橋渡しする
        tab.Editor.Edit.ZoomWheel += delta => SetZoom(_zoomPercent + (delta > 0 ? ZoomStep : -ZoomStep));
        tab.Editor.Edit.ContextMenuRequested += () => ShowEditorContextMenu(tab);
        tab.Editor.Edit.FilesDropped += files => { foreach (var f in files) if (File.Exists(f)) OpenFile(f); };
        document.PropertyChanged += (_, _) =>
        {
            if (tab == _current) UpdateTitle();
            _titleBar.RefreshTabs();
        };
        tab.Editor.Edit.Visible = false;
        _editorHost.Controls.Add(tab.Editor.Edit);
        _tabs.Add(tab);
        SelectTab(tab);
        return tab;
    }

    private void SelectTab(DocumentTab? tab)
    {
        _current = tab;
        foreach (var t in _tabs) t.Editor.Edit.Visible = t == tab;
        _titleBar.SetTabs(_tabs, tab);
        UpdateTitle();
        UpdateCaretStatus();
        UpdateContentStatus();
        if (tab is not null && IsHandleCreated)
        {
            BeginInvoke(() => tab.Editor.FocusEditor());
        }
    }

    private void SelectAdjacentTab(int delta)
    {
        if (_tabs.Count < 2) return;
        var index = _current is null ? 0 : _tabs.IndexOf(_current);
        SelectTab(_tabs[(index + delta + _tabs.Count) % _tabs.Count]);
    }

    /// <summary>タブを閉じる。未保存なら確認する。閉じられなかった（キャンセル）場合は false。</summary>
    private bool CloseTab(DocumentTab tab)
    {
        if (!ConfirmDiscard(tab)) return false;

        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        _editorHost.Controls.Remove(tab.Editor.Edit);
        tab.Editor.Edit.Dispose();
        if (_tabs.Count == 0)
        {
            // 最後のタブを閉じたらメモ帳と同じくウィンドウを閉じる
            _current = null;
            Close();
            return true;
        }
        SelectTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
        return true;
    }

    /// <summary>未保存の変更があれば「保存／保存しない／キャンセル」を確認する。続行してよければ true。</summary>
    private bool ConfirmDiscard(DocumentTab tab)
    {
        if (!tab.Document.IsDirty) return true;
        SelectTab(tab);
        return AskSaveChanges(tab.Document.Title) switch
        {
            SaveChangesResult.Save => SaveTab(tab),
            SaveChangesResult.DontSave => true,
            _ => false,
        };
    }

    // WPF のダイアログは専用のメソッドから呼ぶ（呼び出し側のメソッドが JIT されるときに WPF が読み込まれないようにする）
    private SaveChangesResult AskSaveChanges(string title)
    {
        WpfHost.Ensure(Settings.Theme);
        return SaveChangesDialog.Ask(Handle, title);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;
        // 未保存のタブを順に確認する。ひとつでもキャンセルされたら閉じない
        foreach (var tab in _tabs.ToList())
        {
            if (!ConfirmDiscard(tab))
            {
                e.Cancel = true;
                return;
            }
        }
        Settings.WindowMaximized = WindowState == FormWindowState.Maximized;
        if (WindowState == FormWindowState.Normal)
        {
            Settings.WindowWidth = Math.Round(Width * 96.0 / DeviceDpi);
            Settings.WindowHeight = Math.Round(Height * 96.0 / DeviceDpi);
        }
        SettingsService.Save(Settings);
    }

    // =====================================================================
    // キー操作（メニューのショートカット以外）
    // =====================================================================

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.Tab:
                SelectAdjacentTab(+1);
                return true;
            case Keys.Control | Keys.Shift | Keys.Tab:
                SelectAdjacentTab(-1);
                return true;
            case Keys.Escape when _findBar.Visible:
                HideFindBar();
                return true;
            case Keys.Control | Keys.Y:
                Current?.Editor.Redo();
                return true;
            case Keys.Control | Keys.Oemplus:
            case Keys.Control | Keys.Add:
                SetZoom(_zoomPercent + ZoomStep);
                return true;
            case Keys.Control | Keys.OemMinus:
            case Keys.Control | Keys.Subtract:
                SetZoom(_zoomPercent - ZoomStep);
                return true;
            case Keys.Control | Keys.D0:
            case Keys.Control | Keys.NumPad0:
                SetZoom(100);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // =====================================================================
    // ファイルのドロップ（タイトル行やメニューの上。本文の上は RichEdit が受ける）
    // =====================================================================

    protected override void OnDragEnter(DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        base.OnDragEnter(e);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files) if (File.Exists(f)) OpenFile(f);
        }
        base.OnDragDrop(e);
    }

    // =====================================================================
    // ファイル
    // =====================================================================

    private const string OpenFilter = "テキスト文書 (*.txt)|*.txt|すべてのファイル (*.*)|*.*";

    // 名前を付けて保存では「ファイルの種類」でエンコードを選ぶ（共通ダイアログにはメモ帳のようなエンコード欄を
    // 足せないため、種類の一覧で代用する）
    private static readonly (string Label, TextEncodingKind? Encoding)[] SaveFilters =
    {
        ("テキスト文書 - UTF-8 (*.txt)", TextEncodingKind.Utf8),
        ("テキスト文書 - UTF-8 (BOM 付き) (*.txt)", TextEncodingKind.Utf8Bom),
        ("テキスト文書 - ANSI (Shift_JIS) (*.txt)", TextEncodingKind.Ansi),
        ("テキスト文書 - UTF-16 LE (*.txt)", TextEncodingKind.Utf16LE),
        ("テキスト文書 - UTF-16 BE (*.txt)", TextEncodingKind.Utf16BE),
        ("すべてのファイル (現在のエンコード) (*.*)", null),
    };

    public void OpenFile(string path)
    {
        path = Path.GetFullPath(path);

        // 既に開いているならそのタブへ
        var existing = _tabs.FirstOrDefault(t => string.Equals(t.Document.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectTab(existing);
            return;
        }

        TextFileContent content;
        try
        {
            content = TextFileService.Read(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{path}\n\nファイルを開けませんでした。\n{ex.Message}", "MemoPad", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 空の「タイトルなし」しか無いときはそのタブを使い回す（メモ帳と同じ挙動）
        var tab = Current is { } c && c.Document.FilePath is null && !c.Document.IsDirty && c.Editor.TextLength == 0
            ? c
            : AddTab(new Document());

        tab.Document.FilePath = path;
        tab.Document.Encoding = content.Encoding;
        tab.Document.LineEnding = content.LineEnding;
        tab.Editor.LoadText(content.Text);
        SelectTab(tab);
        Settings.PushRecentFile(path);
        UpdateTitle();
        UpdateContentStatus();
    }

    private void Open()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = OpenFilter,
            Multiselect = true,
            Title = "開く",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var path in dialog.FileNames) OpenFile(path);
    }

    private void Save()
    {
        if (Current is { } tab) SaveTab(tab);
    }

    private void SaveAs()
    {
        if (Current is { } tab) SaveTabAs(tab);
    }

    private void SaveAll()
    {
        foreach (var tab in _tabs.ToList())
        {
            if (tab.Document.IsDirty || tab.Document.FilePath is null)
            {
                if (!SaveTab(tab)) return;
            }
        }
    }

    /// <summary>保存する。パスが無ければ「名前を付けて保存」。成功したら true。</summary>
    private bool SaveTab(DocumentTab tab)
    {
        if (tab.Document.FilePath is null) return SaveTabAs(tab);
        return WriteTab(tab, tab.Document.FilePath);
    }

    private bool SaveTabAs(DocumentTab tab)
    {
        SelectTab(tab);
        var currentIndex = Array.FindIndex(SaveFilters, f => f.Encoding == tab.Document.Encoding);
        using var dialog = new SaveFileDialog
        {
            Filter = string.Join("|", SaveFilters.Select(f => $"{f.Label}|{(f.Encoding is null ? "*.*" : "*.txt")}")),
            FilterIndex = currentIndex < 0 ? 1 : currentIndex + 1,
            FileName = tab.Document.FilePath is null ? $"{tab.Document.Title}.txt" : Path.GetFileName(tab.Document.FilePath),
            InitialDirectory = tab.Document.FilePath is null ? "" : Path.GetDirectoryName(tab.Document.FilePath) ?? "",
            DefaultExt = "txt",
            AddExtension = true,
            Title = "名前を付けて保存",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;

        var chosen = SaveFilters[Math.Clamp(dialog.FilterIndex - 1, 0, SaveFilters.Length - 1)].Encoding;
        if (chosen is { } enc) tab.Document.Encoding = enc;
        return WriteTab(tab, dialog.FileName);
    }

    private bool WriteTab(DocumentTab tab, string path)
    {
        try
        {
            TextFileService.Write(path, tab.Editor.Text, tab.Document.Encoding, tab.Document.LineEnding);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{path}\n\n保存できませんでした。\n{ex.Message}", "MemoPad", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        tab.Document.FilePath = path;
        // 保存した時点の内容をエディタにも覚えさせる（ここまで元に戻すと「編集なし」に戻る）
        tab.Editor.MarkClean();
        tab.Document.IsDirty = false;
        Settings.PushRecentFile(path);
        UpdateTitle();
        UpdateContentStatus();
        return true;
    }

    private void NewWindow()
    {
        var exe = Environment.ProcessPath;
        if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    private void PageSetup()
    {
        WpfHost.Ensure(Settings.Theme);
        Dialogs.PageSetupDialog.Show(Handle, Settings);
    }

    private void Print()
    {
        if (Current is not { } tab) return;
        try
        {
            WpfHost.Ensure(Settings.Theme);
            PrintService.Print(tab.Document.Title, tab.Editor.Text, Settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"印刷できませんでした。\n{ex.Message}", "MemoPad", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // =====================================================================
    // 表示：ズーム・ステータスバー・折り返し
    // =====================================================================

    private void SetZoom(int percent)
    {
        _zoomPercent = Math.Clamp(percent, ZoomMin, ZoomMax);
        ApplyAppearanceToAll();
        _statusBar.ZoomText = $"{_zoomPercent}%";
    }

    private void ToggleStatusBar()
    {
        Settings.ShowStatusBar = !Settings.ShowStatusBar;
        UpdateViewMenu();
    }

    private void ToggleWordWrap()
    {
        Settings.WordWrap = !Settings.WordWrap;
        UpdateViewMenu();
        ApplyAppearanceToAll();
    }

    private void UpdateViewMenu()
    {
        _statusBarItem.Checked = Settings.ShowStatusBar;
        _wordWrapItem.Checked = Settings.WordWrap;
        _statusBar.Visible = Settings.ShowStatusBar;
    }

    // =====================================================================
    // 書式：フォント・色変更・配色パターン・テーマ
    // =====================================================================

    private void ChooseFont()
    {
        WpfHost.Ensure(Settings.Theme);
        if (Dialogs.FontDialog.Ask(Handle, Settings) is not { } choice) return;
        Settings.FontFamily = choice.Family;
        Settings.FontSize = choice.Size;
        Settings.FontBold = choice.Bold;
        Settings.FontItalic = choice.Italic;
        ApplyAppearanceToAll();
    }

    /// <summary>「色変更」ダイアログで背景色と文字色をまとめて変える（v0.4.0）。</summary>
    private void ChangeColors()
    {
        WpfHost.Ensure(Settings.Theme);
        if (ColorChangeDialog.Ask(Handle, Settings) is not { } result) return;
        Settings.BackgroundColor = result.Background;
        Settings.ForegroundColor = result.Foreground;
        ApplyAppearanceToAll();
    }

    /// <summary>「配色パターン」ダイアログで、目に優しい 16 パターンから背景色と文字色を一度に変える（v0.5.0）。</summary>
    private void ChooseColorScheme()
    {
        WpfHost.Ensure(Settings.Theme);
        if (ColorSchemeDialog.Ask(Handle, Settings) is not { } scheme) return;
        Settings.BackgroundColor = scheme.BackgroundHex;
        Settings.ForegroundColor = scheme.ForegroundHex;
        ApplyAppearanceToAll();
    }

    private void ResetColors()
    {
        Settings.BackgroundColor = "";
        Settings.ForegroundColor = "";
        ApplyAppearanceToAll();
    }

    private void ShowAbout()
    {
        WpfHost.Ensure(Settings.Theme);
        new AboutDialog(Handle).ShowDialog();
    }

    private void SetTheme(string theme)
    {
        Settings.Theme = theme;
        ApplyTheme();
    }

    /// <summary>テーマ（ライト／ダーク）を全体に反映する。</summary>
    private void ApplyTheme()
    {
        _palette = ThemeService.Palette(Settings.Theme);
        _renderer.Palette = _palette;
        BackColor = _palette.Window;
        _editorHost.BackColor = _palette.Window;
        _titleBar.Palette = _palette;
        _statusBar.Palette = _palette;
        _menu.BackColor = _palette.MenuBar;
        ApplyFindBarTheme(_findBar);
        ThemeService.ApplyWindowFrame(Handle, _palette.IsDark);
        if (WpfHost.IsInitialized) WpfHost.ApplyTheme(Settings.Theme);
        UpdateThemeMenu();
        ApplyAppearanceToAll();
        Invalidate(true);
    }

    private void UpdateThemeMenu()
    {
        _themeLightItem.Checked = Settings.Theme == "Light";
        _themeDarkItem.Checked = Settings.Theme == "Dark";
        _themeSystemItem.Checked = Settings.Theme is not ("Light" or "Dark");
    }

    /// <summary>全タブにフォント・色・折り返し・ズームを反映する。</summary>
    private void ApplyAppearanceToAll()
    {
        foreach (var tab in _tabs) tab.Editor.ApplyAppearance(Settings, _zoomPercent);
    }

    // =====================================================================
    // タイトルとステータスバー
    // =====================================================================

    private void UpdateTitle()
    {
        if (Current is { } tab)
        {
            Text = $"{(tab.Document.IsDirty ? "*" : "")}{tab.Document.Title} - MemoPad";
            _statusBar.LineEndingText = tab.Document.LineEnding.DisplayName();
            _statusBar.EncodingText = tab.Document.Encoding.DisplayName();
        }
        else
        {
            Text = "MemoPad";
        }
    }

    private void UpdateCaretStatus()
    {
        if (Current is { } tab)
        {
            var (line, column) = tab.Editor.GetCaretPosition();
            _statusBar.PositionText = $"行 {line}、列 {column}";
        }
    }

    private void UpdateContentStatus()
    {
        if (Current is { } tab)
        {
            _statusBar.CharCountText = $"{tab.Editor.GetCharacterCount():N0} 文字";
            UpdateTitle();
        }
    }

    private void ShowLineEndingMenu(Point screenPoint)
    {
        if (Current is not { } tab) return;
        var menu = NewPopupMenu(checkable: true);
        foreach (var kind in Enum.GetValues<LineEndingKind>())
        {
            var item = new ToolStripMenuItem(kind.DisplayName()) { Checked = tab.Document.LineEnding == kind, Padding = ItemPadding };
            item.Click += (_, _) =>
            {
                tab.Document.LineEnding = kind;
                tab.Document.IsDirty = true;
                UpdateTitle();
            };
            menu.Items.Add(item);
        }
        ShowPopupAbove(menu, screenPoint);
    }

    private void ShowEncodingMenu(Point screenPoint)
    {
        if (Current is not { } tab) return;
        var menu = NewPopupMenu(checkable: true);
        foreach (var kind in Enum.GetValues<TextEncodingKind>())
        {
            var item = new ToolStripMenuItem(kind.DisplayName()) { Checked = tab.Document.Encoding == kind, Padding = ItemPadding };
            item.Click += (_, _) =>
            {
                tab.Document.Encoding = kind;
                tab.Document.IsDirty = true;
                UpdateTitle();
            };
            menu.Items.Add(item);
        }
        ShowPopupAbove(menu, screenPoint);
    }

    /// <summary>ステータスバーの項目の上に（右端を基準に左へ）ポップアップを出す。</summary>
    private static void ShowPopupAbove(ContextMenuStrip menu, Point screenPoint)
    {
        menu.Show(screenPoint, ToolStripDropDownDirection.AboveLeft);
    }
}
