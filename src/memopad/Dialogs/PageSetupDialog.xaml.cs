using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Memopad.Services;

namespace Memopad.Dialogs;

/// <summary>ページ設定。値は AppSettings に直接書き戻す。</summary>
public partial class PageSetupDialog : Window
{
    private readonly AppSettings _settings;

    public PageSetupDialog(IntPtr owner, AppSettings settings)
    {
        InitializeComponent();
        new System.Windows.Interop.WindowInteropHelper(this).Owner = owner;
        _settings = settings;

        PortraitRadio.IsChecked = !settings.PrintLandscape;
        LandscapeRadio.IsChecked = settings.PrintLandscape;
        LeftBox.Text = Fmt(settings.MarginLeft);
        RightBox.Text = Fmt(settings.MarginRight);
        TopBox.Text = Fmt(settings.MarginTop);
        BottomBox.Text = Fmt(settings.MarginBottom);
        HeaderBox.Text = settings.PrintHeader;
        FooterBox.Text = settings.PrintFooter;
    }

    public static void Show(IntPtr owner, AppSettings settings) => new PageSetupDialog(owner, settings).ShowDialog();

    private static string Fmt(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static bool TryMargin(TextBox box, out double value)
    {
        return double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value is >= 0 and <= 100;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryMargin(LeftBox, out var l) || !TryMargin(RightBox, out var r) || !TryMargin(TopBox, out var t) || !TryMargin(BottomBox, out var b))
        {
            MessageBox.Show(this, "余白は 0 〜 100 の数値（mm）で指定してください。", "MemoPad - ページ設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.PrintLandscape = LandscapeRadio.IsChecked == true;
        _settings.MarginLeft = l;
        _settings.MarginRight = r;
        _settings.MarginTop = t;
        _settings.MarginBottom = b;
        _settings.PrintHeader = HeaderBox.Text;
        _settings.PrintFooter = FooterBox.Text;
        DialogResult = true;
    }
}
