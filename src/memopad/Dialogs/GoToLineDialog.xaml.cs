using System.Windows;

namespace Memopad.Dialogs;

/// <summary>「行に移動」（Ctrl+G）。行番号を入力させ、範囲内なら返す。</summary>
public partial class GoToLineDialog : Window
{
    private readonly int _lineCount;
    private int? _result;

    public GoToLineDialog(IntPtr owner, int currentLine, int lineCount)
    {
        InitializeComponent();
        new System.Windows.Interop.WindowInteropHelper(this).Owner = owner;
        _lineCount = lineCount;
        LineTextBox.Text = currentLine.ToString();
        RangeText.Text = $"1 〜 {lineCount} 行";
        Loaded += (_, _) => { LineTextBox.Focus(); LineTextBox.SelectAll(); };
    }

    public static int? Ask(IntPtr owner, int currentLine, int lineCount)
    {
        var dialog = new GoToLineDialog(owner, currentLine, lineCount);
        dialog.ShowDialog();
        return dialog._result;
    }

    private void GoButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(LineTextBox.Text.Trim(), out var line) || line < 1 || line > _lineCount)
        {
            MessageBox.Show(this, "行番号が範囲外です。", "MemoPad - 行に移動", MessageBoxButton.OK, MessageBoxImage.Warning);
            LineTextBox.Focus();
            LineTextBox.SelectAll();
            return;
        }
        _result = line;
        DialogResult = true;
    }
}
