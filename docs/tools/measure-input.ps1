<#
.SYNOPSIS
  キーを打ってから画面に文字が出るまでの時間を、別プロセスから計測する（FB-1 の指標）。

.DESCRIPTION
  対象（MemoPad.exe またはメモ帳）を新規の空文書で起動し、本文の先頭付近を画面から写し取って
  おいてからキーを 1 つ送り、その範囲のピクセルが変わるまでの時間を 1 ms 単位で測る。
  これを指定回数くり返し、中央値・最小・最大を出す。

  画面の更新は 60 Hz（16.7 ms ごと）なので、結果はその粒度に丸まる。カーソルの点滅を拾って
  極端に小さい値が出ることがあるため、代表値は中央値で見る。

  前提：実行中はマウス／キーボードに触らない（キーは対象のウィンドウへ送られる）。

.PARAMETER ExePath
  MemoPad.exe のフル パス。'notepad' を指定すると Windows のメモ帳を測る。

.PARAMETER Keys
  送るキーの数（既定 20）。

.PARAMETER FilePath
  指定すると、そのファイルを開いた状態で測る（既定は新規の空文書）。大きなファイルを指定すると、
  読み込み直後の「折り返しの計算中」に打ったときのレスポンスが分かる。

.PARAMETER Experiment
  MemoPad に渡す環境変数 MEMOPAD_EXP の値（対策候補の比較用。既定は空＝現行の動作）。

.EXAMPLE
  .\docs\tools\measure-input.ps1 -ExePath "$env:LOCALAPPDATA\Programs\MemoPad\MemoPad.exe"
  .\docs\tools\measure-input.ps1 -ExePath notepad -Keys 20
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ExePath,
    [int]$Keys = 20,
    [string]$FilePath = '',
    [int]$WarmupMs = -1,
    [string]$Experiment = '',
    [int]$TimeoutMs = 3000
)
$ErrorActionPreference = 'Stop'

Add-Type @"
using System; using System.Collections.Generic; using System.Diagnostics; using System.Runtime.InteropServices; using System.Text;
public static class InputProbe {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int max);
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
  [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
  [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int dx, int dy, int w, int h, IntPtr s, int sx, int sy, uint rop);
  [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFO bi, uint usage);
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
  [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint c0, c1, c2; }

  static int rx, ry, rw, rh;
  static IntPtr memDc, bmp, oldBmp;
  static byte[] bits;
  static BITMAPINFO bi;

  public static IntPtr WaitMain(int pid, int timeoutMs) {
    var sw = Stopwatch.StartNew(); IntPtr found = IntPtr.Zero;
    while (sw.ElapsedMilliseconds < timeoutMs) {
      EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p);
        if (p == (uint)pid && IsWindowVisible(h)) { RECT r; GetClientRect(h, out r);
          if (r.Right > 200 && r.Bottom > 200) { found = h; return false; } }
        return true; }, IntPtr.Zero);
      if (found != IntPtr.Zero) return found;
      System.Threading.Thread.Sleep(2);
    }
    return IntPtr.Zero;
  }

  // 本文のコントロール（Scintilla / RichEdit）を探す。見つからなければメイン ウィンドウを使う
  public static IntPtr FindEditor(IntPtr parent) {
    IntPtr found = IntPtr.Zero;
    EnumChildWindows(parent, (h, l) => {
      var sb = new StringBuilder(256); GetClassName(h, sb, 256);
      var cls = sb.ToString();
      if (cls.IndexOf("Scintilla", StringComparison.OrdinalIgnoreCase) >= 0 ||
          cls.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0 ||
          cls.Equals("Edit", StringComparison.OrdinalIgnoreCase)) { found = h; return false; }
      return true; }, IntPtr.Zero);
    return found == IntPtr.Zero ? parent : found;
  }

  // 本文の先頭付近（1 行目の左側）を写し取る範囲を決める
  public static string Prepare(IntPtr editor, int width, int height) {
    RECT r; GetClientRect(editor, out r);
    POINT p = new POINT { X = 0, Y = 0 }; ClientToScreen(editor, ref p);
    rx = p.X; ry = p.Y; rw = Math.Min(width, r.Right); rh = Math.Min(height, r.Bottom);
    IntPtr screen = GetDC(IntPtr.Zero);
    memDc = CreateCompatibleDC(screen); bmp = CreateCompatibleBitmap(screen, rw, rh);
    oldBmp = SelectObject(memDc, bmp);
    ReleaseDC(IntPtr.Zero, screen);
    bits = new byte[rw * rh * 4];
    bi = new BITMAPINFO();
    bi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
    bi.bmiHeader.biWidth = rw; bi.bmiHeader.biHeight = -rh; bi.bmiHeader.biPlanes = 1; bi.bmiHeader.biBitCount = 32;
    return string.Format("({0},{1}) {2}x{3}", rx, ry, rw, rh);
  }

  // 範囲を写し取ってチェックサムを返す
  public static long Snapshot() {
    IntPtr screen = GetDC(IntPtr.Zero);
    BitBlt(memDc, 0, 0, rw, rh, screen, rx, ry, 0x00CC0020 /*SRCCOPY*/);
    ReleaseDC(IntPtr.Zero, screen);
    GetDIBits(memDc, bmp, 0, (uint)rh, bits, ref bi, 0);
    long sum = 17;
    for (int i = 0; i < bits.Length; i += 4) sum = sum * 31 + bits[i] + bits[i + 1] * 7 + bits[i + 2] * 13;
    return sum;
  }

  public static void Cleanup() {
    if (memDc != IntPtr.Zero) { SelectObject(memDc, oldBmp); DeleteObject(bmp); DeleteDC(memDc); memDc = IntPtr.Zero; }
  }

  // キーを 1 つ送り、写し取った範囲が変わるまでの ms を返す（変わらなければ -1）
  public static double TypeAndWait(byte vk, int timeoutMs) {
    long before = Snapshot();
    var sw = Stopwatch.StartNew();
    keybd_event(vk, 0, 0, UIntPtr.Zero);
    keybd_event(vk, 0, 2 /*KEYEVENTF_KEYUP*/, UIntPtr.Zero);
    while (sw.Elapsed.TotalMilliseconds < timeoutMs) {
      if (Snapshot() != before) return sw.Elapsed.TotalMilliseconds;
    }
    return -1;
  }
}
"@
[InputProbe]::SetProcessDPIAware() | Out-Null

