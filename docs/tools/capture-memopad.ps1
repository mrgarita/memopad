<#
.SYNOPSIS
  MemoPad のスクリーンショットを docs/site/img/step2/ へ一括生成する（動作確認を兼ねる）。

.DESCRIPTION
  ビルド済みの MemoPad.exe をサンプル ファイル付きで起動し、メニュー・検索／置換・各ダイアログ・
  色の変更・ダーク テーマ・タブ・未保存確認を UI Automation とキー操作で再現しながら撮影する。
  ユーザーの設定ファイル（%APPDATA%\memopad\settings.json）は退避し、終了時に元へ戻す。

  前提：実行中はマウス／キーボードに触らない（前面ウィンドウの確認に失敗すると中断する）。

.PARAMETER ExePath
  撮影対象の MemoPad.exe。既定は Debug ビルドの出力。

.PARAMETER OutDir
  出力先フォルダ。既定は docs/site/img/step2。

.EXAMPLE
  dotnet build src\memopad\memopad.csproj
  .\docs\tools\capture-memopad.ps1
#>
[CmdletBinding()]
param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\..\src\memopad\bin\Debug\net9.0-windows\memopad.exe'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\site\img\step2')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class NativeWin2 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hgt, bool repaint);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[NativeWin2]::SetProcessDPIAware() | Out-Null

$ExePath = [System.IO.Path]::GetFullPath($ExePath)
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
if (-not (Test-Path $ExePath)) { throw "MemoPad.exe が見つかりません: $ExePath（先に dotnet build してください）" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$AE = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]
$TS = [System.Windows.Automation.TreeScope]
$SK = [System.Windows.Forms.SendKeys]
$desk = $AE::RootElement

# --- ユーザーの設定を退避し、既定の状態で撮影する
$settingsDir = Join-Path $env:APPDATA 'memopad'
$settingsPath = Join-Path $settingsDir 'settings.json'
$backupPath = Join-Path $env:TEMP 'memopad_settings_backup.json'
$hadSettings = Test-Path $settingsPath
if ($hadSettings) { Copy-Item $settingsPath $backupPath -Force; Remove-Item $settingsPath -Force }

# --- サンプル ファイル
$sample = Join-Path $env:TEMP 'memopad_sample.txt'
@(
  'memopad の動作確認用サンプル',
  '',
  'このファイルはスクリーンショット撮影用です。',
  '背景色と文字色を自由に変えられるメモ帳を目指しています。',
  '',
  '1. ファイル操作（新規・開く・保存・印刷）',
  '2. 編集操作（元に戻す・検索・置換・行へ移動）',
  '3. 表示（ズーム・ステータスバー・右端で折り返す）',
  '4. 書式（フォント・背景色・文字色・テーマ）'
) | Set-Content -Path $sample -Encoding UTF8

