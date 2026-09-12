<#
.SYNOPSIS
  大きなファイルを開いたときに UI が応答を取り戻すまでの時間を、別プロセスから計測する（FB-19 の調査用）。

.DESCRIPTION
  対象（MemoPad.exe またはメモ帳）にファイルのパスを引数で渡して起動し、次を 1 ms 単位で測る。

    1. 可視ウィンドウが現れるまで
    2. そのウィンドウが「応答を取り戻す」まで（WM_NULL に答え、かつ 500 ms 応答し続ける）
    3. 読み込み後の操作（最大化 → 元に戻す → 新しいタブ）それぞれで、再び応答を取り戻すまで

  「一度固まるとその後の操作でも固まる」（FB-19）を数値で確かめるために 3 を測る。
  MemoPad のときは -WordWrap で「右端で折り返す」を切り替えてから起動する
  （%APPDATA%\MemoPad\settings.json を退避し、終了後に必ず戻す）。

  前提：実行中はマウス／キーボードに触らない。

.PARAMETER ExePath
  MemoPad.exe のフル パス。'notepad' を指定すると Windows のメモ帳を測る。

.PARAMETER FilePath
  開くテキスト ファイルのフル パス。

.PARAMETER WordWrap
  'on' / 'off' / 'keep'（既定 keep＝設定を変えない）。MemoPad のときだけ効く。

