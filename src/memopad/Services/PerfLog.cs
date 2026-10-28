using System.Diagnostics;
using System.IO;

namespace Memopad.Services;

/// <summary>
/// 入力レスポンスの診断ログ。環境変数 MEMOPAD_PERF=1 で有効になり、%TEMP%\memopad-perf.log に追記する。
/// 通常の利用では何もしない（step3 の「IME 入力が一呼吸遅れる」調査のために追加）。
/// </summary>
public static class PerfLog
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("MEMOPAD_PERF") == "1";
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "memopad-perf.log");

    public static double Now => Clock.Elapsed.TotalMilliseconds;

    /// <summary>プロセス起動（exe のロード）からの経過時間。起動時間の内訳を調べるときに使う。</summary>
    public static double SinceProcessStart =>
        (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;

    /// <summary>
    /// アセンブリが読み込まれるたびに記録する（MEMOPAD_PERF=1 のときだけ）。
    /// 起動時に不要な DLL を読んでいないかを調べるため。WPF などは呼び出し元のスタックも残す。
    /// </summary>
    public static void TraceAssemblyLoads()
    {
        if (!Enabled) return;
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
        {
            var name = e.LoadedAssembly.GetName().Name ?? "";
            var stack = name.StartsWith("Presentation") || name.StartsWith("WindowsBase") || name.Contains("Cryptography")
                ? "\n" + new StackTrace(1, false) : "";
            Write($"起動 {SinceProcessStart,7:F0} ms  [{Environment.CurrentManagedThreadId}] アセンブリ読み込み {name}{stack}");
        };
    }

    /// <summary>起動処理の段階を記録する（MEMOPAD_PERF=1 のときだけ）。</summary>
    public static void Mark(string stage)
    {
        if (!Enabled) return;
        Write($"起動 {SinceProcessStart,7:F0} ms  {stage}");
    }

    public static void Write(string message)
    {
        if (!Enabled) return;
        try { File.AppendAllText(Path, $"{Now,10:F1} ms  {message}\n"); } catch { }
    }
}
