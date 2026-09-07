using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Memopad.Dialogs;
using Memopad.Models;
using Memopad.Services;
using Memopad.Views;

namespace Memopad;

/// <summary>
/// メイン ウィンドウ。タブの管理、ファイル操作、検索／置換、表示設定、色の変更をまとめて扱う。
/// </summary>
public partial class MainWindow : Window
{
    private const int ZoomStep = 10;
    private const int ZoomMin = 10;
    private const int ZoomMax = 500;

    private readonly ObservableCollection<DocumentTab> _tabs = new();
    private int _zoomPercent = 100;
    private DocumentTab? _current;

    private static AppSettings Settings => App.Settings;

    public MainWindow()
    {
        InitializeComponent();

        Width = Settings.WindowWidth;
        Height = Settings.WindowHeight;
        if (Settings.WindowMaximized) WindowState = WindowState.Maximized;

        TabList.ItemsSource = _tabs;
        UpdateViewMenu();
        UpdateThemeMenu();
        UpdateTabBrushes();
        LoadAppIcon();

        // 起動時は常に新規の空タブから始める（セッション復元は作らない方針）
        AddTab(new Document());
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService.MakeOpaque(this, Settings.Theme);
    }

    // =====================================================================
    // 自前のタイトル行（WindowChrome）
    // =====================================================================

