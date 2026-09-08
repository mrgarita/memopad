using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Memopad.Services;

namespace Memopad.Views;

/// <summary>
/// MenuStrip とポップアップ メニューを Windows 11（Fluent）風に描く。角丸のホバー、薄い枠線、
/// Segoe Fluent Icons のチェックとサブメニュー矢印。ライト／ダークは Palette で切り替える。
/// </summary>
public sealed class FluentMenuRenderer : ToolStripRenderer
{
    private Font? _glyphFont;
    private int _glyphDpi;

    public FluentMenuRenderer(ThemePalette palette)
    {
        Palette = palette;
    }

    public ThemePalette Palette { get; set; }

    private static int S(Control c, int v) => (int)Math.Round(v * c.DeviceDpi / 96.0);

    private Font GlyphFont(Control c)
    {
        if (_glyphFont is null || _glyphDpi != c.DeviceDpi)
        {
            _glyphFont?.Dispose();
            _glyphFont = UiStyle.PixelFont(UiStyle.GlyphFontName, S(c, 12));
            _glyphDpi = c.DeviceDpi;
        }
        return _glyphFont;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var color = e.ToolStrip is ToolStripDropDown ? Palette.Popup : Palette.MenuBar;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, e.ToolStrip.ClientRectangle);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is not ToolStripDropDown) return;
        var r = e.ToolStrip.ClientRectangle;
        r.Width--;
        r.Height--;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiStyle.RoundedRect(r, S(e.ToolStrip, 8));
        using var pen = new Pen(Palette.PopupBorder);
        g.DrawPath(pen, path);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // 左の余白は地の色のまま（既定の灰色の帯は描かない）
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        if (!item.Enabled || !(item.Selected || item.Pressed)) return;
        var top = item.Owner is MenuStrip;
        var r = new Rectangle(Point.Empty, item.Size);
        r.Inflate(top ? -S(item.Owner!, 2) : -S(item.Owner!, 4), top ? -S(item.Owner!, 3) : -S(item.Owner!, 1));
        UiStyle.FillRounded(e.Graphics, r, S(item.Owner!, 4), Palette.TabHover);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // ショートカット表示（右寄せで描かれる）は薄い色にする
        var shortcut = (e.TextFormat & TextFormatFlags.Right) == TextFormatFlags.Right;
        e.TextColor = !e.Item.Enabled ? Palette.MutedText : shortcut ? Palette.MutedText : Palette.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var owner = e.Item.Owner!;
        var r = e.ImageRectangle;
        r.Inflate(S(owner, 4), S(owner, 4));
        TextRenderer.DrawText(e.Graphics, UiStyle.GlyphCheck, GlyphFont(owner), r, Palette.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var owner = e.Item.Owner!;
        var y = e.Item.Height / 2;
        using var pen = new Pen(Palette.Separator);
        e.Graphics.DrawLine(pen, S(owner, 10), y, e.Item.Width - S(owner, 10), y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item?.Owner is not { } owner) { base.OnRenderArrow(e); return; }
        var r = e.ArrowRectangle;
        r.Inflate(S(owner, 6), S(owner, 6));
        TextRenderer.DrawText(e.Graphics, UiStyle.GlyphChevronRight, GlyphFont(owner), r, e.Item.Enabled ? Palette.Text : Palette.MutedText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
