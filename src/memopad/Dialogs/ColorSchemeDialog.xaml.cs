using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Memopad.Services;

namespace Memopad.Dialogs;

/// <summary>
/// 「配色パターン」ダイアログ。目に優しい 16 パターンをカードで見せ、選んだものの背景色と文字色を返す。
/// </summary>
public partial class ColorSchemeDialog : Window
{
    private readonly AppSettings _settings;
    private ColorScheme? _selected;
    private ColorScheme? _result;

    public ColorSchemeDialog(IntPtr owner, AppSettings settings)
    {
        InitializeComponent();
        new System.Windows.Interop.WindowInteropHelper(this).Owner = owner;
        _settings = settings;
        _selected = ColorSchemes.Find(settings.BackgroundColor, settings.ForegroundColor);

        foreach (var scheme in ColorSchemes.All)
        {
            (scheme.IsDark ? DarkCards : LightCards).Children.Add(MakeCard(scheme));
        }

        PreviewText.FontFamily = new FontFamily(settings.FontFamily);
        PreviewText.FontSize = Math.Max(1, settings.FontSize * 96.0 / 72.0);
        PreviewText.FontWeight = settings.FontBold ? FontWeights.Bold : FontWeights.Normal;
        PreviewText.FontStyle = settings.FontItalic ? FontStyles.Italic : FontStyles.Normal;
        Refresh();
    }

    /// <summary>選んだパターン。null ならキャンセル。</summary>
    public static ColorScheme? Ask(IntPtr owner, AppSettings settings)
    {
        var dialog = new ColorSchemeDialog(owner, settings);
        dialog.ShowDialog();
        return dialog._result;
    }

    /// <summary>パターン 1 つ分のカード：名前（アクセント色）と見本文（文字色）を背景色の上に置く。</summary>
    private Button MakeCard(ColorScheme scheme)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = scheme.Name,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(scheme.Accent),
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "あいうえお Aa Bb 0123",
            Foreground = new SolidColorBrush(scheme.Foreground),
            FontSize = 12,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{scheme.BackgroundHex} / {scheme.ForegroundHex}",
            Foreground = new SolidColorBrush(scheme.Foreground),
            FontSize = 10,
            Opacity = 0.75,
            Margin = new Thickness(0, 2, 0, 0),
        });
        var button = new Button
        {
            Style = (Style)FindResource("CardStyle"),
            Background = new SolidColorBrush(scheme.Background),
            Content = panel,
            ToolTip = $"{scheme.Name}  背景 {scheme.BackgroundHex} / 文字 {scheme.ForegroundHex}",
            DataContext = scheme,
        };
        AutomationProperties.SetName(button, scheme.Name);
        button.Click += (_, _) => { _selected = scheme; Refresh(); };
        return button;
    }

    /// <summary>選択中のカードに枠を付け、プレビューを更新する。</summary>
    private void Refresh()
    {
        foreach (var grid in new[] { LightCards, DarkCards })
        {
            foreach (var card in grid.Children.OfType<Button>())
            {
                card.Tag = ReferenceEquals(card.DataContext, _selected) ? "Selected" : null;
            }
        }
        OkButton.IsEnabled = _selected is not null;
        if (_selected is { } s)
        {
            PreviewBorder.Background = new SolidColorBrush(s.Background);
            PreviewName.Text = s.Name;
            PreviewName.Foreground = new SolidColorBrush(s.Accent);
            PreviewText.Foreground = new SolidColorBrush(s.Foreground);
        }
        else
        {
            // 未選択：現在の色で見せる
            var bg = ColorUtil.TryParse(_settings.BackgroundColor) ?? ThemeService.DefaultBackground(_settings.Theme);
            var fg = ColorUtil.TryParse(_settings.ForegroundColor) ?? ThemeService.DefaultForeground(_settings.Theme);
            PreviewBorder.Background = new SolidColorBrush(bg);
            PreviewName.Text = "（現在の色。パターンをクリックすると切り替わります）";
            PreviewName.Foreground = new SolidColorBrush(fg);
            PreviewText.Foreground = new SolidColorBrush(fg);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _result = _selected;
        DialogResult = true;
    }
}
