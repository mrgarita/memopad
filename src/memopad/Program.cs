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
/// アセンブリ属性に WPF の型（WPF テンプレートの ThemeInfo など）を置くと、Windows Forms の初期化が
/// exe の属性を調べる時点で WPF 本体（約 23 MB）が読み込まれてしまうので置かない（v0.10.2 で削除。FB-22）。
/// </summary>
internal static class Program
{
    /// <summary>アプリ全体で共有する設定。</summary>
    public static AppSettings Settings { get; private set; } = new();

    [STAThread]
    private static void Main(string[] args)
    {
        PerfLog.Mark("Main 開始（ランタイム起動完了）");
        PerfLog.TraceAssemblyLoads();
        // Scintilla.NET は最初のエディタを作るときに Scintilla.dll と Lexilla.dll の電子署名（Authenticode）を
        // 検証する。証明書チェーンの組み立てと証明書ストアの読み込みが入り、端末を起動した直後ほど遅い（FB-22）。
        // 2 つの DLL は MemoPad.exe・MemoPad.dll・.NET ランタイムと同じフォルダーに入っていて、そちらは検証して
        // いないので、ここだけ確かめても守りにはならない。Scintilla の型に触れる前に切る必要がある
        ScintillaNET.ScintillaNativeLibrary.VerifyScintillaDll = false;
        ScintillaNET.ScintillaNativeLibrary.VerifyLexillaDll = false;
        // 発行版では 2 つの DLL は exe と同じフォルダーにある（StartupWarmup が先読みするのも同じ場所）。
        // ここに無ければ（開発中のビルド）Scintilla.NET が既定の runtimes\win-x64\native\ を探す
        ScintillaNET.ScintillaNativeLibrary.SatelliteDirectory = AppContext.BaseDirectory;
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
