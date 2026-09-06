using System.Windows;
using System.Windows.Interop;
using WpfColor = System.Windows.Media.Color;

namespace Memopad.Dialogs;

/// <summary>
/// 「その他の色...」のカラーピッカー。Windows 標準の色の設定ダイアログ（Windows Forms の ColorDialog）を
/// WPF ウィンドウをオーナーにして開く。ユーザーが作った色は同じセッション内で保持する。
/// </summary>
public static class ColorPicker
{
    private static int[]? _customColors;

    public static WpfColor? Pick(Window owner, WpfColor? initial)
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,          // 最初から「色の作成」を開いた状態にする
            AnyColor = true,
            AllowFullOpen = true,
        };
        if (initial is { } c) dialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        if (_customColors is not null) dialog.CustomColors = _customColors;

        var result = dialog.ShowDialog(new Win32Owner(new WindowInteropHelper(owner).Handle));
        _customColors = dialog.CustomColors;
        if (result != System.Windows.Forms.DialogResult.OK) return null;
        return WpfColor.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
    }

    private sealed class Win32Owner(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
