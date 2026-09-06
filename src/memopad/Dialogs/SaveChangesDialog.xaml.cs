using System.Windows;

namespace Memopad.Dialogs;

public enum SaveChangesResult
{
    Save,
    DontSave,
    Cancel,
}

/// <summary>未保存の変更があるタブを閉じるときの確認。「保存／保存しない／キャンセル」を返す。</summary>
public partial class SaveChangesDialog : Window
{
    private SaveChangesResult _result = SaveChangesResult.Cancel;

    public SaveChangesDialog(Window owner, string documentTitle)
    {
        InitializeComponent();
        Owner = owner;
        MessageText.Text = $"{documentTitle} への変更内容を保存しますか?";
    }

    public static SaveChangesResult Ask(Window owner, string documentTitle)
    {
        var dialog = new SaveChangesDialog(owner, documentTitle);
        dialog.ShowDialog();
        return dialog._result;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _result = SaveChangesResult.Save;
        DialogResult = true;
    }

    private void DontSaveButton_Click(object sender, RoutedEventArgs e)
    {
        _result = SaveChangesResult.DontSave;
        DialogResult = true;
    }
}
