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
        BuildColorMenus();
        UpdateViewMenu();
        UpdateThemeMenu();

        // 起動時は常に新規の空タブから始める（セッション復元は作らない方針）
        AddTab(new Document());
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
            Dispatcher.BeginInvoke(() => tab.Editor.Editor.Focus(), System.Windows.Threading.DispatcherPriority.Input);
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
        // Ctrl+Tab / Ctrl+Shift+Tab でタブを切り替える
        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _tabs.Count > 1)
        {
            var index = _current is null ? 0 : _tabs.IndexOf(_current);
            var delta = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            TabList.SelectedItem = _tabs[(index + delta + _tabs.Count) % _tabs.Count];
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible)
        {
            HideFindBar();
            e.Handled = true;
        }
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
        var tab = Current is { } c && c.Document.FilePath is null && !c.Document.IsDirty && c.Editor.Editor.Text.Length == 0
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
            TextFileService.Write(path, tab.Editor.Editor.Text, tab.Document.Encoding, tab.Document.LineEnding);
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
            PrintService.Print(this, tab.Document.Title, tab.Editor.Editor.Text, Settings);
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
        if (Current is { } tab && tab.Editor.Editor.SelectedText is { Length: > 0 } sel && !sel.Contains('\n'))
        {
            FindTextBox.Text = sel;
        }
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    private void HideFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        Current?.Editor.Editor.Focus();
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

        var editor = tab.Editor.Editor;
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
        editor.ScrollToLine(editor.GetLineIndexFromCharacterIndex(index));
        FindStatusText.Text = "";
        return true;
    }

    /// <summary>選択中が一致していれば置き換え、次の一致へ進む。</summary>
    private void ReplaceOne()
    {
        if (Current is not { } tab) return;
        var editor = tab.Editor.Editor;
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
        var editor = tab.Editor.Editor;
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
        var lineCount = tab.Editor.Editor.Text.AsSpan().Count('\n') + 1;
        var result = GoToLineDialog.Ask(this, line, lineCount);
        if (result is { } target) tab.Editor.GoToLine(target);
    }

    private void InsertDateTime_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        // メモ帳の F5 と同じ形式（例：16:25 2026/09/06）
        if (Current is { } tab)
        {
            var editor = tab.Editor.Editor;
            editor.SelectedText = DateTime.Now.ToString("H:mm yyyy/MM/dd");
            editor.Select(editor.SelectionStart + editor.SelectionLength, 0);
            editor.Focus();
        }
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
    // 書式：背景色・文字色・テーマ
    // =====================================================================

    private void BuildColorMenus()
    {
        FillColorMenu(BackgroundColorMenu, hex => { Settings.BackgroundColor = hex; ApplyAppearanceToAll(); }, () => Settings.BackgroundColor);
        FillColorMenu(ForegroundColorMenu, hex => { Settings.ForegroundColor = hex; ApplyAppearanceToAll(); }, () => Settings.ForegroundColor);
    }

    /// <summary>標準色 16 色＋「その他の色...」（カラーピッカー）のサブメニューを作る。</summary>
    private void FillColorMenu(MenuItem parent, Action<string> apply, Func<string> current)
    {
        parent.Items.Clear();
        foreach (var (name, color) in ColorUtil.StandardColors)
        {
            var hex = ColorUtil.ToHex(color);
            var item = new MenuItem
            {
                Header = $"{name}  {hex}",
                Icon = new Border
                {
                    Width = 16, Height = 16,
                    Background = new SolidColorBrush(color),
                    BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                },
            };
            item.Click += (_, _) => apply(hex);
            parent.Items.Add(item);
        }
        parent.Items.Add(new Separator());
        var other = new MenuItem { Header = "その他の色(_M)..." };
        other.Click += (_, _) =>
        {
            var picked = ColorPicker.Pick(this, ColorUtil.TryParse(current()));
            if (picked is { } c) apply(ColorUtil.ToHex(c));
        };
        parent.Items.Add(other);
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
            UpdateThemeMenu();
            ApplyAppearanceToAll();
        }
    }

    private void UpdateThemeMenu()
    {
        ThemeLightMenuItem.IsChecked = Settings.Theme == "Light";
        ThemeDarkMenuItem.IsChecked = Settings.Theme == "Dark";
        ThemeSystemMenuItem.IsChecked = Settings.Theme is not ("Light" or "Dark");
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
