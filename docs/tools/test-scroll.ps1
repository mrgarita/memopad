<#
.SYNOPSIS
  本文のスクロールまわりの確認（FB-17・FB-18 の回帰確認）。

.DESCRIPTION
  MemoPad を起動して次を確かめる。

    1. 末尾が改行のファイルで、マウス ホイールを回し切ったときに一番下（最後の空行）まで届くか
    2. 「右端で折り返す」をオフにしたとき、長い行で横スクロールバーが出て横に送れるか

  本文のウィンドウ（Scintilla）に GetScrollInfo を送って位置を読む。
  設定は退避して最後に戻す。実行中はマウス／キーボードに触らないこと。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ExePath,
    [string]$WorkDir = $env:TEMP
)

$ErrorActionPreference = 'Stop'
Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class Scr {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int max);
  [DllImport("user32.dll")] public static extern bool GetScrollInfo(IntPtr h, int bar, ref SCROLLINFO si);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct SCROLLINFO { public uint cbSize, fMask; public int nMin, nMax; public uint nPage; public int nPos, nTrackPos; }

  public static IntPtr FindTop(HashSet<uint> pids) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h,l) => { uint p; GetWindowThreadProcessId(h, out p);
      if (pids.Contains(p) && IsWindowVisible(h)) { RECT r; GetClientRect(h, out r); if (r.Right > 200 && r.Bottom > 200) { found = h; return false; } }
      return true; }, IntPtr.Zero);
    return found;
  }
  public static IntPtr FindEditor(IntPtr parent) {
    IntPtr found = IntPtr.Zero;
    EnumChildWindows(parent, (h,l) => { var sb = new StringBuilder(128); GetClassName(h, sb, sb.Capacity);
      // Windows Forms がクラス名を "WindowsForms10.Scintilla.app..." のように付け替えるので部分一致で探す
      if (sb.ToString().IndexOf("Scintilla", StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  public static string Info(IntPtr h, int bar) {
    SCROLLINFO si = new SCROLLINFO(); si.cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO)); si.fMask = 0x17;
    if (!GetScrollInfo(h, bar, ref si)) return "(取得できず)";
    return string.Format("min={0} max={1} page={2} pos={3} 一番下={4}", si.nMin, si.nMax, si.nPage, si.nPos, si.nMax - (int)si.nPage + 1);
  }
  public static int Pos(IntPtr h, int bar) {
    SCROLLINFO si = new SCROLLINFO(); si.cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO)); si.fMask = 0x17;
    GetScrollInfo(h, bar, ref si); return si.nPos;
  }
  public static int Bottom(IntPtr h, int bar) {
    SCROLLINFO si = new SCROLLINFO(); si.cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO)); si.fMask = 0x17;
    GetScrollInfo(h, bar, ref si); return si.nMax - (int)si.nPage + 1;
  }
  public static bool HasHScroll(IntPtr h) { return (GetWindowLong(h, -16) & 0x00100000) != 0; }  // GWL_STYLE / WS_HSCROLL
}
"@
[Scr]::SetProcessDPIAware() | Out-Null

$settingsPath = Join-Path $env:APPDATA 'MemoPad\settings.json'
$backup = if (Test-Path $settingsPath) { [System.IO.File]::ReadAllBytes($settingsPath) } else { $null }
$procName = [System.IO.Path]::GetFileNameWithoutExtension($ExePath)
$enc = New-Object System.Text.UTF8Encoding($false)

function Set-WordWrap([bool]$on) {
    if ($null -eq $backup) { return }
    $text = [System.Text.Encoding]::UTF8.GetString($backup)
    $text = [regex]::Replace($text, '"WordWrap"\s*:\s*(true|false)', ('"WordWrap": ' + $on.ToString().ToLower()))
    [System.IO.File]::WriteAllBytes($settingsPath, $enc.GetBytes($text))
}

function Start-App([string]$file) {
    Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 600
    $before = New-Object 'System.Collections.Generic.HashSet[uint32]'
    Get-Process $procName -ErrorAction SilentlyContinue | ForEach-Object { [void]$before.Add([uint32]$_.Id) }
    Start-Process $ExePath -ArgumentList $file | Out-Null
    Start-Sleep -Seconds 3
    $pids = New-Object 'System.Collections.Generic.HashSet[uint32]'
    Get-Process $procName -ErrorAction SilentlyContinue | Where-Object { -not $before.Contains([uint32]$_.Id) } | ForEach-Object { [void]$pids.Add([uint32]$_.Id) }
    $top = [Scr]::FindTop($pids)
    if ($top -eq [IntPtr]::Zero) { throw 'ウィンドウが見つかりません' }
    [Scr]::SetForegroundWindow($top) | Out-Null
    Start-Sleep -Milliseconds 500
    return @($top, [Scr]::FindEditor($top))
}

