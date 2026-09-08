using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Memopad.Services;

namespace Memopad.Views;

/// <summary>
/// タイトル行に置くボタン（タブの×、＋、最小化／最大化／閉じる）。
/// 見た目は自前で描くが、支援技術に名前と役割が伝わるよう Button を継承している
/// （素の Control は UI Automation から名前の無い Pane にしか見えない）。
/// </summary>
internal sealed class TitleBarButton : Button
{
    private bool _hot;
    private Font? _font;
    private int _fontDpi;

    public TitleBarButton(string glyph, bool useGlyphFont, int fontSize)
    {
        Glyph = glyph;
        UseGlyphFont = useGlyphFont;
        FontSize = fontSize;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);   // フォーカスは本文に置いたままにする
        TabStop = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Text = string.Empty;                          // 表示は OnPaint で自前に描く
        AccessibleRole = AccessibleRole.PushButton;
    }

    /// <summary>描く文字（アイコン フォントのグリフ、または「＋」「✕」）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; set; }

    /// <summary>アイコン フォント（Segoe Fluent Icons）で描くか。</summary>
    public bool UseGlyphFont { get; }

    /// <summary>文字の大きさ（論理 px）。</summary>
    public int FontSize { get; }

    /// <summary>閉じるボタンはホバーで赤くする（Windows 11 と同じ）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsCloseButton { get; init; }

    /// <summary>角丸の半径（論理 px）。0 ならホバーを四角く塗る（キャプション ボタン）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; init; } = 4;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette { get; set; } = ThemePalette.Light;

    /// <summary>マウスが載っているか（親のタブが自分の見た目を合わせるために読む）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsHot => _hot;

    /// <summary>ホバー状態が変わった（タブが×の上でも自分のホバー表示を保つために使う）。</summary>
    public event Action? HotChanged;

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    private Font GlyphFont
    {
        get
        {
            if (_font is null || _fontDpi != DeviceDpi)
            {
                _font?.Dispose();
                _font = UiStyle.PixelFont(UseGlyphFont ? UiStyle.GlyphFontName : UiStyle.UiFontName, S(FontSize));
                _fontDpi = DeviceDpi;
            }
            return _font;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var color = Palette.Text;
        if (_hot)
        {
            if (IsCloseButton)
            {
                using var brush = new SolidBrush(Color.FromArgb(0xC4, 0x2B, 0x1C));
                g.FillRectangle(brush, ClientRectangle);
                color = Color.White;
            }
            else if (CornerRadius > 0)
            {
                UiStyle.FillRounded(g, ClientRectangle, S(CornerRadius), Palette.TabHover);
            }
            else
            {
                using var brush = new SolidBrush(Palette.TabHover);
                g.FillRectangle(brush, ClientRectangle);
            }
        }
        TextRenderer.DrawText(g, Glyph, GlyphFont, ClientRectangle, color, UiStyle.CenterFlags);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hot = true;
        Invalidate();
        HotChanged?.Invoke();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = false;
        Invalidate();
        HotChanged?.Invoke();
        base.OnMouseLeave(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _font?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// タイトル行のタブ 1 つ。選択中とホバーで背景（上だけ角丸）を塗り、表題と閉じるボタンを載せる。
/// 支援技術にはタブ（PageTab）として見える。
/// </summary>
internal sealed class TabButton : Button
{
    private readonly TitleBarButton _close;
    private ThemePalette _palette = ThemePalette.Light;
    private bool _hot;
    private bool _selected;
    private Font? _font;
    private int _fontDpi;

    public TabButton(DocumentTab tab)
    {
        Tab = tab;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);   // フォーカスは本文に置いたままにする
        TabStop = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Text = string.Empty;                          // 表示は OnPaint で自前に描く
        AccessibleRole = AccessibleRole.PageTab;

        _close = new TitleBarButton("✕", useGlyphFont: false, fontSize: 11);
        _close.Click += (_, _) => CloseRequested?.Invoke(Tab);
        _close.HotChanged += Invalidate;   // ×の上でもタブのホバー表示を保つ
        Controls.Add(_close);
        UpdateText();
    }

    public DocumentTab Tab { get; }

    /// <summary>タブがクリックされた。</summary>
    public event Action<DocumentTab>? Selecting;

    /// <summary>×または中クリックで閉じる要求。</summary>
    public event Action<DocumentTab>? CloseRequested;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Invalidate(); } }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette
    {
        get => _palette;
        set { _palette = value; _close.Palette = value; Invalidate(); }
    }

    /// <summary>表題（未保存の●を含む）とツールチップ用の名前を最新にする。</summary>
    public void UpdateText()
    {
        AccessibleName = Tab.Document.TabTitle;
        _close.AccessibleName = $"{Tab.Document.Title} を閉じる";
        Invalidate();
    }

    /// <summary>表題を収めるのに必要な幅。</summary>
    public int PreferredWidth =>
        TextRenderer.MeasureText(Tab.Document.TabTitle, TextFont, new Size(int.MaxValue, Math.Max(1, Height)), UiStyle.TabTextFlags).Width
        + S(12) + S(8) + S(22) + S(4);

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    private Font TextFont
    {
        get
        {
            if (_font is null || _fontDpi != DeviceDpi)
            {
                _font?.Dispose();
                _font = UiStyle.PixelFont(UiStyle.UiFontName, S(13));
                _fontDpi = DeviceDpi;
            }
            return _font;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var size = S(22);
        _close.SetBounds(Width - S(4) - size, (Height - size) / 2, size, size);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // タブの外（角丸の外側）はタイトル行の色で塗る
        g.Clear(_palette.TitleBar);
        var hot = _hot || _close.IsHot;
        var fill = _selected ? _palette.MenuBar : hot ? _palette.TabHover : _palette.TitleBar;
        if (_selected || hot) UiStyle.FillRounded(g, ClientRectangle, S(6), fill, topOnly: true);
        _close.BackColor = fill;

        var textRect = new Rectangle(S(12), 0, Math.Max(0, Width - S(12) - S(8) - S(22) - S(4)), Height);
        TextRenderer.DrawText(g, Tab.Document.TabTitle, TextFont, textRect, _palette.Text, UiStyle.TabTextFlags);
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        // 押した時点で切り替える（メモ帳やブラウザと同じ操作感）
        if (e.Button == MouseButtons.Left) Selecting?.Invoke(Tab);
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        // 中クリックでタブを閉じる（ブラウザと同じ操作感）
        if (e.Button == MouseButtons.Middle && ClientRectangle.Contains(e.Location)) CloseRequested?.Invoke(Tab);
        base.OnMouseUp(e);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new TabAccessibleObject(this);

    private sealed class TabAccessibleObject(TabButton owner) : ButtonBaseAccessibleObject(owner)
    {
        public override AccessibleStates State =>
            owner.Selected ? base.State | AccessibleStates.Selected : base.State;

        public override string DefaultAction => "選択";

        public override void DoDefaultAction() => owner.Selecting?.Invoke(owner.Tab);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _font?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// 自前のタイトル行。左からアプリ アイコン、タブ、＋ボタン、右端に最小化／最大化／閉じるボタンを並べる
/// （メモ帳と同じくタブをタイトル行に載せる。v0.8.0 で WPF の WindowChrome から Windows Forms に置き換え）。
///
/// タブやボタンは実体のあるコントロールなので、支援技術から名前と位置が見え、クリックもそのまま届く。
/// 何も無いところは親フォームが WM_NCHITTEST で HTCAPTION と答えるため、ドラッグ移動・ダブルクリックでの
/// 最大化・右クリックのシステム メニューは Windows がそのまま面倒を見る。
/// </summary>
public sealed class TitleBar : Control
{
    private readonly List<TabButton> _tabButtons = new();
    private readonly TitleBarButton _newTabButton;
    private readonly TitleBarButton _minimizeButton;
    private readonly TitleBarButton _maximizeButton;
    private readonly TitleBarButton _closeButton;
    private readonly ToolTip _tip = new() { InitialDelay = 700, ReshowDelay = 500, AutoPopDelay = 5000 };
    private ThemePalette _palette = ThemePalette.Light;
    private Icon? _appIcon;
    private bool _maximized;

    public TitleBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleRole = AccessibleRole.ToolBar;

        _newTabButton = new TitleBarButton("＋", useGlyphFont: false, fontSize: 15) { AccessibleName = "新しいタブ" };
        _newTabButton.Click += (_, _) => NewTabRequested?.Invoke();
        _minimizeButton = new TitleBarButton(UiStyle.GlyphMinimize, useGlyphFont: true, fontSize: 10) { CornerRadius = 0, AccessibleName = "最小化" };
        _minimizeButton.Click += (_, _) => MinimizeRequested?.Invoke();
        _maximizeButton = new TitleBarButton(UiStyle.GlyphMaximize, useGlyphFont: true, fontSize: 10) { CornerRadius = 0, AccessibleName = "最大化" };
        _maximizeButton.Click += (_, _) => MaximizeRequested?.Invoke();
        _closeButton = new TitleBarButton(UiStyle.GlyphClose, useGlyphFont: true, fontSize: 10) { CornerRadius = 0, IsCloseButton = true, AccessibleName = "閉じる" };
        _closeButton.Click += (_, _) => CloseRequested?.Invoke();

        Controls.AddRange(new Control[] { _newTabButton, _minimizeButton, _maximizeButton, _closeButton });
        _tip.SetToolTip(_newTabButton, "新しいタブ (Ctrl+N)");
        _tip.SetToolTip(_minimizeButton, "最小化");
        _tip.SetToolTip(_maximizeButton, "最大化");
        _tip.SetToolTip(_closeButton, "閉じる");
    }

    /// <summary>タブがクリックされた。</summary>
    public event Action<DocumentTab>? TabSelected;

    /// <summary>タブの×（または中クリック）で閉じる要求。</summary>
    public event Action<DocumentTab>? TabCloseRequested;

    public event Action? NewTabRequested;
    public event Action? MinimizeRequested;
    public event Action? MaximizeRequested;
    public event Action? CloseRequested;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            BackColor = value.TitleBar;
            foreach (var b in Controls.OfType<TitleBarButton>()) { b.Palette = value; b.BackColor = value.TitleBar; }
            foreach (var t in _tabButtons) t.Palette = value;
            Invalidate(true);
        }
    }

    /// <summary>左端に出すアプリ アイコン（16 px）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Icon? AppIcon
    {
        get => _appIcon;
        set { _appIcon = value; Invalidate(); }
    }

    /// <summary>最大化中は「元に戻す」のグリフにする。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsMaximized
    {
        get => _maximized;
        set
        {
            if (_maximized == value) return;
            _maximized = value;
            _maximizeButton.Glyph = value ? UiStyle.GlyphRestore : UiStyle.GlyphMaximize;
            _maximizeButton.AccessibleName = value ? "元に戻す" : "最大化";
            _tip.SetToolTip(_maximizeButton, _maximizeButton.AccessibleName);
            _maximizeButton.Invalidate();
        }
    }

    /// <summary>指定した位置（このコントロール内の座標）にタブやボタンがあるか。親のヒット テストが使う。</summary>
    public bool HasInteractiveChildAt(Point point) => GetChildAtPoint(point) is not null;

    /// <summary>タブの一覧と選択中のタブを反映する。</summary>
    public void SetTabs(IReadOnlyList<DocumentTab> tabs, DocumentTab? selected)
    {
        var order = tabs.ToList();
        // 無くなったタブのコントロールを外す
        for (var i = _tabButtons.Count - 1; i >= 0; i--)
        {
            if (tabs.Contains(_tabButtons[i].Tab)) continue;
            var old = _tabButtons[i];
            _tabButtons.RemoveAt(i);
            Controls.Remove(old);
            old.Dispose();
        }
        // 増えたタブのコントロールを作る
        foreach (var tab in tabs)
        {
            if (_tabButtons.Any(b => b.Tab == tab)) continue;
            var button = new TabButton(tab) { Palette = _palette };
            button.Selecting += t => TabSelected?.Invoke(t);
            button.CloseRequested += t => TabCloseRequested?.Invoke(t);
            _tabButtons.Add(button);
            Controls.Add(button);
        }
        _tabButtons.Sort((a, b) => order.IndexOf(a.Tab).CompareTo(order.IndexOf(b.Tab)));
        foreach (var button in _tabButtons)
        {
            button.Selected = button.Tab == selected;
            _tip.SetToolTip(button, button.Tab.Document.FilePath);
        }
        LayoutTabs();
    }

    /// <summary>タブの表題（未保存の●など）が変わったときに描き直す。</summary>
    public void RefreshTabs()
    {
        foreach (var button in _tabButtons)
        {
            button.UpdateText();
            _tip.SetToolTip(button, button.Tab.Document.FilePath);
        }
        LayoutTabs();
    }

    private int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutTabs();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        LayoutTabs();
    }

    /// <summary>タブとボタンを並べる。タブは表題に合わせ（120〜240 px）、入り切らないときは均等に縮める（最小 60 px）。</summary>
    private void LayoutTabs()
    {
        if (Width <= 0 || Height <= 0) return;
        var capW = S(46);
        var capH = S(30);
        var right = Width;
        _closeButton.SetBounds(right - capW, 0, capW, capH);
        _maximizeButton.SetBounds(right - capW * 2, 0, capW, capH);
        _minimizeButton.SetBounds(right - capW * 3, 0, capW, capH);

        var tabH = S(34);
        var tabTop = Height - tabH;
        var gap = S(4);
        var plusW = S(30);
        var plusH = S(28);
        var x = S(12) + S(16) + S(4);
        var avail = right - capW * 3 - x - (gap + plusW) - S(8);

        var widths = new int[_tabButtons.Count];
        var total = 0;
        for (var i = 0; i < _tabButtons.Count; i++)
        {
            _tabButtons[i].Height = tabH;   // PreferredWidth は Height を使うので先に決める
            widths[i] = Math.Clamp(_tabButtons[i].PreferredWidth, S(120), S(240));
            total += widths[i] + gap;
        }
        if (total > avail && _tabButtons.Count > 0)
        {
            // 均等に縮めて収める（メモ帳もタブが増えると幅を詰める）
            var each = Math.Max(S(60), avail / _tabButtons.Count - gap);
            for (var i = 0; i < widths.Length; i++) widths[i] = Math.Min(widths[i], each);
        }

        for (var i = 0; i < _tabButtons.Count; i++)
        {
            x += gap;
            _tabButtons[i].SetBounds(x, tabTop, widths[i], tabH);
            x += widths[i];
        }
        _newTabButton.SetBounds(x + gap, tabTop + (tabH - plusH) / 2, plusW, plusH);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_palette.TitleBar);
        if (_appIcon is not null)
        {
            var size = S(16);
            g.DrawIcon(_appIcon, new Rectangle(S(12), (Height - size) / 2, size, size));
        }
        base.OnPaint(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