$isNotepad = $ExePath -eq 'notepad'
$procName = if ($isNotepad) { 'Notepad' } else { [System.IO.Path]::GetFileNameWithoutExtension($ExePath) }
Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

$old = $env:MEMOPAD_EXP
$env:MEMOPAD_EXP = $Experiment
$p = $null
try {
    $args = if ($FilePath) { @("`"$FilePath`"") } else { @() }
    $p = if ($isNotepad) { Start-Process notepad.exe -ArgumentList $args -PassThru }
         elseif ($args.Count) { Start-Process $ExePath -ArgumentList $args -PassThru }
         else { Start-Process $ExePath -PassThru }
    $main = [InputProbe]::WaitMain($p.Id, 20000)
    if ($main -eq [IntPtr]::Zero) { throw 'ウィンドウが見つからない' }
    # ファイルを指定したときは、読み込み直後（計算中）に打ちたいので既定の待ち時間を短くする
    $wait = if ($WarmupMs -ge 0) { $WarmupMs } elseif ($FilePath) { 300 } else { 1200 }
    Start-Sleep -Milliseconds $wait
    [InputProbe]::SetForegroundWindow($main) | Out-Null
    Start-Sleep -Milliseconds 400
    $editor = [InputProbe]::FindEditor($main)
    $area = [InputProbe]::Prepare($editor, 600, 60)

    $samples = @()
    for ($i = 0; $i -lt $Keys; $i++) {
        Start-Sleep -Milliseconds $(if ($FilePath) { 120 } else { 250 })
        $ms = [InputProbe]::TypeAndWait(0x41, $TimeoutMs)   # 'A' キー
        if ($ms -ge 0) { $samples += $ms } else { Write-Verbose "  $i 回目：制限時間内に変化なし" }
    }
    [InputProbe]::Cleanup()

    $sorted = @($samples | Sort-Object)
    $median = if ($sorted.Count % 2) { $sorted[[int](($sorted.Count - 1) / 2)] } else { ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2 }
    $label = if ($Experiment) { " (MEMOPAD_EXP=$Experiment)" } else { '' }
    Write-Host ("対象 {0}{1} / 範囲 {2} / 有効 {3} 回" -f $ExePath, $label, $area, $sorted.Count)
    Write-Host ("入力→表示：中央値 {0:F1} ms（最小 {1:F1} / 最大 {2:F1}）" -f $median, $sorted[0], $sorted[-1])
    [pscustomobject]@{
        Target = $ExePath; Experiment = $Experiment; ValidCount = $sorted.Count
        MedianMs = [math]::Round($median, 1); MinMs = [math]::Round($sorted[0], 1); MaxMs = [math]::Round($sorted[-1], 1)
        Samples = ($sorted | ForEach-Object { [math]::Round($_, 1) }) -join ' '
    }
} finally {
    $env:MEMOPAD_EXP = $old
    if ($p -and -not $p.HasExited) { $p.Kill(); $p.WaitForExit(5000) }
}
