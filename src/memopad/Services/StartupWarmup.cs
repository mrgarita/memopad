using System.Runtime.InteropServices;

namespace Memopad.Services;

/// <summary>
/// 起動の高速化：UI スレッドが後で必要とするアセンブリと DLL を、別スレッドで先に読み込んでおく。
///
/// 起動直後の UI スレッドは設定の読み込みとフォームの構築で手一杯だが、その間 CPU の他のコアは空いている。
/// 最初のタブ（本文のエディタ）を作る時点で Scintilla.dll を、設定の読み書きで System.Text.Json を
/// 読み込むので、これらを並列に済ませておくと UI スレッドの待ち時間がその分減る
/// （v0.7.0 で導入、v0.8.0 で対象を見直し、v0.10.0 で本文が Scintilla になったので差し替え）。
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
            // 本文のエディタ（Scintilla）のネイティブ DLL。exe と同じフォルダーに置かれる
            var dir = AppContext.BaseDirectory;
            if (LoadLibraryW(System.IO.Path.Combine(dir, "Scintilla.dll")) == IntPtr.Zero) LoadLibraryW("Scintilla.dll");
            // typeof はそのアセンブリの読み込みだけを起こし、型の静的初期化は走らない
            _ = typeof(ScintillaNET.Scintilla);
            _ = typeof(System.Text.Json.JsonSerializer);
            _ = typeof(System.Windows.Forms.ToolStripProfessionalRenderer);
            PerfLog.Mark("プリロード完了（別スレッド）");
        }
        catch
        {
            // 先読みに失敗しても本来の読み込みが後で行われるだけなので無視する
        }
    }
}
