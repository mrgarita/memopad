using System.Runtime.InteropServices;

namespace Memopad.Services;

/// <summary>メイン ウィンドウの配色（ライト／ダーク）。値は Windows 11 のメモ帳に合わせている。</summary>
public sealed record ThemePalette(
    bool IsDark,
    System.Drawing.Color Window,        // ウィンドウの地の色
    System.Drawing.Color TitleBar,      // タイトル行（タブの背景）
    System.Drawing.Color MenuBar,       // メニュー行と選択中のタブ（つなげて見せる）
    System.Drawing.Color TabHover,      // タブ・メニュー・ボタンのホバー
    System.Drawing.Color Text,          // 通常の文字
    System.Drawing.Color MutedText,     // ショートカット表示や無効項目
    System.Drawing.Color Popup,         // メニューのポップアップ
    System.Drawing.Color PopupBorder,   // ポップアップの枠線
    System.Drawing.Color Input,         // 入力欄・ボタンの地
    System.Drawing.Color InputBorder,   // 入力欄・ボタンの枠線
    System.Drawing.Color Separator)     // 区切り線
{
    public static readonly ThemePalette Light = new(
        false,
        Rgb(0xF3, 0xF3, 0xF3), Rgb(0xE8, 0xE8, 0xE8), Rgb(0xF9, 0xF9, 0xF9), Rgb(0xDC, 0xDC, 0xDC),
        Rgb(0x1B, 0x1B, 0x1B), Rgb(0x6E, 0x6E, 0x6E), Rgb(0xF9, 0xF9, 0xF9), Rgb(0xE0, 0xE0, 0xE0),
        Rgb(0xFF, 0xFF, 0xFF), Rgb(0xC8, 0xC8, 0xC8), Rgb(0xD0, 0xD0, 0xD0));

    public static readonly ThemePalette Dark = new(
        true,
        Rgb(0x20, 0x20, 0x20), Rgb(0x1C, 0x1C, 0x1C), Rgb(0x2B, 0x2B, 0x2B), Rgb(0x3A, 0x3A, 0x3A),
        Rgb(0xF0, 0xF0, 0xF0), Rgb(0xA0, 0xA0, 0xA0), Rgb(0x2C, 0x2C, 0x2C), Rgb(0x3F, 0x3F, 0x3F),
        Rgb(0x2B, 0x2B, 0x2B), Rgb(0x4A, 0x4A, 0x4A), Rgb(0x3A, 0x3A, 0x3A));

    private static System.Drawing.Color Rgb(int r, int g, int b) => System.Drawing.Color.FromArgb(r, g, b);
}

/// <summary>
/// アプリのテーマ（ライト／ダーク／システム設定）。メイン ウィンドウは Windows Forms なので配色を自前で持ち、
/// WPF のダイアログには WpfHost が Fluent テーマ（ThemeMode）で同じテーマを適用する。
/// </summary>
public static class ThemeService
{
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

    public static ThemePalette Palette(string theme) => IsDark(theme) ? ThemePalette.Dark : ThemePalette.Light;

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

    // --- テキスト領域の既定色（ユーザーが色を指定していないときに使う）

    /// <summary>テキスト領域の既定の背景色（Windows Forms 用）。</summary>
    public static System.Drawing.Color EditorBackground(string theme) =>
        IsDark(theme) ? System.Drawing.Color.FromArgb(0x27, 0x27, 0x27) : System.Drawing.Color.White;

    /// <summary>テキスト領域の既定の文字色（Windows Forms 用）。</summary>
    public static System.Drawing.Color EditorForeground(string theme) =>
        IsDark(theme) ? System.Drawing.Color.FromArgb(0xF0, 0xF0, 0xF0) : System.Drawing.Color.Black;

    /// <summary>テキスト領域の既定の背景色（WPF のダイアログ用）。</summary>
    public static System.Windows.Media.Color DefaultBackground(string theme)
    {
        var c = EditorBackground(theme);
        return System.Windows.Media.Color.FromRgb(c.R, c.G, c.B);
    }

    /// <summary>テキスト領域の既定の文字色（WPF のダイアログ用）。</summary>
    public static System.Windows.Media.Color DefaultForeground(string theme)
    {
        var c = EditorForeground(theme);
        return System.Windows.Media.Color.FromRgb(c.R, c.G, c.B);
    }

    // --- DWM（ウィンドウ枠の見た目）

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    /// <summary>ウィンドウ枠（影と縁の色）をテーマに合わせ、Windows 11 の角丸を付ける。</summary>
    public static void ApplyWindowFrame(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return;
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        var round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    /// <summary>メニューのポップアップなど小さいウィンドウに小さめの角丸を付ける。</summary>
    public static void ApplySmallRoundedCorners(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var round = DWMWCP_ROUNDSMALL;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }
}
