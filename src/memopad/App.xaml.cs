using System.IO;
using System.Text;
using System.Windows;
using Memopad.Services;

namespace Memopad;

/// <summary>
/// アプリケーションの入口。設定の読み込み、テーマの適用、起動引数（開くファイル）の処理を行う。
/// </summary>
public partial class App : Application
{
    /// <summary>アプリ全体で共有する設定。</summary>
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // ANSI（Shift_JIS）を扱えるようにする
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Settings = SettingsService.Load();
        ThemeService.Apply(Settings.Theme);

        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // 起動引数に渡されたファイルを開く（エクスプローラーからの「プログラムから開く」に対応）
        foreach (var path in e.Args)
        {
            if (File.Exists(path)) window.OpenFile(path);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsService.Save(Settings);
        base.OnExit(e);
    }
}
