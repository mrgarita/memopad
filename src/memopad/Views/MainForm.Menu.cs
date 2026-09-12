using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Memopad.Models;
using Memopad.Services;

namespace Memopad.Views;

/// <summary>メイン ウィンドウ：メニュー（項目とショートカットはメモ帳に合わせる）と右クリック メニュー。</summary>
public sealed partial class MainForm
{
    private ToolStripMenuItem _editMenu = null!;
    private ToolStripMenuItem _recentMenu = null!;
    private ToolStripMenuItem _undoItem = null!;
    private ToolStripMenuItem _redoItem = null!;
    private ToolStripMenuItem _cutItem = null!;
    private ToolStripMenuItem _copyItem = null!;
    private ToolStripMenuItem _pasteItem = null!;
    private ToolStripMenuItem _deleteItem = null!;
    private ToolStripMenuItem _statusBarItem = null!;
    private ToolStripMenuItem _wordWrapItem = null!;
    private ToolStripMenuItem _themeLightItem = null!;
    private ToolStripMenuItem _themeDarkItem = null!;
    private ToolStripMenuItem _themeSystemItem = null!;

    /// <summary>
    /// ポップアップ内の項目の余白（Fluent のメニュー項目の高さに合わせる）。
    /// 左右はメニューの内部レイアウトが決めるので、効くのは上下（＝項目の高さ）だけ。
    /// </summary>
    private Padding ItemPadding => new(S(4), S(5), S(4), S(5));

    /// <summary>チェックが付き得る項目の目印。左のチェック欄を出すかの判定に使う。</summary>
    private static readonly object CheckableTag = new();

    private MenuStrip BuildMenu()
    {
        var menu = new ChromeMenuStrip
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = S(MenuHeight),
            Padding = new Padding(S(6), 0, 0, 0),
            BackColor = _palette.MenuBar,
            Renderer = _renderer,
            Font = Font,
            ImageScalingSize = new Size(S(16), S(16)),
            ShowItemToolTips = false,
            GripStyle = ToolStripGripStyle.Hidden,
        };

        // ファイル
        _recentMenu = SubMenu("最近使ったファイル(&R)", new ToolStripMenuItem("(なし)") { Enabled = false });
        _recentMenu.DropDownOpening += (_, _) => RebuildRecentMenu();
        menu.Items.Add(TopMenu("ファイル(&F)",
            Item("新しいタブ(&N)", () => AddTab(new Document()), Keys.Control | Keys.N),
            Item("新しいウィンドウ(&W)", NewWindow, Keys.Control | Keys.Shift | Keys.N),
            Item("開く(&O)...", Open, Keys.Control | Keys.O),
            _recentMenu,
            Item("保存(&S)", Save, Keys.Control | Keys.S),
            Item("名前を付けて保存(&A)...", SaveAs, Keys.Control | Keys.Shift | Keys.S),
            Item("すべて保存(&L)", SaveAll, Keys.Control | Keys.Alt | Keys.S),
            new ToolStripSeparator(),
            Item("ページ設定(&U)...", PageSetup),
            Item("印刷(&P)...", Print, Keys.Control | Keys.P),
            new ToolStripSeparator(),
            Item("タブを閉じる(&C)", () => { if (Current is { } t) CloseTab(t); }, Keys.Control | Keys.W),
            Item("ウィンドウを閉じる(&E)", Close, Keys.Control | Keys.Shift | Keys.W),
            Item("終了(&X)", Close)));

        // 編集：元に戻す〜削除はエディタ（RichEdit）が自前で処理するので表示だけ合わせる。有効／無効はメニューを開くときに更新する
        _undoItem = Item("元に戻す(&U)", () => Current?.Editor.Undo(), display: "Ctrl+Z");
        _redoItem = Item("やり直し(&R)", () => Current?.Editor.Redo(), display: "Ctrl+Y");
        _cutItem = Item("切り取り(&T)", () => Current?.Editor.Cut(), display: "Ctrl+X");
        _copyItem = Item("コピー(&C)", () => Current?.Editor.Copy(), display: "Ctrl+C");
        _pasteItem = Item("貼り付け(&P)", () => Current?.Editor.Paste(), display: "Ctrl+V");
        _deleteItem = Item("削除(&L)", () => Current?.Editor.Delete(), display: "Del");
        _editMenu = TopMenu("編集(&E)",
            _undoItem, _redoItem,
            new ToolStripSeparator(),
            _cutItem, _copyItem, _pasteItem, _deleteItem,
            new ToolStripSeparator(),
            Item("検索(&F)...", () => ShowFindBar(replace: false), Keys.Control | Keys.F),
            Item("次を検索(&N)", () => FindNext(backward: false), Keys.F3),
            Item("前を検索(&V)", () => FindNext(backward: true), Keys.Shift | Keys.F3),
            Item("置換(&R)...", () => ShowFindBar(replace: true), Keys.Control | Keys.H),
            Item("行へ移動(&G)...", GoToLine, Keys.Control | Keys.G),
            new ToolStripSeparator(),
            Item("すべて選択(&A)", () => Current?.Editor.SelectAll(), display: "Ctrl+A"),
            Item("日付と時刻(&D)", InsertDateTime, Keys.F5));
        _editMenu.DropDownOpening += (_, _) => UpdateEditMenu();
        menu.Items.Add(_editMenu);

