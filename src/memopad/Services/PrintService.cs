using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Memopad.Services;

/// <summary>
/// 印刷。本文を FlowDocument にしてページ分割し、各ページにヘッダー／フッターを重ねて印刷する。
/// 余白・向き・ヘッダー／フッターはページ設定（AppSettings）に従う。
/// </summary>
public static class PrintService
{
    private const double MmToPx = 96.0 / 25.4;

    public static void Print(Window owner, string documentTitle, string text, AppSettings settings)
    {
        var dialog = new PrintDialog();
        dialog.PrintTicket.PageOrientation = settings.PrintLandscape ? PageOrientation.Landscape : PageOrientation.Portrait;
        if (dialog.ShowDialog() != true) return;

        var pageWidth = dialog.PrintableAreaWidth;
        var pageHeight = dialog.PrintableAreaHeight;
        var margins = new Thickness(
            settings.MarginLeft * MmToPx, settings.MarginTop * MmToPx,
            settings.MarginRight * MmToPx, settings.MarginBottom * MmToPx);

        var family = new FontFamily(settings.FontFamily);
        var fontSizePx = settings.FontSize * 96.0 / 72.0;

        var doc = new FlowDocument
        {
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            PagePadding = margins,
            ColumnWidth = double.PositiveInfinity,   // 段組みにしない
            FontFamily = family,
            FontSize = fontSizePx,
            FontWeight = settings.FontBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = settings.FontItalic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = Brushes.Black,   // 画面の色に関わらず、紙には黒で刷る
            Background = Brushes.White,
        };
        // 改行は Run の中にそのまま入れれば改行として描画される
        doc.Blocks.Add(new Paragraph(new Run(text)) { Margin = new Thickness(0) });

        var inner = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        var paginator = new HeaderFooterPaginator(inner, documentTitle, settings, family, fontSizePx, margins);
        dialog.PrintDocument(paginator, $"{documentTitle} - memopad");
    }

    /// <summary>ページ設定の置換コード（&amp;f &amp;p &amp;d &amp;t &amp;&amp;）を展開する。</summary>
    public static string ExpandCodes(string template, string title, int pageNumber, DateTime now)
    {
        var sb = new System.Text.StringBuilder(template.Length + 16);
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '&' && i + 1 < template.Length)
            {
                var code = char.ToLowerInvariant(template[++i]);
                switch (code)
                {
                    case 'f': sb.Append(title); break;
                    case 'p': sb.Append(pageNumber.ToString(CultureInfo.InvariantCulture)); break;
                    case 'd': sb.Append(now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)); break;
                    case 't': sb.Append(now.ToString("H:mm", CultureInfo.InvariantCulture)); break;
                    case '&': sb.Append('&'); break;
                    default: sb.Append('&').Append(template[i]); break;
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>本文のページにヘッダー／フッターを描き足すページネーター。</summary>
    private sealed class HeaderFooterPaginator : DocumentPaginator
    {
        private readonly DocumentPaginator _inner;
        private readonly string _title;
        private readonly AppSettings _settings;
        private readonly Typeface _typeface;
        private readonly double _fontSize;
        private readonly Thickness _margins;
        private readonly DateTime _now = DateTime.Now;

        public HeaderFooterPaginator(DocumentPaginator inner, string title, AppSettings settings, FontFamily family, double fontSize, Thickness margins)
        {
            _inner = inner;
            _title = title;
            _settings = settings;
            _typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            _fontSize = Math.Max(8, Math.Min(fontSize, 14));   // ヘッダー／フッターは本文より大きくしない
            _margins = margins;
        }

        public override bool IsPageCountValid => _inner.IsPageCountValid;
        public override int PageCount => _inner.PageCount;
        public override Size PageSize { get => _inner.PageSize; set => _inner.PageSize = value; }
        public override IDocumentPaginatorSource Source => _inner.Source;

        public override DocumentPage GetPage(int pageNumber)
        {
            var page = _inner.GetPage(pageNumber);
            var container = new ContainerVisual();
            container.Children.Add(page.Visual);

            var overlay = new DrawingVisual();
            using (var dc = overlay.RenderOpen())
            {
                var width = PageSize.Width - _margins.Left - _margins.Right;
                var header = ExpandCodes(_settings.PrintHeader, _title, pageNumber + 1, _now);
                var footer = ExpandCodes(_settings.PrintFooter, _title, pageNumber + 1, _now);
                if (header.Length > 0)
                {
                    var ft = Format(header, width);
                    // 上余白の中央付近に、中央揃えで描く
                    dc.DrawText(ft, new Point(_margins.Left + (width - ft.Width) / 2, Math.Max(0, _margins.Top / 2 - ft.Height / 2)));
                }
                if (footer.Length > 0)
                {
                    var ft = Format(footer, width);
                    dc.DrawText(ft, new Point(_margins.Left + (width - ft.Width) / 2, PageSize.Height - _margins.Bottom / 2 - ft.Height / 2));
                }
            }
            container.Children.Add(overlay);

            return new DocumentPage(container, PageSize, page.BleedBox, page.ContentBox);
        }

        private FormattedText Format(string text, double maxWidth) => new(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.Black, 1.0)
        {
            MaxTextWidth = Math.Max(10, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
    }
}
