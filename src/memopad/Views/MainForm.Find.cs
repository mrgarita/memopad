using System.Drawing;
using System.Windows.Forms;

namespace Memopad.Views;

/// <summary>メイン ウィンドウ：検索／置換バー（Ctrl+F / Ctrl+H で表示、Esc で閉じる）。</summary>
public sealed partial class MainForm
{
    private Label _findLabel = null!;
    private TextBox _findText = null!;
    private Button _findNextButton = null!;
    private Button _findPrevButton = null!;
    private Button _findCloseButton = null!;
    private Label _replaceLabel = null!;
    private TextBox _replaceText = null!;
    private Button _replaceButton = null!;
    private Button _replaceAllButton = null!;
    private CheckBox _matchCaseCheck = null!;
    private CheckBox _wrapAroundCheck = null!;
    private Label _findStatus = null!;
    private bool _replaceMode;

    private Panel BuildFindBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Visible = false, BackColor = _palette.Window };
        _findLabel = new Label { Text = "検索:", AutoSize = true };
        _findText = new TextBox { BorderStyle = BorderStyle.FixedSingle };
        _findText.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                FindNext(backward: e.Shift);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        _findText.TextChanged += (_, _) => _findStatus.Text = "";
        _findNextButton = FlatButton("↓ 次へ", () => FindNext(backward: false), "次を検索 (F3)");
        _findPrevButton = FlatButton("↑ 前へ", () => FindNext(backward: true), "前を検索 (Shift+F3)");
        _findCloseButton = FlatButton(UiStyle.GlyphClose, HideFindBar, "閉じる (Esc)");
        _findCloseButton.Font = UiStyle.PixelFont(UiStyle.GlyphFontName, S(12));

        _replaceLabel = new Label { Text = "置換:", AutoSize = true };
        _replaceText = new TextBox { BorderStyle = BorderStyle.FixedSingle };
        _replaceText.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ReplaceOne();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        _replaceButton = FlatButton("置換", ReplaceOne);
        _replaceAllButton = FlatButton("すべて置換", ReplaceAll);

        _matchCaseCheck = new CheckBox { Text = "大文字と小文字を区別する", AutoSize = true };
        _wrapAroundCheck = new CheckBox { Text = "折り返す", AutoSize = true, Checked = true };
        _findStatus = new Label { AutoSize = true };

        bar.Controls.AddRange(new Control[]
        {
            _findLabel, _findText, _findNextButton, _findPrevButton, _findCloseButton,
            _replaceLabel, _replaceText, _replaceButton, _replaceAllButton,
            _matchCaseCheck, _wrapAroundCheck, _findStatus,
        });
        bar.Resize += (_, _) => LayoutFindBar();
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(_palette.Separator);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };
        ApplyFindBarTheme(bar);
        return bar;
    }

    private Button FlatButton(string text, Action action, string? toolTip = null)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            AutoSize = false,
            TabStop = true,
        };
        button.FlatAppearance.BorderSize = 1;
        button.Click += (_, _) => action();
        if (toolTip is not null) new ToolTip().SetToolTip(button, toolTip);
        return button;
    }

    /// <summary>検索バーの控えめな色をテーマに合わせる。</summary>
    private void ApplyFindBarTheme(Panel bar)
    {
        bar.BackColor = _palette.Window;
        foreach (Control c in bar.Controls)
        {
            c.ForeColor = _palette.Text;
            switch (c)
            {
                case TextBox tb:
                    tb.BackColor = _palette.Input;
                    break;
                case Button b:
                    b.BackColor = _palette.Input;
                    b.FlatAppearance.BorderColor = _palette.InputBorder;
                    b.FlatAppearance.MouseOverBackColor = _palette.TabHover;
                    b.FlatAppearance.MouseDownBackColor = _palette.TabHover;
                    break;
                default:
                    c.BackColor = _palette.Window;
                    break;
            }
        }
        _findStatus.ForeColor = _palette.MutedText;
    }

    /// <summary>1 行目：検索語と前後ボタン、2 行目：置換（置換モードのみ）、3 行目：オプションと結果。</summary>
    private void LayoutFindBar()
    {
        var pad = S(8);
        var rowH = S(30);
        var rowGap = S(6);
        var y = S(6);
        var labelW = S(40);
        var textW = Math.Clamp((int)(_findBar.Width * 0.45), S(200), S(480));
        var buttonW = S(90);
        var gap = S(6);

        _findLabel.Location = new Point(pad, y + (rowH - _findLabel.Height) / 2);
        _findText.SetBounds(pad + labelW + pad, y + (rowH - _findText.Height) / 2, textW, _findText.Height);
        _findNextButton.SetBounds(_findText.Right + gap, y, buttonW, rowH);
        _findPrevButton.SetBounds(_findNextButton.Right + gap, y, buttonW, rowH);
        _findCloseButton.SetBounds(_findPrevButton.Right + S(12), y, S(40), rowH);
        y += rowH + rowGap;

        var replaceVisible = _replaceMode;
        _replaceLabel.Visible = _replaceText.Visible = _replaceButton.Visible = _replaceAllButton.Visible = replaceVisible;
        if (replaceVisible)
        {
            _replaceLabel.Location = new Point(pad, y + (rowH - _replaceLabel.Height) / 2);
            _replaceText.SetBounds(pad + labelW + pad, y + (rowH - _replaceText.Height) / 2, textW, _replaceText.Height);
            _replaceButton.SetBounds(_replaceText.Right + gap, y, buttonW, rowH);
            _replaceAllButton.SetBounds(_replaceButton.Right + gap, y, buttonW, rowH);
            y += rowH + rowGap;
        }

        _matchCaseCheck.Location = new Point(pad + labelW + pad, y + (rowH - _matchCaseCheck.Height) / 2);
        _wrapAroundCheck.Location = new Point(_matchCaseCheck.Right + S(16), y + (rowH - _wrapAroundCheck.Height) / 2);
        _findStatus.Location = new Point(_wrapAroundCheck.Right + S(20), y + (rowH - _findStatus.Height) / 2);
        y += rowH + S(6);
        _findBar.Height = y;
    }

    private void ShowFindBar(bool replace)
    {
        _replaceMode = replace;
        _findStatus.Text = "";
        LayoutFindBar();
        _findBar.Visible = true;

        // 選択中の文字列があれば検索語にする（1 行以内のときだけ）
        if (Current is { } tab && tab.Editor.SelectedText is { Length: > 0 } sel && !sel.Contains('\n'))
        {
            _findText.Text = sel;
        }
        _findText.Focus();
        _findText.SelectAll();
    }

    private void HideFindBar()
    {
        _findBar.Visible = false;
        Current?.Editor.FocusEditor();
    }

    private StringComparison FindComparison =>
        _matchCaseCheck.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>次（または前）の一致を選択する。見つからなければステータスに表示する。</summary>
    private bool FindNext(bool backward)
    {
        if (Current is not { } tab) return false;
        var query = _findText.Text;
        if (query.Length == 0)
        {
            ShowFindBar(replace: _replaceMode);
            return false;
        }

        var editor = tab.Editor;
        var text = editor.Text;
        var wrap = _wrapAroundCheck.Checked;
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
            _findStatus.Text = $"\"{query}\" が見つかりません";
            System.Media.SystemSounds.Asterisk.Play();
            return false;
        }

        editor.Select(index, query.Length);
        _findStatus.Text = "";
        return true;
    }

    /// <summary>選択中が一致していれば置き換え、次の一致へ進む。</summary>
    private void ReplaceOne()
    {
        if (Current is not { } tab) return;
        var editor = tab.Editor;
        var query = _findText.Text;
        if (query.Length == 0) return;
        if (editor.SelectionLength > 0 && string.Equals(editor.SelectedText, query, FindComparison))
        {
            var start = editor.SelectionStart;
            editor.SelectedText = _replaceText.Text;
            editor.Select(start + _replaceText.Text.Length, 0);
        }
        FindNext(backward: false);
    }

    private void ReplaceAll()
    {
        if (Current is not { } tab) return;
        var editor = tab.Editor;
        var query = _findText.Text;
        if (query.Length == 0) return;

        var text = editor.Text;
        var count = 0;
        var sb = new System.Text.StringBuilder(text.Length);
        var pos = 0;
        while (true)
        {
            var next = text.IndexOf(query, pos, FindComparison);
            if (next < 0) break;
            sb.Append(text, pos, next - pos).Append(_replaceText.Text);
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
        _findStatus.Text = count > 0 ? $"{count} 件置換しました" : $"\"{query}\" が見つかりません";
    }
}
