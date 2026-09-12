<#
.SYNOPSIS
  本文の編集まわりの確認（v0.10.0 で本文を Scintilla に替えたときの回帰確認）。

.DESCRIPTION
  MemoPad を起動してキー操作を送り、次を確かめる。

    1. 多段階の「元に戻す」「やり直し」が効くか
    2. 保存したファイルの改行コードが元のまま（CRLF / LF）保たれるか
    3. 日本語を含む本文が壊れずに保存できるか
    4. 未保存の印（タイトルの *）が、編集で付き、元に戻すと消えるか

  実行中はマウス／キーボードに触らないこと。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ExePath,
    [string]$WorkDir = $env:TEMP
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class Ed {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[Ed]::SetProcessDPIAware() | Out-Null

$procName = [System.IO.Path]::GetFileNameWithoutExtension($ExePath)
$enc = New-Object System.Text.UTF8Encoding($false)
$ok = 0; $ng = 0
function Check($name, $cond, $detail) {
    if ($cond) { Write-Output ("  OK  {0}  {1}" -f $name, $detail); $script:ok++ }
    else { Write-Output ("  NG  {0}  {1}" -f $name, $detail); $script:ng++ }
}

function Start-App([string]$file) {
    Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 600
    $p = Start-Process $ExePath -ArgumentList $file -PassThru
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 300; $p.Refresh(); if ($p.MainWindowHandle -ne 0) { $h = $p.MainWindowHandle; break } }
    if ($h -eq [IntPtr]::Zero) { throw 'ウィンドウが出ません' }
    [Ed]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 1200
    [uint32]$fp = 0
    [Ed]::GetWindowThreadProcessId([Ed]::GetForegroundWindow(), [ref]$fp) | Out-Null
    if ($fp -ne $p.Id) { throw '前面化できませんでした' }
    return @($p, $h)
}
function Title([IntPtr]$h) { $sb = New-Object System.Text.StringBuilder 256; [Ed]::GetWindowTextW($h, $sb, 256) | Out-Null; $sb.ToString() }
function Send($keys, $waitMs = 350) { [System.Windows.Forms.SendKeys]::SendWait($keys); Start-Sleep -Milliseconds $waitMs }

# --- 1・4：多段階の元に戻す／やり直しと、未保存の印
$f1 = Join-Path $WorkDir 'edit-undo.txt'
[System.IO.File]::WriteAllText($f1, "1 行目`r`n2 行目`r`n", $enc)
$r = Start-App $f1
$p = $r[0]; $h = $r[1]
Write-Output '== 多段階の元に戻す・やり直し =='
Check '開いた直後は未編集' (-not (Title $h).StartsWith('*')) ("タイトル: " + (Title $h))
Send '^{END}'
Send 'aaa' 250
Send 'bbb' 250
Send 'ccc' 400
Check '編集すると * が付く' ((Title $h).StartsWith('*')) ("タイトル: " + (Title $h))
Send '^z' 250
Send '^z' 250
Send '^z' 400
Check '3 回の元に戻すで * が消える（多段階 Undo）' (-not (Title $h).StartsWith('*')) ("タイトル: " + (Title $h))
Send '^y' 400
Check 'やり直しで * が戻る' ((Title $h).StartsWith('*')) ("タイトル: " + (Title $h))
Stop-Process -Id $p.Id -Force

# --- 2・3：保存の往復（CRLF と LF、日本語）
foreach ($case in @(
  @{ n = 'CRLF'; nl = "`r`n" },
  @{ n = 'LF';   nl = "`n" }
)) {
    $f = Join-Path $WorkDir ("edit-save-" + $case.n + ".txt")
    $body = "あいうえお abc 123" + $case.nl + "二行目の日本語テキスト" + $case.nl
    [System.IO.File]::WriteAllText($f, $body, $enc)
    $r = Start-App $f
    $p = $r[0]; $h = $r[1]
    Write-Output ("== 保存の往復（{0}）==" -f $case.n)
    Send '^{END}'
    Send '追記' 400
    Send '^s' 900
    Stop-Process -Id $p.Id -Force
    Start-Sleep -Milliseconds 400
    $saved = [System.IO.File]::ReadAllText($f)
    $crlf = ([regex]::Matches($saved, "`r`n")).Count
    $loneLf = ([regex]::Matches($saved, "(?<!`r)`n")).Count
    if ($case.n -eq 'CRLF') {
        Check '改行コードが CRLF のまま' (($crlf -ge 2) -and ($loneLf -eq 0)) ("CRLF={0} 単独LF={1}" -f $crlf, $loneLf)
    } else {
        Check '改行コードが LF のまま' (($crlf -eq 0) -and ($loneLf -ge 2)) ("CRLF={0} 単独LF={1}" -f $crlf, $loneLf)
    }
    Check '日本語が壊れていない' ($saved.Contains('あいうえお') -and $saved.Contains('二行目の日本語テキスト')) ("先頭: " + $saved.Split("`n")[0])
    Check '追記した文字が入っている' ($saved.Contains('追記')) ''
}

Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output ''
Write-Output ("結果: OK {0} 件 / NG {1} 件" -f $ok, $ng)
