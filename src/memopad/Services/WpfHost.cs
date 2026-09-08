using System.Windows;

namespace Memopad.Services;

/// <summary>
/// WPF のダイアログを Windows Forms のメイン ウィンドウから使うための遅延初期化。
/// 起動時には WPF を読み込まず、最初にダイアログを開くときに Application を作って Fluent テーマを適用する
/// （初回だけ 200 ms ほどかかる）。ダイアログのオーナーは Win32 のウィンドウ ハンドルで渡す。
/// </summary>
public static class WpfHost
{
    /// <summary>WPF を初期化済みか（このフラグを読むだけなら WPF のアセンブリは読み込まれない）。</summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>WPF の Application を用意し、テーマを合わせる。ダイアログを開く直前に呼ぶ。</summary>
    public static void Ensure(string theme)
    {
        if (!IsInitialized)
        {
            PerfLog.Mark("WPF を初期化（最初のダイアログ）");
            // Run は呼ばない。Windows Forms のメッセージ ループの上で ShowDialog だけを使う
            if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            IsInitialized = true;
        }
        ApplyTheme(theme);
    }

    /// <summary>テーマ（ライト／ダーク／システム設定）を WPF 側にも反映する。WPF が未初期化なら何もしない。</summary>
    public static void ApplyTheme(string theme)
    {
        if (Application.Current is not { } app) return;
        app.ThemeMode = theme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }
}