        // 表示
        _statusBarItem = Item("ステータス バー(&S)", ToggleStatusBar, checkable: true);
        _wordWrapItem = Item("右端で折り返す(&W)", ToggleWordWrap, checkable: true);
        menu.Items.Add(TopMenu("表示(&V)",
            SubMenu("ズーム(&Z)",
                Item("拡大(&I)", () => SetZoom(_zoomPercent + ZoomStep), display: "Ctrl+プラス記号 (+)"),
                Item("縮小(&O)", () => SetZoom(_zoomPercent - ZoomStep), display: "Ctrl+マイナス記号 (-)"),
                Item("既定の倍率に戻す(&R)", () => SetZoom(100), display: "Ctrl+0")),
            _statusBarItem,
            _wordWrapItem));

        // 書式：MemoPad の追加機能（色変更）とフォント・テーマ
        _themeLightItem = Item("ライト(&L)", () => SetTheme("Light"), checkable: true);
        _themeDarkItem = Item("ダーク(&D)", () => SetTheme("Dark"), checkable: true);
        _themeSystemItem = Item("システム設定を使用する(&S)", () => SetTheme("System"), checkable: true);
        menu.Items.Add(TopMenu("書式(&O)",
            Item("フォント(&F)...", ChooseFont),
            new ToolStripSeparator(),
            Item("色変更(&C)...", ChangeColors),
            Item("配色パターン(&P)...", ChooseColorScheme),
            Item("既定の色に戻す(&D)", ResetColors),
            new ToolStripSeparator(),
            SubMenu("テーマ(&M)", _themeLightItem, _themeDarkItem, _themeSystemItem)));

