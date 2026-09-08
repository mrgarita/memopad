using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Memopad.Services;

/// <summary>
/// アプリのテーマ（ライト／ダーク／システム設定）。.NET 9 WPF の Fluent テーマ（ThemeMode）を使う。
/// テキスト領域の既定色もここで決める（ユーザーが色を指定していないときに使う）。
/// </summary>
public static class ThemeService
{
    public static void Apply(string theme)
    {
        Application.Current.ThemeMode = theme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    /// <summary>実際に適用されているのがダークかどうか（"System" のときは OS の設定から判定）。</summary>
    public static bool IsDark(string theme)
    {
        return theme switch
        {
            "Light" => false,
            "Dark" => true,
            _ => IsSystemDark(),
        };
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1;

    /// <summary>
    /// ウィンドウの背景を不透明にする。.NET 9 の Fluent テーマはウィンドウ全体を DWM のガラス領域（Mica 背景）に
    /// するが、その上に GDI で描く RichEdit はアルファ値 0 で描かれるため透けて見えてしまう。
    /// フレームの拡張と Mica を止め、背景色をテーマに合わせて塗る。テーマを切り替えたときも呼び直す。
    /// </summary>
    public static void MakeOpaque(Window window, string theme)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var margins = new Margins();
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var none = DWMSBT_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
        // タイトル バーを自前にしても Windows 11 の角丸を保つ
        var round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        window.Background = new SolidColorBrush(IsDark(theme) ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
    }

    /// <summary>テキスト領域の既定の背景色。</summary>
    public static Color DefaultBackground(string theme) =>
        IsDark(theme) ? Color.FromRgb(0x27, 0x27, 0x27) : Colors.White;

    /// <summary>テキスト領域の既定の文字色。</summary>
    public static Color DefaultForeground(string theme) =>
        IsDark(theme) ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Colors.Black;
}
