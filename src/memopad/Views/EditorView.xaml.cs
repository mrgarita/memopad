using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Memopad.Models;
using Memopad.Services;

namespace Memopad.Views;

/// <summary>
/// タブ 1 つ分の編集領域。TextBox をタブごとに持つことで、元に戻す履歴とカーソル位置をタブ単位で保てる。
/// </summary>
public partial class EditorView : UserControl
{
    private bool _loading;

    public EditorView(Document document)
    {
        InitializeComponent();
        Document = document;
    }

    public Document Document { get; }

    /// <summary>本文の TextBox。検索・置換やカーソル操作で使う。</summary>
    public TextBox Editor => TextBox;

    /// <summary>カーソル位置や選択範囲が変わったとき（ステータスバー更新用）。</summary>
    public event EventHandler? CaretChanged;

    /// <summary>本文が変わったとき（変更フラグ・文字数更新用）。</summary>
    public event EventHandler? ContentChanged;

    /// <summary>ファイルから読み込んだ本文を設定する。元に戻す履歴は捨て、変更なしの状態にする。</summary>
    public void LoadText(string text)
    {
        _loading = true;
        try
        {
            TextBox.IsUndoEnabled = false;   // 履歴をクリア
            TextBox.Text = TextFileService.NormalizeToCrlf(text);
            TextBox.IsUndoEnabled = true;
            TextBox.CaretIndex = 0;
        }
        finally
        {
            _loading = false;
        }
        Document.IsDirty = false;
        ContentChanged?.Invoke(this, EventArgs.Empty);
        CaretChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>フォント・色・折り返し・ズームを反映する。</summary>
    public void ApplyAppearance(AppSettings settings, int zoomPercent)
    {
        TextBox.FontFamily = new FontFamily(settings.FontFamily);
        // WPF の FontSize は px 単位。メモ帳と同じくポイント指定なので 96/72 を掛ける
        TextBox.FontSize = Math.Max(1, settings.FontSize * 96.0 / 72.0 * zoomPercent / 100.0);
        TextBox.FontWeight = settings.FontBold ? FontWeights.Bold : FontWeights.Normal;
        TextBox.FontStyle = settings.FontItalic ? FontStyles.Italic : FontStyles.Normal;
        TextBox.TextWrapping = settings.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        TextBox.HorizontalScrollBarVisibility = settings.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

        var bg = ColorUtil.TryParse(settings.BackgroundColor) ?? ThemeService.DefaultBackground(settings.Theme);
        var fg = ColorUtil.TryParse(settings.ForegroundColor) ?? ThemeService.DefaultForeground(settings.Theme);
        TextBox.Background = new SolidColorBrush(bg);
        TextBox.Foreground = new SolidColorBrush(fg);
        TextBox.CaretBrush = new SolidColorBrush(fg);
        // 選択範囲は文字色を半透明にして、どんな背景色でも見えるようにする
        TextBox.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x60, fg.R, fg.G, fg.B));
    }

    /// <summary>カーソル位置を「論理行, 列」で返す（折り返しの見た目の行ではなく、改行で数えた行）。</summary>
    public (int Line, int Column) GetCaretPosition()
    {
        var text = TextBox.Text;
        var index = Math.Min(TextBox.CaretIndex, text.Length);
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }
        return (line, index - lineStart + 1);
    }

    /// <summary>改行を除いた文字数（ステータスバーの「N 文字」）。</summary>
    public int GetCharacterCount()
    {
        var count = 0;
        foreach (var c in TextBox.Text)
        {
            if (c != '\r' && c != '\n') count++;
        }
        return count;
    }

    /// <summary>指定した論理行の先頭へカーソルを移動する。</summary>
    public void GoToLine(int line)
    {
        var text = TextBox.Text;
        var current = 1;
        var index = 0;
        while (current < line && index < text.Length)
        {
            var next = text.IndexOf('\n', index);
            if (next < 0) break;
            index = next + 1;
            current++;
        }
        TextBox.CaretIndex = index;
        TextBox.ScrollToLine(TextBox.GetLineIndexFromCharacterIndex(index));
        TextBox.Focus();
    }

    private void TextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading) CaretChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Document.IsDirty = true;
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }
}