        // ヘルプ
        menu.Items.Add(TopMenu("ヘルプ(&H)", Item("MemoPad について(&A)", ShowAbout)));
        return menu;
    }

    private ToolStripMenuItem TopMenu(string text, params ToolStripItem[] items)
    {
        var item = new ToolStripMenuItem(text) { Padding = new Padding(S(10), 0, S(10), 0) };
        item.DropDownItems.AddRange(items);
        PrepareDropDown(item);
        return item;
    }

    private ToolStripMenuItem SubMenu(string text, params ToolStripItem[] items)
    {
        var item = new ToolStripMenuItem(text) { Padding = ItemPadding };
        item.DropDownItems.AddRange(items);
        PrepareDropDown(item);
        return item;
    }

    private ToolStripMenuItem Item(string text, Action action, Keys shortcut = Keys.None, string? display = null, bool checkable = false)
    {
        var item = new ToolStripMenuItem(text) { Padding = ItemPadding };
        if (checkable) item.Tag = CheckableTag;
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        if (display is not null) item.ShortcutKeyDisplayString = display;
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>ポップアップの見た目（レンダラー・余白・角丸・チェック欄）を整える。</summary>
    private void PrepareDropDown(ToolStripMenuItem item)
    {
        var dropDown = item.DropDown;
        dropDown.Renderer = _renderer;
        dropDown.Padding = new Padding(S(4));
        dropDown.Font = Font;
        // メモ帳と同じで、チェックが付く項目のある menu だけ左にチェック欄を空ける。
        // 無い menu（ファイル・編集など）で欄を空けると、字下げが全角 2 文字分ほどになって右へ寄りすぎる
        if (dropDown is ToolStripDropDownMenu menu)
        {
            menu.ShowImageMargin = item.DropDownItems.OfType<ToolStripMenuItem>().Any(i => ReferenceEquals(i.Tag, CheckableTag));
        }
        item.DropDownOpening += (_, _) => ThemeService.ApplySmallRoundedCorners(dropDown.Handle);
    }

    /// <summary><paramref name="checkable"/> が true なら左にチェック欄を空ける。</summary>
    private ContextMenuStrip NewPopupMenu(bool checkable = false)
    {
        var menu = new ContextMenuStrip { Renderer = _renderer, Font = Font, Padding = new Padding(S(4)), ImageScalingSize = new Size(S(16), S(16)), ShowImageMargin = checkable };
        menu.Opening += (_, _) => ThemeService.ApplySmallRoundedCorners(menu.Handle);
        // Closed の中で破棄すると、その後の後始末が破棄済みオブジェクトに触れて例外になる。
        // メッセージを処理し終えてから捨てる
        menu.Closed += (_, _) => BeginInvoke(() => menu.Dispose());
        return menu;
    }

    private void RebuildRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();
        if (Settings.RecentFiles.Count == 0)
        {
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem("(なし)") { Enabled = false, Padding = ItemPadding });
            return;
        }
        var n = 1;
        foreach (var path in Settings.RecentFiles.ToList())
        {
            var item = new ToolStripMenuItem($"&{n++} {Path.GetFileName(path)}") { ToolTipText = path, Padding = ItemPadding };
            item.Click += (_, _) =>
            {
                if (File.Exists(path)) OpenFile(path);
                else
                {
                    MessageBox.Show(this, $"{path}\n\nファイルが見つかりません。一覧から削除します。", "MemoPad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Settings.RecentFiles.Remove(path);
                }
            };
            _recentMenu.DropDownItems.Add(item);
        }
        _recentMenu.DropDownItems.Add(new ToolStripSeparator());
        var clear = new ToolStripMenuItem("一覧を消去する(&C)") { Padding = ItemPadding };
        clear.Click += (_, _) => Settings.RecentFiles.Clear();
        _recentMenu.DropDownItems.Add(clear);
    }

    private void UpdateEditMenu()
    {
        var editor = Current?.Editor;
        _undoItem.Enabled = editor?.CanUndo == true;
        _redoItem.Enabled = editor?.CanRedo == true;
        _cutItem.Enabled = editor?.HasSelection == true;
        _copyItem.Enabled = editor?.HasSelection == true;
        _deleteItem.Enabled = editor?.HasSelection == true;
        _pasteItem.Enabled = editor?.CanPaste == true;
    }

    /// <summary>テキスト領域の右クリック メニュー（メモ帳の編集項目。Copilot 系は対象外）。</summary>
    private void ShowEditorContextMenu(DocumentTab tab)
    {
        var editor = tab.Editor;
        var menu = NewPopupMenu();
        void Add(string text, string gesture, bool enabled, Action action)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = gesture, Enabled = enabled, Padding = ItemPadding };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("元に戻す(&U)", "Ctrl+Z", editor.CanUndo, editor.Undo);
        Add("やり直し(&R)", "Ctrl+Y", editor.CanRedo, editor.Redo);
        menu.Items.Add(new ToolStripSeparator());
        Add("切り取り(&T)", "Ctrl+X", editor.HasSelection, editor.Cut);
        Add("コピー(&C)", "Ctrl+C", editor.HasSelection, editor.Copy);
        Add("貼り付け(&P)", "Ctrl+V", editor.CanPaste, editor.Paste);
        Add("削除(&D)", "Del", editor.HasSelection, editor.Delete);
        menu.Items.Add(new ToolStripSeparator());
        Add("すべて選択(&A)", "Ctrl+A", true, editor.SelectAll);
        Add("検索(&F)...", "Ctrl+F", true, () => ShowFindBar(replace: false));
        menu.Closed += (_, _) => editor.FocusEditor();
        menu.Show(Cursor.Position);
    }

    // =====================================================================
    // 編集：行へ移動・日付と時刻
    // =====================================================================

    private void GoToLine()
    {
        if (Current is not { } tab) return;
        var (line, _) = tab.Editor.GetCaretPosition();
        // 全文を読まずにエディタから行数をもらう（大きなファイルでも一瞬で済む）
        var lineCount = tab.Editor.GetLineCount();
        var result = AskGoToLine(line, lineCount);
        if (result is { } target) tab.Editor.GoToLine(target);
    }

    private int? AskGoToLine(int line, int lineCount)
    {
        WpfHost.Ensure(Settings.Theme);
        return Dialogs.GoToLineDialog.Ask(Handle, line, lineCount);
    }

    private void InsertDateTime()
    {
        // メモ帳の F5 と同じ形式（例：16:25 2026/09/06）
        if (Current is { } tab)
        {
            var editor = tab.Editor;
            editor.SelectedText = DateTime.Now.ToString("H:mm yyyy/MM/dd");
            editor.Select(editor.SelectionStart + editor.SelectionLength, 0);
            editor.FocusEditor();
        }
    }
}
