using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Memopad.Services;

namespace Memopad.Views;

/// <summary>
/// ステータスバー：行・列／文字数／ズーム／改行コード／エンコード（メモ帳と同じ並び）。
/// 改行コードとエンコードはクリックで変更メニューを出す。
/// </summary>
public sealed class StatusBarPanel : Control
{
    private ThemePalette _palette = ThemePalette.Light;
    private string _position = "行 1、列 1";
    private string _charCount = "0 文字";
    private string _zoom = "100%";
    private string _lineEnding = "Windows (CRLF)";
    private string _encoding = "UTF-8";
    private Rectangle _lineEndingRect;
    private Rectangle _encodingRect;
    private readonly ToolTip _tip = new() { InitialDelay = 700, ReshowDelay = 500 };
    private string? _tipText;

    public StatusBarPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    /// <summary>改行コードの表示がクリックされた（引数はメニューを出す位置：スクリーン座標）。</summary>
    public event Action<Point>? LineEndingClicked;

    /// <summary>エンコードの表示がクリックされた。</summary>
    public event Action<Point>? EncodingClicked;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette
    {
        get => _palette;
        set { _palette = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PositionText { get => _position; set { _position = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string CharCountText { get => _charCount; set { _charCount = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ZoomText { get => _zoom; set { _zoom = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string LineEndingText { get => _lineEnding; set { _lineEnding = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EncodingText { get => _encoding; set { _encoding = value; Invalidate(); } }

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    private const TextFormatFlags Flags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_palette.Window);
        var h = Height;
        var pad = S(8);
        using var sepPen = new Pen(_palette.Separator);

        // 左：行・列 | 文字数
        var x = S(4);
        x = DrawItem(g, _position, x, S(110), pad, h);
        g.DrawLine(sepPen, x, S(8), x, h - S(8));
        x = DrawItem(g, _charCount, x, S(80), pad, h);
        g.DrawLine(sepPen, x, S(8), x, h - S(8));

        // 右：ズーム  改行コード  エンコード（右端から詰める）
        var right = Width - S(4) - pad;
        var encW = Math.Max(S(110), Measure(_encoding));
        _encodingRect = new Rectangle(right - encW, 0, encW, h);
        TextRenderer.DrawText(g, _encoding, Font, _encodingRect, _palette.Text, Flags);
        right = _encodingRect.Left - S(16);
        var leW = Math.Max(S(120), Measure(_lineEnding));
        _lineEndingRect = new Rectangle(right - leW, 0, leW, h);
        TextRenderer.DrawText(g, _lineEnding, Font, _lineEndingRect, _palette.Text, Flags);
        right = _lineEndingRect.Left - S(16);
        var zoomW = Math.Max(S(50), Measure(_zoom));
        TextRenderer.DrawText(g, _zoom, Font, new Rectangle(right - zoomW, 0, zoomW, h), _palette.Text, Flags);
    }

    private int Measure(string text) => TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, Height), Flags).Width;

    private int DrawItem(Graphics g, string text, int x, int minWidth, int pad, int h)
    {
        var w = Math.Max(minWidth, Measure(text));
        TextRenderer.DrawText(g, text, Font, new Rectangle(x + pad, 0, w, h), _palette.Text, Flags);
        return x + pad + w + pad;
    }

    protected override void WndProc(ref Message m)
    {
        // 下端と左右の端はウィンドウの縁なので、リサイズの判定を親フォームへ譲る
        if (ChromeHitTest.TryPassToFrame(this, ref m)) return;
        base.WndProc(ref m);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        string? tip = null;
        if (_lineEndingRect.Contains(e.Location)) tip = "クリックして改行コードを変更";
        else if (_encodingRect.Contains(e.Location)) tip = "クリックしてエンコードを変更";
        Cursor = tip is null ? Cursors.Default : Cursors.Hand;
        if (tip != _tipText)
        {
            _tipText = tip;
            _tip.SetToolTip(this, tip);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // メニューは右端を基準に左へ開くので、項目の右端を渡す
            if (_lineEndingRect.Contains(e.Location)) LineEndingClicked?.Invoke(PointToScreen(new Point(_lineEndingRect.Right, 0)));
            else if (_encodingRect.Contains(e.Location)) EncodingClicked?.Invoke(PointToScreen(new Point(_encodingRect.Right, 0)));
        }
        base.OnMouseClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