.EXAMPLE
  .\docs\tools\measure-openfile.ps1 -ExePath "$env:LOCALAPPDATA\Programs\MemoPad\MemoPad.exe" `
      -FilePath D:\claude\memopad\tmp\big-ja-120k.txt -WordWrap on
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ExePath,
    [Parameter(Mandatory)] [string]$FilePath,
    [ValidateSet('on', 'off', 'keep')] [string]$WordWrap = 'keep',
    [int]$Runs = 1,
    [int]$TimeoutMs = 180000
)

$ErrorActionPreference = 'Stop'

Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class OpenProbe {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int max);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint ms, out IntPtr res);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

  public static IntPtr FindVisibleWindow(HashSet<uint> pids) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pids.Contains(pid) && IsWindowVisible(h)) { RECT r; GetClientRect(h, out r); if (r.Right > 200 && r.Bottom > 200) { found = h; return false; } }
      return true;
    }, IntPtr.Zero);
    return found;
  }

  // 本文のエディタ（RichEdit / メモ帳の Edit）を子ウィンドウから探す
  public static IntPtr FindEdit(IntPtr parent) {
    IntPtr found = IntPtr.Zero;
    EnumChildWindows(parent, (h, l) => {
      var sb = new StringBuilder(128); GetClassName(h, sb, sb.Capacity);
      string cls = sb.ToString();
      if (cls.StartsWith("RICHEDIT") || cls == "Edit" || cls.StartsWith("RichEdit")) {
        RECT r; GetClientRect(h, out r);
        if (r.Right > 100 && r.Bottom > 100) { found = h; return false; }
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }

  // WM_NULL に答えられるか（＝メッセージ ループが回っているか）
  public static bool Responsive(IntPtr h, uint ms) {
    IntPtr res;
    // SMTO_ABORTIFHUNG(2) | SMTO_BLOCK(1)
    return SendMessageTimeoutW(h, 0x0000, IntPtr.Zero, IntPtr.Zero, 3, ms, out res) != IntPtr.Zero;
  }
}
"@
[OpenProbe]::SetProcessDPIAware() | Out-Null

$isNotepad = $ExePath -eq 'notepad'
$procName = if ($isNotepad) { 'Notepad' } else { [System.IO.Path]::GetFileNameWithoutExtension($ExePath) }
$settingsPath = Join-Path $env:APPDATA 'MemoPad\settings.json'
$backup = $null

function Get-Pids { [uint32[]]@(Get-Process $procName -ErrorAction SilentlyContinue | ForEach-Object { [uint32]$_.Id }) }

# 応答が戻り、指定時間そのまま続くまで待つ。戻り値は「最後に固まっていた時刻」（＝応答が戻った時刻）
function Wait-Responsive([IntPtr]$hwnd, [System.Diagnostics.Stopwatch]$sw, [int]$stableMs = 500) {
    $lastBusy = $sw.ElapsedMilliseconds
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        if ([OpenProbe]::Responsive($hwnd, 60)) {
            if ($sw.ElapsedMilliseconds - $lastBusy -ge $stableMs) { return $lastBusy }
        } else {
            $lastBusy = $sw.ElapsedMilliseconds
        }
        Start-Sleep -Milliseconds 5
    }
    return -1
}

# 設定の「右端で折り返す」を切り替える（UTF-8 のまま読み書きする）
function Set-WordWrap([string]$value) {
    if ($isNotepad -or $value -eq 'keep') { return }
    if (-not (Test-Path $settingsPath)) { Write-Warning "設定ファイルが見つかりません: $settingsPath"; return }
    $script:backup = [System.IO.File]::ReadAllBytes($settingsPath)
    $text = [System.Text.Encoding]::UTF8.GetString($script:backup)
    $want = if ($value -eq 'on') { 'true' } else { 'false' }
    $text = [regex]::Replace($text, '"WordWrap"\s*:\s*(true|false)', ('"WordWrap": ' + $want))
    [System.IO.File]::WriteAllBytes($settingsPath, (New-Object System.Text.UTF8Encoding($false)).GetBytes($text))
}

function Restore-Settings {
    if ($null -ne $script:backup) {
        [System.IO.File]::WriteAllBytes($settingsPath, $script:backup)
        $script:backup = $null
    }
}

$results = @()
try {
    Set-WordWrap $WordWrap
    for ($run = 1; $run -le $Runs; $run++) {
        Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 800
        $before = New-Object 'System.Collections.Generic.HashSet[uint32]'
        Get-Pids | ForEach-Object { [void]$before.Add($_) }

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        if ($isNotepad) { Start-Process notepad.exe -ArgumentList $FilePath } else { Start-Process $ExePath -ArgumentList $FilePath }

        # 1) 可視ウィンドウが現れるまで
        $hwnd = [IntPtr]::Zero; $tVisible = -1
        while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
            $pids = New-Object 'System.Collections.Generic.HashSet[uint32]'
            Get-Pids | Where-Object { -not $before.Contains($_) } | ForEach-Object { [void]$pids.Add($_) }
            if ($pids.Count -gt 0) {
                $hwnd = [OpenProbe]::FindVisibleWindow($pids)
                if ($hwnd -ne [IntPtr]::Zero) { $tVisible = $sw.ElapsedMilliseconds; break }
            }
            Start-Sleep -Milliseconds 2
        }
        if ($hwnd -eq [IntPtr]::Zero) { Write-Warning "run $run : ウィンドウが現れませんでした"; continue }

        # 2) 応答を取り戻すまで（＝ファイルの読み込みと最初のレイアウトが終わるまで）
        $tReady = Wait-Responsive $hwnd $sw 500
        Write-Host ("run {0}: ウィンドウ表示 {1} ms / 応答回復 {2} ms" -f $run, $tVisible, $tReady)

        # 読み込みが本当に終わっているか、エディタの行数で確かめる
        $edit = [OpenProbe]::FindEdit($hwnd)
        $lines = -1
        if ($edit -ne [IntPtr]::Zero) { $lines = [int][OpenProbe]::SendMessageW($edit, 0x00BA, [IntPtr]::Zero, [IntPtr]::Zero) }  # EM_GETLINECOUNT

        # 3) 読み込み後の操作ごとに、再び応答を取り戻すまでを測る
        $script:ops = @()
        function Measure-Op([string]$name, [scriptblock]$action) {
            $s = [System.Diagnostics.Stopwatch]::StartNew()
            & $action
            Start-Sleep -Milliseconds 30
            $lastBusy = 0
            while ($s.ElapsedMilliseconds -lt $TimeoutMs) {
                if ([OpenProbe]::Responsive($hwnd, 60)) {
                    if ($s.ElapsedMilliseconds - $lastBusy -ge 500) { break }
                } else { $lastBusy = $s.ElapsedMilliseconds }
                Start-Sleep -Milliseconds 5
            }
            Write-Host ("    {0,-16}: {1,7} ms" -f $name, $lastBusy)
            $script:ops += [pscustomobject]@{ Op = $name; Ms = $lastBusy }
        }

        $WM_SYSCOMMAND = 0x0112; $SC_MAXIMIZE = 0xF030; $SC_RESTORE = 0xF120
        Measure-Op '最大化' { [OpenProbe]::PostMessage($hwnd, $WM_SYSCOMMAND, [IntPtr]$SC_MAXIMIZE, [IntPtr]::Zero) | Out-Null }
        Measure-Op '元のサイズ' { [OpenProbe]::PostMessage($hwnd, $WM_SYSCOMMAND, [IntPtr]$SC_RESTORE, [IntPtr]::Zero) | Out-Null }
        if (-not $isNotepad) {
            Measure-Op '新しいタブ' {
                [OpenProbe]::SetForegroundWindow($hwnd) | Out-Null
                Start-Sleep -Milliseconds 150
                [OpenProbe]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)          # Ctrl 押下
                [OpenProbe]::keybd_event(0x4E, 0, 0, [UIntPtr]::Zero)          # N 押下
                [OpenProbe]::keybd_event(0x4E, 0, 2, [UIntPtr]::Zero)          # N 離す
                [OpenProbe]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)          # Ctrl 離す
            }
        }

        $results += [pscustomobject]@{ Run = $run; VisibleMs = $tVisible; ReadyMs = $tReady; Lines = $lines; Ops = $script:ops }
        Start-Sleep -Milliseconds 300
        Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
    }
}
finally {
    Restore-Settings
}

Write-Host ""
Write-Host ("== {0} / 折り返し {1} / {2}" -f $procName, $WordWrap, (Split-Path $FilePath -Leaf))
foreach ($r in $results) {
    Write-Host ("   run {0}: 表示 {1} ms, 応答回復 {2} ms, 行数 {3}" -f $r.Run, $r.VisibleMs, $r.ReadyMs, $r.Lines)
}
