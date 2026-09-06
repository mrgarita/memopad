using System.Windows;
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

    /// <summary>テキスト領域の既定の背景色。</summary>
    public static Color DefaultBackground(string theme) =>
        IsDark(theme) ? Color.FromRgb(0x27, 0x27, 0x27) : Colors.White;

    /// <summary>テキスト領域の既定の文字色。</summary>
    public static Color DefaultForeground(string theme) =>
        IsDark(theme) ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Colors.Black;
}
