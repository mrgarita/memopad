using Memopad.Models;
using Memopad.Services;
using WinForms = System.Windows.Forms;

namespace Memopad.Views;

/// <summary>
/// タブ 1 つ分の編集領域。エディタ（RichEdit）をタブごとに持つことで、元に戻す履歴とカーソル位置をタブ単位で保てる。
/// MainForm からは本文・選択範囲・編集操作をこのクラス経由で扱う（v0.8.0 で WPF の UserControl から純粋なクラスに変更）。
/// </summary>
public sealed class EditorView
{
    private readonly PlainTextEdit _edit = new();
    private bool _loading;
    private bool _statusPending;
    private bool _contentPending;

    public EditorView(Document document)
    {
        Document = document;
        _edit.Dock = WinForms.DockStyle.Fill;
        _edit.TextChanged += Edit_TextChanged;
        _edit.SelectionChanged += Edit_SelectionChanged;
        if (PerfLog.Enabled) AttachPerfProbes();
    }

    public Document Document { get; }

    /// <summary>編集コントロール本体（ホイール・右クリック・ドロップのイベント購読と、フォームへの配置に使う）。</summary>
    public PlainTextEdit Edit => _edit;

    /// <summary>カーソル位置や選択範囲が変わったとき（ステータスバー更新用）。連続入力中はまとめて 1 回にする。</summary>
    public event EventHandler? CaretChanged;

    /// <summary>本文が変わったとき（変更フラグ・文字数更新用）。連続入力中はまとめて 1 回にする。</summary>
    public event EventHandler? ContentChanged;

    // --- 本文と選択範囲（改行は "\n" 1 文字で表現される）

    public string Text => _edit.Text;
    public int TextLength => _edit.TextLength;
    public int SelectionStart => _edit.SelectionStart;
    public int SelectionLength => _edit.SelectionLength;

    public string SelectedText
    {
        get => _edit.SelectedText;
        set => _edit.SelectedText = value;
    }

    /// <summary>範囲を選択し、見える位置までスクロールする。</summary>
    public void Select(int start, int length)
    {
        _edit.Select(start, length);
        _edit.ScrollToCaret();
    }

    public void SelectAll() => _edit.SelectAll();
    public void FocusEditor() => _edit.Focus();

    // --- 編集操作（編集メニューと右クリック メニューから使う）

    public bool CanUndo => _edit.CanUndo;
    public bool CanRedo => _edit.CanRedo;
    public bool HasSelection => _edit.SelectionLength > 0;
    public bool CanPaste => WinForms.Clipboard.ContainsText();
    public void Undo() { if (_edit.CanUndo) _edit.Undo(); }
    public void Redo() { if (_edit.CanRedo) _edit.Redo(); }
    public void Cut() => _edit.Cut();
    public void Copy() => _edit.Copy();
    public void Paste() { if (WinForms.Clipboard.ContainsText()) _edit.SelectedText = WinForms.Clipboard.GetText(); }
    public void Delete() { if (_edit.SelectionLength > 0) _edit.SelectedText = ""; }

    /// <summary>ファイルから読み込んだ本文を設定する。元に戻す履歴は捨て、変更なしの状態にする。</summary>
    public void LoadText(string text)
    {
        _loading = true;
        try
        {
            _edit.Text = text;
            _edit.ClearUndo();
            _edit.Select(0, 0);
        }
        finally
        {
            _loading = false;
        }
        Document.IsDirty = false;
        ContentChanged?.Invoke(this, EventArgs.Empty);
        CaretChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>フォント・色・折り返し・ズーム・スクロールバーのテーマを反映する。</summary>
    public void ApplyAppearance(AppSettings settings, int zoomPercent)
    {
        var style = System.Drawing.FontStyle.Regular;
        if (settings.FontBold) style |= System.Drawing.FontStyle.Bold;
        if (settings.FontItalic) style |= System.Drawing.FontStyle.Italic;
        var current = _edit.Font;
        if (current.Name != settings.FontFamily || Math.Abs(current.SizeInPoints - settings.FontSize) > 0.01 || current.Style != style)
        {
            _edit.Font = new System.Drawing.Font(settings.FontFamily, (float)Math.Max(1, settings.FontSize), style, System.Drawing.GraphicsUnit.Point);
        }
        _edit.ZoomFactor = Math.Clamp(zoomPercent / 100f, 1f / 64, 64f);
        _edit.WrapText = settings.WordWrap;

        _edit.BackColor = ColorText.TryParse(settings.BackgroundColor) ?? ThemeService.EditorBackground(settings.Theme);
        _edit.ForeColor = ColorText.TryParse(settings.ForegroundColor) ?? ThemeService.EditorForeground(settings.Theme);
        _edit.DarkScrollBars = ThemeService.IsDark(settings.Theme);
    }

    /// <summary>カーソル位置を「論理行, 列」で返す（折り返しの見た目の行ではなく、改行で数えた行）。</summary>
    public (int Line, int Column) GetCaretPosition()
    {
        var text = _edit.Text;
        var index = Math.Min(_edit.SelectionStart, text.Length);
        var head = text.AsSpan(0, index);
        var line = head.Count('\n') + 1;
        var lineStart = head.LastIndexOf('\n') + 1;
        return (line, index - lineStart + 1);
    }

    /// <summary>文字数（ステータスバーの「N 文字」）。メモ帳に合わせ、改行は 1 つにつき 1 文字と数える。</summary>
    public int GetCharacterCount() => _edit.TextLength;

    /// <summary>指定した論理行の先頭へカーソルを移動する。</summary>
    public void GoToLine(int line)
    {
        var text = _edit.Text;
        var current = 1;
        var index = 0;
        while (current < line && index < text.Length)
        {
            var next = text.IndexOf('\n', index);
            if (next < 0) break;
            index = next + 1;
            current++;
        }
        Select(index, 0);
        _edit.Focus();
    }

    private void Edit_SelectionChanged(object? sender, EventArgs e)
    {
        if (_loading || _statusPending) return;
        // 連続入力中に毎回全文を読まないよう、入力が途切れたタイミングで 1 回だけ通知する
        _statusPending = true;
        Later(() =>
        {
            _statusPending = false;
            CaretChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void Edit_TextChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        Document.IsDirty = true;
        if (_contentPending) return;
        _contentPending = true;
        Later(() =>
        {
            _contentPending = false;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>今処理中の入力が終わってから（メッセージ キューの後ろで）実行する。</summary>
    private void Later(Action action)
    {
        if (_edit.IsHandleCreated) _edit.BeginInvoke(action);
        else action();
    }

    // --- 診断（MEMOPAD_PERF=1 のときだけ）：キー押下から本文変更までの時間を記録する
    private double _keyDownAt;

    private void AttachPerfProbes()
    {
        _edit.KeyDown += (_, e) => { _keyDownAt = PerfLog.Now; PerfLog.Write($"KeyDown {e.KeyCode}"); };
        _edit.TextChanged += (_, _) => PerfLog.Write($"TextChanged (+{PerfLog.Now - _keyDownAt:F1} ms after KeyDown)");
    }
}
