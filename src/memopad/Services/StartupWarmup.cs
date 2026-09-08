using System.Reflection;
using System.Runtime.InteropServices;

namespace Memopad.Services;

/// <summary>
/// 起動の高速化：UI スレッドが後で必要とするアセンブリと DLL を、別スレッドで先に読み込んでおく。
///
/// 起動直後の UI スレッドは WPF の初期化と XAML の読み込みで手一杯だが、その間 CPU の他のコアは空いている。
/// 最初のタブ（RichEdit）を作る時点で System.Windows.Forms／WindowsFormsIntegration／msftedit.dll、
/// タイトル行のアイコンで System.Drawing、テーマ適用で PresentationFramework.Fluent を読み込むので、
/// これらを並列に済ませておくと UI スレッドの待ち時間がその分減る（5 回目のフィードバック、v0.7.0）。
/// 読み込むだけで初期化は行わないため、UI スレッドとの競合は起きない。
/// </summary>
public static class StartupWarmup
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string fileName);

    public static void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "startup-warmup" };
        thread.Start();
    }

    private static void Run()
    {
        try
        {
            PerfLog.Mark("プリロード開始（別スレッド）");
            // typeof はそのアセンブリの読み込みだけを起こし、型の静的初期化は走らない
            _ = typeof(System.Windows.Forms.RichTextBox);
            _ = typeof(System.Windows.Forms.Integration.WindowsFormsHost);
            _ = typeof(System.Drawing.Icon);
            _ = typeof(System.Text.Json.JsonSerializer);
            LoadLibraryW("msftedit.dll");
            Assembly.Load("PresentationFramework.Fluent");
            PerfLog.Mark("プリロード完了（別スレッド）");
        }
        catch
        {
            // 先読みに失敗しても本来の読み込みが後で行われるだけなので無視する
        }
    }
}
