using Memopad.Models;
using Memopad.Services;
using WinForms = System.Windows.Forms;

namespace Memopad.Views;

/// <summary>
/// タブ 1 つ分の編集領域。エディタをタブごとに持つことで、元に戻す履歴とカーソル位置をタブ単位で保てる。
/// MainForm からは本文・選択範囲・編集操作をこのクラス経由で扱う（v0.8.0 で WPF の UserControl から純粋なクラスに変更）。
/// 本文のコントロールは v0.10.0 で RichEdit から Scintilla に替えた（<see cref="PlainTextEdit"/>）。
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
        // 「編集済み」はエディタの保存ポイントで判定する（文字が変わったかどうかで見ると、
        // コントロールの初期化でも変更とみなされ、新規タブが最初から編集済みになってしまう）
        _edit.SavePointLeft += (_, _) => { if (!_loading) Document.IsDirty = true; };
        _edit.SavePointReached += (_, _) => { if (!_loading) Document.IsDirty = false; };
        _edit.HandleCreated += (_, _) => _edit.MarkClean();
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
        set => _edit.ReplaceSelectedText(value);
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
    public void Paste() { if (WinForms.Clipboard.ContainsText()) _edit.Paste(); }
    public void Delete() { if (_edit.SelectionLength > 0) _edit.ReplaceSelectedText(""); }

    /// <summary>ファイルから読み込んだ本文を設定する。元に戻す履歴は捨て、変更なしの状態にする。</summary>
    public void LoadText(string text)
    {
        _loading = true;
        try
        {
            // エディタ内部の改行は LF に統一する（保存時に TextFileService がファイルの改行コードへ戻す）
            _edit.Text = NormalizeToLf(text);
            _edit.ClearUndo();
            _edit.MarkClean();
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

    /// <summary>CRLF・CR 混在のテキストを LF にそろえる。</summary>
    private static string NormalizeToLf(string text)
    {
        if (text.IndexOf('\r') < 0) return text;
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>フォント・色・折り返し・ズーム・スクロールバーのテーマを反映する。</summary>
    public void ApplyAppearance(AppSettings settings, int zoomPercent)
    {
        var back = ColorText.TryParse(settings.BackgroundColor) ?? ThemeService.EditorBackground(settings.Theme);
        var fore = ColorText.TryParse(settings.ForegroundColor) ?? ThemeService.EditorForeground(settings.Theme);
        _edit.ApplyTextAppearance(settings.FontFamily, settings.FontSize, settings.FontBold, settings.FontItalic,
                                  back, fore, zoomPercent);
        _edit.WrapText = settings.WordWrap;
        _edit.DarkScrollBars = ThemeService.IsDark(settings.Theme);
    }

    /// <summary>カーソル位置を「論理行, 列」で返す（折り返しの見た目の行ではなく、改行で数えた行）。</summary>
    public (int Line, int Column) GetCaretPosition() => _edit.GetCaretPosition();

    /// <summary>文字数（ステータスバーの「N 文字」）。メモ帳に合わせ、改行は 1 つにつき 1 文字と数える。</summary>
    public int GetCharacterCount() => _edit.TextLength;

    /// <summary>論理行の数（「行へ移動」ダイアログの上限に使う）。</summary>
    public int GetLineCount() => _edit.Lines.Count;

    /// <summary>指定した論理行の先頭へカーソルを移動する。</summary>
    public void GoToLine(int line) => _edit.GoToLine(line);

    /// <summary>保存が済んだ時点の内容を「編集なし」として記録する。</summary>
    public void MarkClean() => _edit.MarkClean();

    private void Edit_SelectionChanged(object? sender, EventArgs e)
    {
        if (_loading || _statusPending) return;
        // 連続入力中に毎回更新しないよう、入力が途切れたタイミングで 1 回だけ通知する
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
