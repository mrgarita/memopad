using System.Text;
using System.Windows.Forms;
using Memopad.Services;
using Memopad.Views;

namespace Memopad;

/// <summary>
/// アプリケーションの入口（v0.8.0 で WPF の App から Windows Forms に置き換え）。
/// 起動を速くするため、ここから MainForm が表示されるまでの間は WPF のアセンブリを一切読み込まない。
/// WPF はダイアログ（フォント・色変更・配色パターン・行へ移動・ページ設定・バージョン情報・保存確認）を
/// 初めて開くときに WpfHost が遅延初期化する。
/// </summary>
internal static class Program
{
    /// <summary>アプリ全体で共有する設定。</summary>
    public static AppSettings Settings { get; private set; } = new();

    [STAThread]
    private static void Main(string[] args)
    {
        PerfLog.Mark("Main 開始（ランタイム起動完了）");
        // 後で使う DLL を別スレッドで先に読み込む（起動時間の短縮）
        StartupWarmup.Start();
        // ANSI（Shift_JIS）を扱えるようにする
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Settings = SettingsService.Load();
        PerfLog.Mark("設定を読み込み");

        // csproj の ApplicationHighDpiMode／ApplicationVisualStyles／ApplicationUseCompatibleTextRendering を適用する
        ApplicationConfiguration.Initialize();
        PerfLog.Mark("Windows Forms を初期化");

        var form = new MainForm(args);
        PerfLog.Mark("MainForm を生成");
        Application.Run(form);
        SettingsService.Save(Settings);
    }
}
