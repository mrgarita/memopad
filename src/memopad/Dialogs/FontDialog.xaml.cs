using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Memopad.Services;

namespace Memopad.Dialogs;

/// <summary>フォント ダイアログの結果。</summary>
public sealed record FontChoice(string Family, double Size, bool Bold, bool Italic);

/// <summary>
/// フォントの選択（ファミリ／スタイル／サイズ）。ファミリは日本語名（例：メイリオ）で表示し、
/// 設定には WPF が解決できる名前（FontFamily.Source）を保存する。
/// </summary>
public partial class FontDialog : Window
{
    private static readonly double[] StandardSizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 };

    private sealed record FamilyItem(string DisplayName, string Source, FontFamily Family);

    private readonly List<FamilyItem> _families;
    private FontChoice? _result;
    private bool _updating;

    public FontDialog(IntPtr owner, AppSettings settings)
    {
        InitializeComponent();
        new System.Windows.Interop.WindowInteropHelper(this).Owner = owner;

        var ja = XmlLanguage.GetLanguage("ja-jp");
        _families = Fonts.SystemFontFamilies
            .Select(f => new FamilyItem(f.FamilyNames.TryGetValue(ja, out var jp) ? jp : f.Source, f.Source, f))
            .OrderBy(f => f.DisplayName, StringComparer.Create(CultureInfo.GetCultureInfo("ja-JP"), ignoreCase: true))
            .ToList();
        FamilyList.ItemsSource = _families;

        foreach (var size in StandardSizes) SizeList.Items.Add(size.ToString(CultureInfo.InvariantCulture));

        _updating = true;
        FamilyList.SelectedItem = _families.FirstOrDefault(f =>
            string.Equals(f.Source, settings.FontFamily, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.DisplayName, settings.FontFamily, StringComparison.OrdinalIgnoreCase));
        FamilyList.ScrollIntoView(FamilyList.SelectedItem);
        StyleList.SelectedIndex = (settings.FontBold, settings.FontItalic) switch
        {
            (false, false) => 0,
            (false, true) => 1,
            (true, false) => 2,
            _ => 3,
        };
        SizeBox.Text = settings.FontSize.ToString(CultureInfo.InvariantCulture);
        SizeList.SelectedItem = SizeBox.Text;
        _updating = false;

        PreviewText.Background = new SolidColorBrush(ColorUtil.TryParse(settings.BackgroundColor) ?? ThemeService.DefaultBackground(settings.Theme));
        PreviewText.Foreground = new SolidColorBrush(ColorUtil.TryParse(settings.ForegroundColor) ?? ThemeService.DefaultForeground(settings.Theme));
        UpdatePreview();
    }

    public static FontChoice? Ask(IntPtr owner, AppSettings settings)
    {
        var dialog = new FontDialog(owner, settings);
        dialog.ShowDialog();
        return dialog._result;
    }

    private FamilyItem? SelectedFamily => FamilyList.SelectedItem as FamilyItem;
    private bool IsBold => StyleList.SelectedIndex is 2 or 3;
    private bool IsItalic => StyleList.SelectedIndex is 1 or 3;

    private double? SelectedSize =>
        double.TryParse(SizeBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v is >= 1 and <= 400 ? v : null;

    private void FamilyFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = FamilyFilterBox.Text.Trim();
        var selected = SelectedFamily;
        FamilyList.ItemsSource = q.Length == 0
            ? _families
            : _families.Where(f => f.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) || f.Source.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        if (selected is not null && ((IEnumerable<FamilyItem>)FamilyList.ItemsSource).Contains(selected)) FamilyList.SelectedItem = selected;
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void SizeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || SizeList.SelectedItem is not string s) return;
        _updating = true;
        SizeBox.Text = s;
        _updating = false;
        UpdatePreview();
    }

    private void SizeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        _updating = true;
        SizeList.SelectedItem = SizeList.Items.Cast<string>().FirstOrDefault(s => s == SizeBox.Text.Trim());
        _updating = false;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewText is null) return;
        if (SelectedFamily is { } f) PreviewText.FontFamily = f.Family;
        PreviewText.FontWeight = IsBold ? FontWeights.Bold : FontWeights.Normal;
        PreviewText.FontStyle = IsItalic ? FontStyles.Italic : FontStyles.Normal;
        if (SelectedSize is { } size) PreviewText.FontSize = size * 96.0 / 72.0;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFamily is not { } family)
        {
            MessageBox.Show(this, "フォントを選んでください。", "MemoPad - フォント", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (SelectedSize is not { } size)
        {
            MessageBox.Show(this, "サイズは 1 〜 400 の数値で指定してください。", "MemoPad - フォント", MessageBoxButton.OK, MessageBoxImage.Warning);
            SizeBox.Focus();
            return;
        }
        _result = new FontChoice(family.Source, size, IsBold, IsItalic);
        DialogResult = true;
    }
}
