<#
.SYNOPSIS
  メモ帳／MemoPad の起動時間を別プロセスから計測する。

.DESCRIPTION
  対象を起動し、(1) 対象プロセスの可視トップレベル ウィンドウが現れるまで、(2) そのウィンドウの
  本文領域（クライアント中央）が実際に描画されるまで（画面のピクセルがウィンドウ表示直後の色から
  最終的な色へ落ち着くまで）の時間を 1 ms 単位で測る。指定回数くり返し、中央値と最小値を出す。

  前提：実行中はマウス／キーボードに触らない。対象ウィンドウは画面内に出ること。

.PARAMETER Target
  'notepad'（Windows 11 の Store 版メモ帳）または MemoPad.exe のフル パス。

.PARAMETER Runs
  計測回数（既定 10）。1 回目はコールド スタート気味になるので、集計は 2 回目以降で行う。

.EXAMPLE
  .\docs\tools\measure-startup.ps1 -Target notepad
  .\docs\tools\measure-startup.ps1 -Target "$env:LOCALAPPDATA\Programs\MemoPad\MemoPad.exe"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Target,
    [int]$Runs = 10,
    [int]$TimeoutMs = 15000
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Collections.Generic; using System.Diagnostics; using System.Runtime.InteropServices;
public static class StartupProbe {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

  // 指定 PID 群に属する可視トップレベル ウィンドウを 1 つ返す
  public static IntPtr FindVisibleWindow(HashSet<uint> pids) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pids.Contains(pid) && IsWindowVisible(h)) { RECT r; GetClientRect(h, out r); if (r.Right > 200 && r.Bottom > 200) { found = h; return false; } }
      return true;
    }, IntPtr.Zero);
    return found;
  }

  // クライアント領域中央（本文）と、上端付近（タイトル行／メニュー行：WPF が描く領域）のピクセル色を画面から読む
  public static ulong Pixels(IntPtr h) {
    RECT r; GetClientRect(h, out r);
    POINT c = new POINT { X = r.Right / 2, Y = r.Bottom / 2 };
    POINT m = new POINT { X = r.Right / 2, Y = 30 };
    ClientToScreen(h, ref c); ClientToScreen(h, ref m);
    IntPtr dc = GetDC(IntPtr.Zero);
    uint pc = GetPixel(dc, c.X, c.Y); uint pm = GetPixel(dc, m.X, m.Y);
    ReleaseDC(IntPtr.Zero, dc);
    return ((ulong)pm << 32) | pc;
  }
}
"@
[StartupProbe]::SetProcessDPIAware() | Out-Null

$isNotepad = $Target -eq 'notepad'
$procName = if ($isNotepad) { 'Notepad' } else { [System.IO.Path]::GetFileNameWithoutExtension($Target) }

function Get-Pids { [uint32[]]@(Get-Process $procName -ErrorAction SilentlyContinue | ForEach-Object { [uint32]$_.Id }) }

$results = @()
for ($run = 1; $run -le $Runs; $run++) {
    # 既存のインスタンスを終了してから測る
    Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    $before = New-Object 'System.Collections.Generic.HashSet[uint32]'
    Get-Pids | ForEach-Object { [void]$before.Add($_) }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    if ($isNotepad) { Start-Process notepad.exe } else { Start-Process $Target }

    # 新しいプロセスの可視ウィンドウを待つ
    $hwnd = [IntPtr]::Zero; $tVisible = -1
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        $pids = New-Object 'System.Collections.Generic.HashSet[uint32]'
        Get-Pids | Where-Object { -not $before.Contains($_) } | ForEach-Object { [void]$pids.Add($_) }
        if ($pids.Count -gt 0) {
            $hwnd = [StartupProbe]::FindVisibleWindow($pids)
            if ($hwnd -ne [IntPtr]::Zero) { $tVisible = $sw.ElapsedMilliseconds; break }
        }
        Start-Sleep -Milliseconds 1
    }
    if ($hwnd -eq [IntPtr]::Zero) { Write-Warning "run $run : ウィンドウが現れませんでした"; continue }

    # 描画されるまで：本文中央とタイトル行の 2 点の色が 300 ms 変わらなくなった時点を「描画完了」とする
    $last = [StartupProbe]::Pixels($hwnd); $tLast = $tVisible; $tPainted = $tVisible
    $first = $last
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        $c = [StartupProbe]::Pixels($hwnd)
        $now = $sw.ElapsedMilliseconds
        if ($c -ne $last) { $last = $c; $tLast = $now }
        if ($now - $tLast -ge 300) { $tPainted = $tLast; break }
        Start-Sleep -Milliseconds 1
    }
    $results += [pscustomobject]@{ Run = $run; VisibleMs = $tVisible; PaintedMs = $tPainted; FirstColor = ('{0:X12}' -f $first); FinalColor = ('{0:X12}' -f $last) }
    Write-Host ("run {0,2}: ウィンドウ表示 {1,5} ms / 描画完了 {2,5} ms  (色 {3} -> {4})" -f $run, $tVisible, $tPainted, ('{0:X12}' -f $first), ('{0:X12}' -f $last))

    Start-Sleep -Milliseconds 400
    Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
}

if ($results.Count -gt 1) {
    $warm = $results | Select-Object -Skip 1
    function Median($xs) { $s = @($xs | Sort-Object); if ($s.Count % 2) { $s[[int](($s.Count - 1) / 2)] } else { ($s[$s.Count / 2 - 1] + $s[$s.Count / 2]) / 2 } }
    Write-Host ("== {0}: 1 回目（コールド）表示 {1} ms / 描画 {2} ms" -f $Target, $results[0].VisibleMs, $results[0].PaintedMs)
    Write-Host ("== {0}: 2 回目以降 {1} 回  表示 中央値 {2} ms（最小 {3}）/ 描画完了 中央値 {4} ms（最小 {5}）" -f $Target, $warm.Count,
        (Median ($warm | % VisibleMs)), ($warm | Measure-Object VisibleMs -Minimum).Minimum,
        (Median ($warm | % PaintedMs)), ($warm | Measure-Object PaintedMs -Minimum).Minimum)
}