function Wheel([IntPtr]$editor, [int]$notches) {
    $r = New-Object Scr+RECT
    [Scr]::GetWindowRect($editor, [ref]$r) | Out-Null
    [Scr]::SetCursorPos([int](($r.Left + $r.Right) / 2), [int](($r.Top + $r.Bottom) / 2)) | Out-Null
    Start-Sleep -Milliseconds 150
    for ($i = 0; $i -lt [Math]::Abs($notches); $i++) {
        [Scr]::mouse_event(0x0800, 0, 0, $(if ($notches -gt 0) { -120 } else { 120 }), [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Milliseconds 400
}

try {
    # --- FB-17：末尾が改行のファイルで、ホイールで一番下まで届くか
    $f1 = Join-Path $WorkDir 'scroll-tail.txt'
    $lines = New-Object string[] 200
    for ($i = 0; $i -lt 200; $i++) { $lines[$i] = ("{0} 行目：スクロールの確認" -f ($i + 1)) }
    [System.IO.File]::WriteAllText($f1, (($lines -join "`r`n") + "`r`n"), $enc)   # 末尾に改行あり

    Set-WordWrap $true
    $h = Start-App $f1
    $top = $h[0]; $editor = $h[1]
    if ($editor -eq [IntPtr]::Zero) { throw '本文のウィンドウ（Scintilla）が見つかりません' }
    Write-Output '== FB-17：末尾が改行のファイルをホイールで送り切る =='
    Write-Output ("  送る前 : {0}" -f [Scr]::Info($editor, 1))
    Wheel $editor 80
    $pos = [Scr]::Pos($editor, 1); $bottom = [Scr]::Bottom($editor, 1)
    Write-Output ("  送った後: {0}" -f [Scr]::Info($editor, 1))
    Write-Output ("  判定    : {0}（位置 {1} / 一番下 {2}）" -f $(if ($pos -ge $bottom) { 'OK 一番下まで届く' } else { 'NG 届かない' }), $pos, $bottom)
    Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force

    # --- FB-18：折り返しオフで横スクロールバーが出るか
    $f2 = Join-Path $WorkDir 'scroll-long.txt'
    $long = ('あいうえおかきくけこ' * 20) + ('abcdefghij' * 20)
    $l2 = New-Object string[] 60
    for ($i = 0; $i -lt 60; $i++) { $l2[$i] = $long }
    [System.IO.File]::WriteAllText($f2, (($l2 -join "`r`n") + "`r`n"), $enc)

    Set-WordWrap $false
    $h = Start-App $f2
    $top = $h[0]; $editor = $h[1]
    Write-Output ''
    Write-Output '== FB-18：折り返しオフで横スクロール =='
    Write-Output ("  WS_HSCROLL: {0}" -f [Scr]::HasHScroll($editor))
    Write-Output ("  横の範囲  : {0}" -f [Scr]::Info($editor, 0))
    $before = [Scr]::Pos($editor, 0)
    # Shift＋ホイールで横に送る
    [Scr]::mouse_event(0x0800, 0, 0, -120, [UIntPtr]::Zero)
    Wheel $editor 0
    $r = New-Object Scr+RECT
    [Scr]::GetWindowRect($editor, [ref]$r) | Out-Null
    [Scr]::SetCursorPos([int](($r.Left + $r.Right) / 2), [int](($r.Top + $r.Bottom) / 2)) | Out-Null
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.SendKeys]::SendWait('{END}')
    Start-Sleep -Milliseconds 600
    $after = [Scr]::Pos($editor, 0)
    Write-Output ("  End キーで行末へ: 横位置 {0} → {1}  {2}" -f $before, $after, $(if ($after -gt $before) { 'OK 横に送れる' } else { 'NG 送れない' }))
    Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
}
finally {
    if ($null -ne $backup) { [System.IO.File]::WriteAllBytes($settingsPath, $backup) }
    Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
}