    /// <summary>タイトル行の左端に出すアプリ アイコン（exe に埋め込んだもの）。</summary>
    private void LoadAppIcon()
    {
        try
        {
            if (Environment.ProcessPath is not { } exe) return;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (icon is null) return;
            AppIcon.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16));
        }
        catch
        {
            // アイコンが取れなくても動作には影響しない
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        // 最大化中は「元に戻す」のグリフにする（Segoe Fluent Icons：E922＝最大化、E923＝元に戻す）
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "元に戻す" : "最大化";
    }

    // =====================================================================
    // タブ
    // =====================================================================

    private DocumentTab? Current => _current;

    private DocumentTab AddTab(Document document)
    {
        var tab = new DocumentTab(document);
        tab.Editor.ApplyAppearance(Settings, _zoomPercent);
        tab.Editor.CaretChanged += (_, _) => { if (tab == _current) UpdateCaretStatus(); };
        tab.Editor.ContentChanged += (_, _) => { if (tab == _current) UpdateContentStatus(); };
        // RichEdit はキーやホイールを自分で受け取るので、アプリ側の操作へ橋渡しする
        tab.Editor.Edit.CommandKey += HandleEditorCommandKey;
        tab.Editor.Edit.ZoomWheel += delta => SetZoom(_zoomPercent + (delta > 0 ? ZoomStep : -ZoomStep));
        tab.Editor.Edit.ContextMenuRequested += () => ShowEditorContextMenu(tab);
        tab.Editor.Edit.FilesDropped += files => { foreach (var f in files) if (File.Exists(f)) OpenFile(f); };
        document.PropertyChanged += (_, _) => { if (tab == _current) UpdateTitle(); };
        _tabs.Add(tab);
        TabList.SelectedItem = tab;
        return tab;
    }

    private void SelectTab(DocumentTab? tab)
    {
        _current = tab;
        EditorHost.Content = tab?.Editor;
        UpdateTitle();
        UpdateCaretStatus();
        UpdateContentStatus();
        if (tab is not null)
        {
            Dispatcher.BeginInvoke(() => tab.Editor.FocusEditor(), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>タブを閉じる。未保存なら確認する。閉じられなかった（キャンセル）場合は false。</summary>
    private bool CloseTab(DocumentTab tab)
    {
        if (!ConfirmDiscard(tab)) return false;

        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        if (_tabs.Count == 0)
        {
            // 最後のタブを閉じたらメモ帳と同じくウィンドウを閉じる
            _current = null;
            Close();
            return true;
        }
        TabList.SelectedItem = _tabs[Math.Min(index, _tabs.Count - 1)];
        return true;
    }

    /// <summary>未保存の変更があれば「保存／保存しない／キャンセル」を確認する。続行してよければ true。</summary>
    private bool ConfirmDiscard(DocumentTab tab)
    {
        if (!tab.Document.IsDirty) return true;
        TabList.SelectedItem = tab;
        return SaveChangesDialog.Ask(this, tab.Document.Title) switch
        {
            SaveChangesResult.Save => SaveTab(tab),
            SaveChangesResult.DontSave => true,
            _ => false,
        };
    }

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectTab(TabList.SelectedItem as DocumentTab);
    }

    private void TabList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 中クリックでタブを閉じる（ブラウザと同じ操作感）
        if (e.ChangedButton == MouseButton.Middle && (e.OriginalSource as DependencyObject)?.FindAncestorTab() is { } tab)
        {
            CloseTab(tab);
            e.Handled = true;
        }
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DocumentTab tab) CloseTab(tab);
    }

    private void NewTab_Executed(object sender, ExecutedRoutedEventArgs e) => AddTab(new Document());

    private void NewWindow_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;
        if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    private void CloseTab_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is { } tab) CloseTab(tab);
    }

    private void CloseWindow_Executed(object sender, ExecutedRoutedEventArgs e) => Close();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // 未保存のタブを順に確認する。ひとつでもキャンセルされたら閉じない
        foreach (var tab in _tabs.ToList())
        {
            if (!ConfirmDiscard(tab))
            {
                e.Cancel = true;
                return;
            }
        }
        Settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            Settings.WindowWidth = Width;
            Settings.WindowHeight = Height;
        }
        SettingsService.Save(Settings);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleGlobalKey(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    /// <summary>Ctrl+Tab（タブ切り替え）と Esc（検索バーを閉じる）。処理したら true。</summary>
    private bool HandleGlobalKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Tab && modifiers.HasFlag(ModifierKeys.Control) && _tabs.Count > 1)
        {
            var index = _current is null ? 0 : _tabs.IndexOf(_current);
            var delta = modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            TabList.SelectedItem = _tabs[(index + delta + _tabs.Count) % _tabs.Count];
            return true;
        }
        if (key == Key.Escape && FindBar.Visibility == Visibility.Visible)
        {
            HideFindBar();
            return true;
        }
        return false;
    }

    /// <summary>
    /// エディタ（RichEdit）で押されたショートカットをアプリのコマンドに変換する。
    /// WindowsFormsHost の中では WPF の InputBinding が効かないため、ここで Commands の KeyGesture と突き合わせる。
    /// 元に戻す・切り取り・コピー・貼り付け・すべて選択などは RichEdit 自身に任せる（false を返す）。
    /// </summary>
    private bool HandleEditorCommandKey(System.Windows.Forms.Keys keys)
    {
        var key = KeyInterop.KeyFromVirtualKey((int)(keys & System.Windows.Forms.Keys.KeyCode));
        var modifiers = ModifierKeys.None;
        if (keys.HasFlag(System.Windows.Forms.Keys.Control)) modifiers |= ModifierKeys.Control;
        if (keys.HasFlag(System.Windows.Forms.Keys.Shift)) modifiers |= ModifierKeys.Shift;
        if (keys.HasFlag(System.Windows.Forms.Keys.Alt)) modifiers |= ModifierKeys.Alt;

        if (HandleGlobalKey(key, modifiers)) return true;
        if (key == Key.Y && modifiers == ModifierKeys.Control)
        {
            Current?.Editor.Redo();
            return true;
        }
        // Alt＋アクセス キーでメニューを開く（Alt+F など）
        if (modifiers == ModifierKeys.Alt && key is >= Key.A and <= Key.Z)
        {
            var letter = (char)('A' + (key - Key.A));
            foreach (var item in MainMenu.Items.OfType<MenuItem>())
            {
                if (item.Header is string header && header.Contains($"(_{letter})", StringComparison.OrdinalIgnoreCase))
                {
                    item.Focus();
                    item.IsSubmenuOpen = true;
                    return true;
                }
            }
        }
        foreach (var command in Commands.All)
        {
            foreach (var gesture in command.InputGestures.OfType<KeyGesture>())
            {
                if (gesture.Key == key && gesture.Modifiers == modifiers)
                {
                    command.Execute(null, this);
                    return true;
                }
            }
        }
        return false;
    }

    // =====================================================================
    // ファイル
    // =====================================================================

    private const string OpenFilter = "テキスト文書 (*.txt)|*.txt|すべてのファイル (*.*)|*.*";

    // 名前を付けて保存では「ファイルの種類」でエンコードを選ぶ（WPF の共通ダイアログには
    // メモ帳のようなエンコード欄を足せないため、種類の一覧で代用する）
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
            TabList.SelectedItem = existing;
            return;
        }

        TextFileContent content;
        try
        {
            content = TextFileService.Read(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{path}\n\nファイルを開けませんでした。\n{ex.Message}", "memopad", MessageBoxButton.OK, MessageBoxImage.Error);
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
        TabList.SelectedItem = tab;
        Settings.PushRecentFile(path);
        UpdateTitle();
        UpdateContentStatus();
    }

    private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = OpenFilter,
            Multiselect = true,
            Title = "開く",
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var path in dialog.FileNames) OpenFile(path);
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is { } tab) SaveTab(tab);
    }

    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is { } tab) SaveTabAs(tab);
    }

    private void SaveAll_Executed(object sender, ExecutedRoutedEventArgs e)
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
        TabList.SelectedItem = tab;
        var currentIndex = Array.FindIndex(SaveFilters, f => f.Encoding == tab.Document.Encoding);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = string.Join("|", SaveFilters.Select(f => $"{f.Label}|{(f.Encoding is null ? "*.*" : "*.txt")}")),
            FilterIndex = currentIndex < 0 ? 1 : currentIndex + 1,
            FileName = tab.Document.FilePath is null ? $"{tab.Document.Title}.txt" : Path.GetFileName(tab.Document.FilePath),
            InitialDirectory = tab.Document.FilePath is null ? null : Path.GetDirectoryName(tab.Document.FilePath),
            DefaultExt = ".txt",
            AddExtension = true,
            Title = "名前を付けて保存",
        };
        if (dialog.ShowDialog(this) != true) return false;

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
            MessageBox.Show(this, $"{path}\n\n保存できませんでした。\n{ex.Message}", "memopad", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        tab.Document.FilePath = path;
        tab.Document.IsDirty = false;
        Settings.PushRecentFile(path);
        UpdateTitle();
        UpdateContentStatus();
        return true;
    }

    private void PageSetup_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        PageSetupDialog.Show(this, Settings);
    }

    private void Print_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is not { } tab) return;
        try
        {
            PrintService.Print(this, tab.Document.Title, tab.Editor.Text, Settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"印刷できませんでした。\n{ex.Message}", "memopad", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RecentFilesMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        RecentFilesMenu.Items.Clear();
        if (Settings.RecentFiles.Count == 0)
        {
            RecentFilesMenu.Items.Add(new MenuItem { Header = "(なし)", IsEnabled = false });
            return;
        }
        var n = 1;
        foreach (var path in Settings.RecentFiles)
        {
            var item = new MenuItem
            {
                Header = $"_{n++} {Path.GetFileName(path)}",
                ToolTip = path,
                Tag = path,
            };
            item.Click += (_, _) =>
            {
                if (File.Exists(path)) OpenFile(path);
                else
                {
                    MessageBox.Show(this, $"{path}\n\nファイルが見つかりません。一覧から削除します。", "memopad", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Settings.RecentFiles.Remove(path);
                }
            };
            RecentFilesMenu.Items.Add(item);
        }
        RecentFilesMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "一覧を消去する(_C)" };
        clear.Click += (_, _) => Settings.RecentFiles.Clear();
        RecentFilesMenu.Items.Add(clear);
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files) if (File.Exists(f)) OpenFile(f);
        }
    }

    // =====================================================================
    // 編集：検索・置換・行へ移動・日付と時刻
    // =====================================================================

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e) => ShowFindBar(replace: false);

    private void Replace_Executed(object sender, ExecutedRoutedEventArgs e) => ShowFindBar(replace: true);

    private void ShowFindBar(bool replace)
    {
        FindBar.Visibility = Visibility.Visible;
        var replaceVisibility = replace ? Visibility.Visible : Visibility.Collapsed;
        ReplaceLabel.Visibility = replaceVisibility;
        ReplaceTextBox.Visibility = replaceVisibility;
        ReplaceButton.Visibility = replaceVisibility;
        ReplaceAllButton.Visibility = replaceVisibility;
        FindStatusText.Visibility = replaceVisibility;
        FindStatusText.Text = "";

        // 選択中の文字列があれば検索語にする（1 行以内のときだけ）
        if (Current is { } tab && tab.Editor.SelectedText is { Length: > 0 } sel && !sel.Contains('\n'))
        {
            FindTextBox.Text = sel;
        }
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    private void HideFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        Current?.Editor.FocusEditor();
    }

    private void CloseFindBar_Click(object sender, RoutedEventArgs e) => HideFindBar();

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e) => FindStatusText.Text = "";

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNext(backward: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
    }

    private void ReplaceTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReplaceOne();
            e.Handled = true;
        }
    }

    private void FindNextButton_Click(object sender, RoutedEventArgs e) => FindNext(backward: false);
    private void FindPreviousButton_Click(object sender, RoutedEventArgs e) => FindNext(backward: true);
    private void FindNext_Executed(object sender, ExecutedRoutedEventArgs e) => FindNext(backward: false);
    private void FindPrevious_Executed(object sender, ExecutedRoutedEventArgs e) => FindNext(backward: true);
    private void ReplaceButton_Click(object sender, RoutedEventArgs e) => ReplaceOne();
    private void ReplaceAllButton_Click(object sender, RoutedEventArgs e) => ReplaceAll();

    private StringComparison FindComparison =>
        MatchCaseCheckBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>次（または前）の一致を選択する。見つからなければステータスに表示する。</summary>
    private bool FindNext(bool backward)
    {
        if (Current is not { } tab) return false;
        var query = FindTextBox.Text;
        if (query.Length == 0)
        {
            ShowFindBar(replace: ReplaceTextBox.Visibility == Visibility.Visible);
            return false;
        }

        var editor = tab.Editor;
        var text = editor.Text;
        var wrap = WrapAroundCheckBox.IsChecked == true;
        int index;
        if (backward)
        {
            var start = editor.SelectionStart - 1;
            index = start >= 0 ? text.LastIndexOf(query, Math.Min(start, text.Length - 1), FindComparison) : -1;
            if (index < 0 && wrap && text.Length > 0) index = text.LastIndexOf(query, text.Length - 1, FindComparison);
        }
        else
        {
            var start = editor.SelectionStart + editor.SelectionLength;
            index = start <= text.Length ? text.IndexOf(query, start, FindComparison) : -1;
            if (index < 0 && wrap) index = text.IndexOf(query, 0, FindComparison);
        }

        if (index < 0)
        {
            FindStatusText.Visibility = Visibility.Visible;
            FindStatusText.Text = $"\"{query}\" が見つかりません";
            System.Media.SystemSounds.Asterisk.Play();
            return false;
        }

        editor.Select(index, query.Length);
        FindStatusText.Text = "";
        return true;
    }

    /// <summary>選択中が一致していれば置き換え、次の一致へ進む。</summary>
    private void ReplaceOne()
    {
        if (Current is not { } tab) return;
        var editor = tab.Editor;
        var query = FindTextBox.Text;
        if (query.Length == 0) return;
        if (editor.SelectionLength > 0 && string.Equals(editor.SelectedText, query, FindComparison))
        {
            var start = editor.SelectionStart;
            editor.SelectedText = ReplaceTextBox.Text;
            editor.Select(start + ReplaceTextBox.Text.Length, 0);
        }
        FindNext(backward: false);
    }

    private void ReplaceAll()
    {
        if (Current is not { } tab) return;
        var editor = tab.Editor;
        var query = FindTextBox.Text;
        if (query.Length == 0) return;

        var text = editor.Text;
        var count = 0;
        var sb = new System.Text.StringBuilder(text.Length);
        var pos = 0;
        while (true)
        {
            var next = text.IndexOf(query, pos, FindComparison);
            if (next < 0) break;
            sb.Append(text, pos, next - pos).Append(ReplaceTextBox.Text);
            pos = next + query.Length;
            count++;
        }
        sb.Append(text, pos, text.Length - pos);

        if (count > 0)
        {
            // 元に戻す 1 回で戻せるよう、全文の差し替えを 1 つの編集にまとめる
            editor.SelectAll();
            editor.SelectedText = sb.ToString();
            editor.Select(0, 0);
        }
        FindStatusText.Visibility = Visibility.Visible;
        FindStatusText.Text = count > 0 ? $"{count} 件置換しました" : $"\"{query}\" が見つかりません";
    }

    private void GoTo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Current is not { } tab) return;
        var (line, _) = tab.Editor.GetCaretPosition();
        var lineCount = tab.Editor.Text.AsSpan().Count('\n') + 1;
        var result = GoToLineDialog.Ask(this, line, lineCount);
        if (result is { } target) tab.Editor.GoToLine(target);
    }

    private void InsertDateTime_Executed(object sender, ExecutedRoutedEventArgs e)
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

    // =====================================================================
    // 編集：元に戻す〜すべて選択（RichEdit に委譲）と右クリック メニュー
    // =====================================================================

    private void EditMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        var editor = Current?.Editor;
        UndoMenuItem.IsEnabled = editor?.CanUndo == true;
        RedoMenuItem.IsEnabled = editor?.CanRedo == true;
        CutMenuItem.IsEnabled = editor?.HasSelection == true;
        CopyMenuItem.IsEnabled = editor?.HasSelection == true;
        DeleteMenuItem.IsEnabled = editor?.HasSelection == true;
        PasteMenuItem.IsEnabled = editor?.CanPaste == true;
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Current?.Editor.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Current?.Editor.Redo();
    private void Cut_Click(object sender, RoutedEventArgs e) => Current?.Editor.Cut();
    private void Copy_Click(object sender, RoutedEventArgs e) => Current?.Editor.Copy();
    private void Paste_Click(object sender, RoutedEventArgs e) => Current?.Editor.Paste();
    private void Delete_Click(object sender, RoutedEventArgs e) => Current?.Editor.Delete();
    private void SelectAll_Click(object sender, RoutedEventArgs e) => Current?.Editor.SelectAll();

    /// <summary>テキスト領域の右クリック メニュー（メモ帳の編集項目。Copilot 系は対象外）。</summary>
    private void ShowEditorContextMenu(DocumentTab tab)
    {
        var editor = tab.Editor;
        var menu = new ContextMenu { PlacementTarget = editor, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        void Add(string header, string gesture, bool enabled, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("元に戻す(_U)", "Ctrl+Z", editor.CanUndo, editor.Undo);
        Add("やり直し(_R)", "Ctrl+Y", editor.CanRedo, editor.Redo);
        menu.Items.Add(new Separator());
        Add("切り取り(_T)", "Ctrl+X", editor.HasSelection, editor.Cut);
        Add("コピー(_C)", "Ctrl+C", editor.HasSelection, editor.Copy);
        Add("貼り付け(_P)", "Ctrl+V", editor.CanPaste, editor.Paste);
        Add("削除(_D)", "Del", editor.HasSelection, editor.Delete);
        menu.Items.Add(new Separator());
        Add("すべて選択(_A)", "Ctrl+A", true, editor.SelectAll);
        Add("検索(_F)...", "Ctrl+F", true, () => ShowFindBar(replace: false));
        menu.Closed += (_, _) => editor.FocusEditor();
        menu.IsOpen = true;
    }

    // =====================================================================
    // 表示：ズーム・ステータスバー・折り返し
    // =====================================================================

    private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e) => SetZoom(_zoomPercent + ZoomStep);
    private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e) => SetZoom(_zoomPercent - ZoomStep);
    private void ZoomReset_Executed(object sender, ExecutedRoutedEventArgs e) => SetZoom(100);

    private void SetZoom(int percent)
    {
        _zoomPercent = Math.Clamp(percent, ZoomMin, ZoomMax);
        ApplyAppearanceToAll();
        ZoomText.Text = $"{_zoomPercent}%";
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetZoom(_zoomPercent + (e.Delta > 0 ? ZoomStep : -ZoomStep));
            e.Handled = true;
        }
    }

    private void ToggleStatusBar_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Settings.ShowStatusBar = !Settings.ShowStatusBar;
        UpdateViewMenu();
    }

    private void ToggleWordWrap_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Settings.WordWrap = !Settings.WordWrap;
        UpdateViewMenu();
        ApplyAppearanceToAll();
    }

    private void UpdateViewMenu()
    {
        StatusBarMenuItem.IsChecked = Settings.ShowStatusBar;
        WordWrapMenuItem.IsChecked = Settings.WordWrap;
        StatusBar.Visibility = Settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
    }

    // =====================================================================
    // 書式：色変更・テーマ
    // =====================================================================

    /// <summary>「色変更」ダイアログで背景色と文字色をまとめて変える（v0.4.0 で背景色／文字色のサブメニューから統合）。</summary>
    private void ChangeColors_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ColorChangeDialog.Ask(this, Settings) is not { } result) return;
        Settings.BackgroundColor = result.Background;
        Settings.ForegroundColor = result.Foreground;
        ApplyAppearanceToAll();
    }

    /// <summary>「配色パターン」ダイアログで、目に優しい 16 パターンから背景色と文字色を一度に変える（v0.5.0）。</summary>
    private void ColorScheme_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ColorSchemeDialog.Ask(this, Settings) is not { } scheme) return;
        Settings.BackgroundColor = scheme.BackgroundHex;
        Settings.ForegroundColor = scheme.ForegroundHex;
        ApplyAppearanceToAll();
    }

    private void Font_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (FontDialog.Ask(this, Settings) is not { } choice) return;
        Settings.FontFamily = choice.Family;
        Settings.FontSize = choice.Size;
        Settings.FontBold = choice.Bold;
        Settings.FontItalic = choice.Italic;
        ApplyAppearanceToAll();
    }

    private void About_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        new AboutDialog(this).ShowDialog();
    }

    private void ResetColors_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Settings.BackgroundColor = "";
        Settings.ForegroundColor = "";
        ApplyAppearanceToAll();
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is string theme)
        {
            Settings.Theme = theme;
            ThemeService.Apply(theme);
            // テーマを切り替えると Fluent が Mica を掛け直すので、不透明化もやり直す
            Dispatcher.BeginInvoke(() => ThemeService.MakeOpaque(this, theme), System.Windows.Threading.DispatcherPriority.Loaded);
            UpdateThemeMenu();
            UpdateTabBrushes();
            ApplyAppearanceToAll();
        }
    }

    private void UpdateThemeMenu()
    {
        ThemeLightMenuItem.IsChecked = Settings.Theme == "Light";
        ThemeDarkMenuItem.IsChecked = Settings.Theme == "Dark";
        ThemeSystemMenuItem.IsChecked = Settings.Theme is not ("Light" or "Dark");
    }

    /// <summary>タブ ストリップとメニュー バーの色をテーマ（ライト／ダーク）に合わせる。選択中のタブは本文と同じ色にしてつなげて見せる。</summary>
    private void UpdateTabBrushes()
    {
        var dark = ThemeService.IsDark(Settings.Theme);
        // タイトル行は少し濃く、メニュー行と選択中のタブは同じ色にしてつなげる（メモ帳と同じ見せ方）
        Resources["TitleBarBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Color.FromRgb(0xE8, 0xE8, 0xE8));
        Resources["MenuBarBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xF9, 0xF9, 0xF9));
        Resources["TabSelectedBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xF9, 0xF9, 0xF9));
        Resources["TabHoverBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2A, 0x2A, 0x2A) : Color.FromRgb(0xDC, 0xDC, 0xDC));
        Resources["TabTextBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x1B, 0x1B, 0x1B));
        Resources["MenuPopupBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xF9, 0xF9, 0xF9));
        Resources["MenuPopupBorderBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x3F, 0x3F, 0x3F) : Color.FromRgb(0xE0, 0xE0, 0xE0));
    }

    /// <summary>全タブにフォント・色・折り返し・ズームを反映する。</summary>
    private void ApplyAppearanceToAll()
    {
        foreach (var tab in _tabs) tab.Editor.ApplyAppearance(Settings, _zoomPercent);
    }

    // =====================================================================
    // ステータスバー
    // =====================================================================

    private void UpdateTitle()
    {
        if (Current is { } tab)
        {
            Title = $"{(tab.Document.IsDirty ? "*" : "")}{tab.Document.Title} - memopad";
            LineEndingText.Text = tab.Document.LineEnding.DisplayName();
            EncodingText.Text = tab.Document.Encoding.DisplayName();
        }
        else
        {
            Title = "memopad";
        }
    }

    private void UpdateCaretStatus()
    {
        if (Current is { } tab)
        {
            var (line, column) = tab.Editor.GetCaretPosition();
            PositionText.Text = $"行 {line}、列 {column}";
        }
    }

    private void UpdateContentStatus()
    {
        if (Current is { } tab)
        {
            CharCountText.Text = $"{tab.Editor.GetCharacterCount():N0} 文字";
            UpdateTitle();
        }
    }

    private void LineEndingText_Click(object sender, MouseButtonEventArgs e)
    {
        if (Current is not { } tab) return;
        var menu = new ContextMenu();
        foreach (var kind in Enum.GetValues<LineEndingKind>())
        {
            var item = new MenuItem { Header = kind.DisplayName(), IsChecked = tab.Document.LineEnding == kind };
            item.Click += (_, _) =>
            {
                tab.Document.LineEnding = kind;
                tab.Document.IsDirty = true;
                UpdateTitle();
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = LineEndingText;
        menu.IsOpen = true;
    }

    private void EncodingText_Click(object sender, MouseButtonEventArgs e)
    {
        if (Current is not { } tab) return;
        var menu = new ContextMenu();
        foreach (var kind in Enum.GetValues<TextEncodingKind>())
        {
            var item = new MenuItem { Header = kind.DisplayName(), IsChecked = tab.Document.Encoding == kind };
            item.Click += (_, _) =>
            {
                tab.Document.Encoding = kind;
                tab.Document.IsDirty = true;
                UpdateTitle();
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = EncodingText;
        menu.IsOpen = true;
    }
}

internal static class VisualTreeExtensions
{
    /// <summary>クリックされた要素から、それが属するタブ（DocumentTab）を探す。</summary>
    public static DocumentTab? FindAncestorTab(this DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: DocumentTab tab }) return tab;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }
}
