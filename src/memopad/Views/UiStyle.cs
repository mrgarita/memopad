using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Memopad.Views;

/// <summary>メイン ウィンドウの描画で共通に使うフォントと図形。</summary>
internal static class UiStyle
{
    /// <summary>UI の文字（メニュー・タブ・ステータスバー）に使うフォント名。日本語 Windows の既定は Yu Gothic UI。</summary>
    public static readonly string UiFontName = SystemFonts.MessageBoxFont?.Name ?? "Segoe UI";

    /// <summary>アイコン フォント。Windows 11 は Segoe Fluent Icons、無ければ Windows 10 の Segoe MDL2 Assets。</summary>
    public static readonly string GlyphFontName = HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    // Segoe Fluent Icons のグリフ（E921 最小化、E922 最大化、E923 元に戻す、E8BB 閉じる、E73E チェック、E76C 右矢印）
    public const string GlyphMinimize = "\uE921";
    public const string GlyphMaximize = "\uE922";
    public const string GlyphRestore = "\uE923";
    public const string GlyphClose = "\uE8BB";
    public const string GlyphCheck = "\uE73E";
    public const string GlyphChevronRight = "\uE76C";

    /// <summary>中央寄せ（ボタンのグリフ）。</summary>
    public const TextFormatFlags CenterFlags =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

    /// <summary>左寄せ・縦中央・入り切らなければ末尾を…にする（タブの表題）。</summary>
    public const TextFormatFlags TabTextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

    /// <summary>ピクセル指定でフォントを作る（表示スケールに合わせて呼び出し側が px を換算する）。</summary>
    public static Font PixelFont(string family, int px, FontStyle style = FontStyle.Regular) =>
        new(family, Math.Max(1, px), style, GraphicsUnit.Pixel);

    /// <summary>角丸の矩形。topOnly なら下の 2 角は直角（タブ用）。</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius, bool topOnly = false)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        var d = radius * 2;
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        if (topOnly)
        {
            path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
        }
        else
        {
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        }
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Rectangle r, int radius, Color color, bool topOnly = false)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(r, radius, topOnly);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
        g.SmoothingMode = old;
    }

    private static bool HasFont(string name)
    {
        try
        {
            using var f = new FontFamily(name);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
