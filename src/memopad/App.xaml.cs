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

    static App()
    {
        PerfLog.Mark("App 型を初期化（ランタイム起動完了）");
        // 後で使うアセンブリを別スレッドで先に読み込む（起動時間の短縮）
        StartupWarmup.Start();
        // WPF の描画をソフトウェアにする。GPU（Direct3D）のデバイス生成に起動時 80 ms ほどかかるのを避けるため。
        // memopad の WPF 部分はタイトル行・メニュー・ステータスバーだけで、本文は GDI の RichEdit が描くので
        // ソフトウェア描画でも見た目・速度に差は出ない
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
    }

    public App()
    {
        PerfLog.Mark("App ctor 開始（Application 基底の初期化済み）");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        PerfLog.Mark("OnStartup 開始");
        // ANSI（Shift_JIS）を扱えるようにする
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Settings = SettingsService.Load();
        PerfLog.Mark("設定を読み込み");
        ThemeService.Apply(Settings.Theme);
        PerfLog.Mark("テーマを適用");

        base.OnStartup(e);

        var window = new MainWindow();
        PerfLog.Mark("MainWindow を生成");
        MainWindow = window;
        window.ContentRendered += (_, _) => PerfLog.Mark("最初の描画が完了");
        window.Show();
        PerfLog.Mark("Show を完了");

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