Get-Process memopad -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$np = Start-Process -FilePath $ExePath -ArgumentList "`"$sample`"" -PassThru
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    $np.Refresh()
    if ($np.MainWindowHandle -ne 0) { $hwnd = $np.MainWindowHandle; break }
}
if ($hwnd -eq [IntPtr]::Zero) { throw 'MemoPad のウィンドウが見つかりません。' }
[NativeWin2]::MoveWindow($hwnd, 80, 60, 1000, 720, $true) | Out-Null
Start-Sleep -Seconds 1

# --- ヘルパー（capture-notepad.ps1 と同じ考え方）
function PC($prop, $val) { New-Object System.Windows.Automation.PropertyCondition($prop, $val) }
function AndC { param([System.Windows.Automation.Condition[]]$c) New-Object System.Windows.Automation.AndCondition($c) }
function ProcFirst($cond) { $desk.FindFirst($TS::Descendants, (AndC @((PC $AE::ProcessIdProperty $np.Id), $cond))) }
function ProcWait($cond, [int]$timeoutMs = 4000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) { $el = ProcFirst $cond; if ($el) { return $el }; Start-Sleep -Milliseconds 250 }
    return $null
}
function FgIsApp { [uint32]$fgPid = 0; [NativeWin2]::GetWindowThreadProcessId([NativeWin2]::GetForegroundWindow(), [ref]$fgPid) | Out-Null; return ($fgPid -eq $np.Id) }
function Fg([IntPtr]$h = $hwnd) {
    if (FgIsApp) { return }
    for ($i = 0; $i -lt 10; $i++) {
        [uint32]$fgPid2 = 0
        $fgThread = [NativeWin2]::GetWindowThreadProcessId([NativeWin2]::GetForegroundWindow(), [ref]$fgPid2)
        $me = [NativeWin2]::GetCurrentThreadId()
        [NativeWin2]::AttachThreadInput($me, $fgThread, $true) | Out-Null
        [NativeWin2]::ShowWindow($h, 9) | Out-Null
        [NativeWin2]::SetForegroundWindow($h) | Out-Null
        [NativeWin2]::AttachThreadInput($me, $fgThread, $false) | Out-Null
        Start-Sleep -Milliseconds 500
        if (FgIsApp) { return }
    }
    throw "ウィンドウを前面化できませんでした（hwnd=$h）。キー入力の誤送信を防ぐため中断します。"
}
function Keys($k, $wait = 700, [IntPtr]$h = $hwnd) { if (-not (FgIsApp)) { Fg $h }; $SK::SendWait($k); Start-Sleep -Milliseconds $wait }
function Invoke-El($el) {
    if (-not $el) { Write-Warning '対象の要素が見つかりません'; return }
    try { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    catch { $el.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand() }
    Start-Sleep -Milliseconds 700
}
# ウィンドウは (80,60) に置くので、画面の左上隅はどの撮影範囲にも入らない
function ParkCursor { [NativeWin2]::SetCursorPos(5, 5) | Out-Null; Start-Sleep -Milliseconds 120 }
function ByName($name, $type) { ProcWait (AndC @((PC $AE::NameProperty $name), (PC $AE::ControlTypeProperty $type))) }
function TopWindow($name) { ProcWait (AndC @((PC $AE::NameProperty $name), (PC $AE::ControlTypeProperty $CT::Window))) 6000 }
function Shot($name, [IntPtr]$h = $hwnd) {
    if (-not (FgIsApp)) { Fg $h }
    ParkCursor
    Start-Sleep -Milliseconds 350
    $r = New-Object NativeWin2+RECT
    [NativeWin2]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $hh)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  撮影: $name.png ($w x $hh)"
}
# ダイアログ（別トップレベル ウィンドウ）を、メイン ウィンドウごと 1 枚に収めて撮る
function ShotWithDialog($name, $dialogName) {
    $dlg = TopWindow $dialogName
    if (-not $dlg) { Write-Warning "ダイアログが見つかりません: $dialogName"; Shot $name; return $null }
    if (-not (FgIsApp)) { Fg }
    ParkCursor
    Start-Sleep -Milliseconds 350
    $r = New-Object NativeWin2+RECT
    [NativeWin2]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $d = $dlg.Current.BoundingRectangle
    # ダイアログがメイン ウィンドウの外へはみ出していれば、その分だけ撮影範囲を広げる
    $left = [Math]::Min($r.Left, [int]$d.Left); $top = [Math]::Min($r.Top, [int]$d.Top)
    $right = [Math]::Max($r.Right, [int]$d.Right); $bottom = [Math]::Max($r.Bottom, [int]$d.Bottom)
    $bmp = New-Object System.Drawing.Bitmap(($right - $left), ($bottom - $top))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($left, $top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  撮影: $name.png ($($right - $left) x $($bottom - $top))"
    return $dlg
}
function CloseDialog($dlg, $buttonName = 'キャンセル') {
    if (-not $dlg) { return }
    $b = $dlg.FindFirst($TS::Descendants, (AndC @((PC $AE::NameProperty $buttonName), (PC $AE::ControlTypeProperty $CT::Button))))
    if ($b) { Invoke-El $b } else { Keys '{ESC}' 500 ([IntPtr]$dlg.Current.NativeWindowHandle) }
    Start-Sleep -Milliseconds 400
}

try {
    Write-Host "出力先: $OutDir"

    # 01 メイン画面（ライト・既定色）
    Fg; Shot '01_main'

    # 02-05 メニュー
    Fg; Keys '%f'; Shot '02_menu_file'; Keys '{ESC}{ESC}' 400
    Fg; Keys '%e'; Shot '03_menu_edit'; Keys '{ESC}{ESC}' 400
    Fg; Keys '%v'; Keys 'z'; Shot '04_menu_view_zoom'; Keys '{ESC}{ESC}{ESC}' 400
    Fg; Keys '%o' 900; Shot '05_menu_format'; Keys '{ESC}{ESC}' 400

    # 06-07 検索／置換バー
    Fg; Keys '^f' 800; Set-Clipboard -Value 'メモ帳'; Keys '^v' 500; Keys '{ENTER}' 600; Shot '06_find'
    Keys '^h' 800; Shot '07_replace'; Keys '{ESC}' 500

    # 08 行に移動
    Fg; Keys '^g' 900; $dlg = ShotWithDialog '08_goto' '行に移動'; CloseDialog $dlg

    # 09 フォント ダイアログ
    Fg; Keys '%o' 500; Keys 'f' 1200; $dlg = ShotWithDialog '09_font_dialog' 'フォント'; CloseDialog $dlg

    # 10 色変更ダイアログで背景色を濃い青、文字色を黄にする（PICO-8 の 16 色から選ぶ）
    Fg; Keys '%o' 500; Keys 'c' 1200
    $dlg = TopWindow '色変更'
    if ($dlg) {
        Invoke-El (ByName '濃い青 #1D2B53' $CT::Button)
        $fgSwatch = $dlg.FindAll($TS::Descendants, (AndC @((PC $AE::NameProperty '黄 #FFEC27'), (PC $AE::ControlTypeProperty $CT::Button))))
        if ($fgSwatch.Count -ge 2) { Invoke-El $fgSwatch.Item(1) } elseif ($fgSwatch.Count -ge 1) { Invoke-El $fgSwatch.Item(0) }
    }
    ShotWithDialog '10_color_dialog' '色変更' | Out-Null
    CloseDialog $dlg 'OK'
    Shot '10b_colors_navy_yellow'

    # 11 その他の色（Windows 標準の色の設定ダイアログ）：色変更ダイアログから開く
    Fg; Keys '%o' 500; Keys 'c' 1200
    $colorDlg = TopWindow '色変更'
    if ($colorDlg) { Invoke-El ($colorDlg.FindFirst($TS::Descendants, (AndC @((PC $AE::NameProperty 'その他の色(M)...'), (PC $AE::ControlTypeProperty $CT::Button))))) }
    Start-Sleep -Milliseconds 800
    # Windows のダイアログは画面の別の場所に出るので、ダイアログだけを撮る
    $dlg = TopWindow '色の設定'
    if ($dlg) { Shot '11_color_picker' ([IntPtr]$dlg.Current.NativeWindowHandle) } else { Write-Warning '色の設定ダイアログが見つかりません' }
    CloseDialog $dlg
    CloseDialog $colorDlg 'キャンセル'

    # 11b 配色パターン：「夜」を選んで反映する
    Fg; Keys '%o' 500; Keys 'p' 1200
    $schemeDlg = TopWindow '配色パターン'
    if ($schemeDlg) { Invoke-El ($schemeDlg.FindFirst($TS::Descendants, (AndC @((PC $AE::NameProperty '夜'), (PC $AE::ControlTypeProperty $CT::Button))))) }
    ShotWithDialog '11b_color_scheme_dialog' '配色パターン' | Out-Null
    CloseDialog $schemeDlg 'OK'
    Shot '11c_color_scheme_night'

    # 12 既定の色に戻してダーク テーマへ
    Fg; Keys '%o' 500; Keys 'd' 800
    Fg; Keys '%o' 500; Keys 'm' 600; Keys 'd' 1500; Shot '12_dark_theme'

    # 13 2 つ目のタブに入力して未保存（●）の表示
    Fg; Keys '^n' 800
    Set-Clipboard -Value "2 つ目のタブに入力したテキスト。まだ保存していないのでタブに●が付き、タイトルバーに * が付く。"
    Keys '^v' 800; Shot '13_tabs_unsaved'

    # 14 未保存のタブを閉じようとしたときの確認
    Fg; Keys '^w' 1000; $dlg = ShotWithDialog '14_save_changes' 'MemoPad'; CloseDialog $dlg 'キャンセル'

    # 15 ページ設定
    Fg; Keys '%f' 500; Keys 'u' 1000; $dlg = ShotWithDialog '15_page_setup' 'ページ設定'; CloseDialog $dlg

    # 16 バージョン情報
    Fg; Keys '%h' 500; Keys 'a' 1000; $dlg = ShotWithDialog '16_about' 'MemoPad について'; CloseDialog $dlg 'OK'

    # 後片付け：ライト テーマに戻し、未保存タブは保存せずに閉じる
    Fg; Keys '%o' 500; Keys 'm' 600; Keys 'l' 800
    Fg; Keys '^+w' 1000
    $dlg = TopWindow 'MemoPad'
    if ($dlg) { CloseDialog $dlg '保存しない(N)' }
    Start-Sleep -Seconds 1
    Write-Host '完了'
}
finally {
    Get-Process memopad -ErrorAction SilentlyContinue | Stop-Process -Force
    # 設定を元に戻す
    if ($hadSettings) { New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null; Copy-Item $backupPath $settingsPath -Force }
    elseif (Test-Path $settingsPath) { Remove-Item $settingsPath -Force }
}
