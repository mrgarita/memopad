using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Memopad.Services;

namespace Memopad.Dialogs;

/// <summary>
/// 「色変更」ダイアログ。背景色と文字色を 1 つの画面で選ぶ（PICO-8 の 16 色＋カラーピッカー）。
/// 結果は "#RRGGBB"、既定の色に戻すときは空文字。
/// </summary>
public partial class ColorChangeDialog : Window
{
    /// <summary>選んだ色。null ならキャンセル。</summary>
    public sealed record Result(string Background, string Foreground);

    private readonly AppSettings _settings;
    private string _background;
    private string _foreground;
    private Result? _result;

    public ColorChangeDialog(IntPtr owner, AppSettings settings)
    {
        InitializeComponent();
        new System.Windows.Interop.WindowInteropHelper(this).Owner = owner;
        _settings = settings;
        _background = settings.BackgroundColor;
        _foreground = settings.ForegroundColor;

        FillSwatches(BackgroundSwatches, hex => { _background = hex; Refresh(); });
        FillSwatches(ForegroundSwatches, hex => { _foreground = hex; Refresh(); });

        // プレビューは現在のフォント設定で見せる
        PreviewText.FontFamily = new FontFamily(settings.FontFamily);
        PreviewText.FontSize = Math.Max(1, settings.FontSize * 96.0 / 72.0);
        PreviewText.FontWeight = settings.FontBold ? FontWeights.Bold : FontWeights.Normal;
        PreviewText.FontStyle = settings.FontItalic ? FontStyles.Italic : FontStyles.Normal;
        Refresh();
    }

    public static Result? Ask(IntPtr owner, AppSettings settings)
    {
        var dialog = new ColorChangeDialog(owner, settings);
        dialog.ShowDialog();
        return dialog._result;
    }

    /// <summary>16 色の見本ボタンを並べる。名前と色コードは UI Automation からも読めるようにしておく。</summary>
    private static void FillSwatches(UniformGrid grid, Action<string> pick)
    {
        foreach (var (name, color) in ColorUtil.StandardColors)
        {
            var hex = ColorUtil.ToHex(color);
            var button = new Button
            {
                Style = (Style)grid.FindResource("SwatchStyle"),
                Background = new SolidColorBrush(color),
                ToolTip = $"{name}  {hex}",
            };
            AutomationProperties.SetName(button, $"{name} {hex}");
            button.Click += (_, _) => pick(hex);
            grid.Children.Add(button);
        }
    }

    /// <summary>現在の選択を見本の枠・現在色・プレビューに反映する。</summary>
    private void Refresh()
    {
        var bg = ColorUtil.TryParse(_background) ?? ThemeService.DefaultBackground(_settings.Theme);
        var fg = ColorUtil.TryParse(_foreground) ?? ThemeService.DefaultForeground(_settings.Theme);

        MarkSelected(BackgroundSwatches, _background);
        MarkSelected(ForegroundSwatches, _foreground);
        BackgroundCurrent.Background = new SolidColorBrush(bg);
        ForegroundCurrent.Background = new SolidColorBrush(fg);
        BackgroundHex.Text = _background.Length == 0 ? $"既定（{ColorUtil.ToHex(bg)}）" : _background;
        ForegroundHex.Text = _foreground.Length == 0 ? $"既定（{ColorUtil.ToHex(fg)}）" : _foreground;
        PreviewBorder.Background = new SolidColorBrush(bg);
        PreviewText.Foreground = new SolidColorBrush(fg);
    }

    private static void MarkSelected(UniformGrid grid, string hex)
    {
        foreach (var child in grid.Children.OfType<Button>())
        {
            var color = ((SolidColorBrush)child.Background).Color;
            child.Tag = string.Equals(ColorUtil.ToHex(color), hex, StringComparison.OrdinalIgnoreCase) ? "Selected" : null;
        }
    }

    private void BackgroundOther_Click(object sender, RoutedEventArgs e)
    {
        var picked = ColorPicker.Pick(this, ColorUtil.TryParse(_background) ?? ThemeService.DefaultBackground(_settings.Theme));
        if (picked is { } c) { _background = ColorUtil.ToHex(c); Refresh(); }
    }

    private void ForegroundOther_Click(object sender, RoutedEventArgs e)
    {
        var picked = ColorPicker.Pick(this, ColorUtil.TryParse(_foreground) ?? ThemeService.DefaultForeground(_settings.Theme));
        if (picked is { } c) { _foreground = ColorUtil.ToHex(c); Refresh(); }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _background = "";
        _foreground = "";
        Refresh();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _result = new Result(_background, _foreground);
        DialogResult = true;
    }
}
