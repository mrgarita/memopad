<#
.SYNOPSIS
  Windows メモ帳（Store 版 Notepad）のスクリーンショットを docs/site/img/step1/ へ一括生成する。

.DESCRIPTION
  step1（メモ帳の機能洗い出し）の備忘録 HTML に載せる画面画像を、UI Automation と
  SendKeys で操作しながら自動撮影する。撮影対象はメイン画面・各メニュー・検索／置換バー・
  行に移動・右クリックメニュー・設定ページ・マークダウン タブ・名前を付けて保存ダイアログ。

  前提：
  - Windows 11 の Store 版メモ帳（Microsoft.WindowsNotepad）が既定のメモ帳であること
  - 実行中はマウス／キーボードに触らない（フォアグラウンド操作に依存するため）
  - 起動中のメモ帳は一度終了する（メモ帳は未保存タブをセッションとして自動保存するので安全）

.PARAMETER OutDir
  出力先フォルダ。既定は docs/site/img/step1（このスクリプトの位置から解決）。

.EXAMPLE
  .\docs\tools\capture-notepad.ps1
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\site\img\step1')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class NativeWin {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hgt, bool repaint);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[NativeWin]::SetProcessDPIAware() | Out-Null

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$AE = [System.Windows.Automation.AutomationElement]
$CT = [System.Windows.Automation.ControlType]
$TS = [System.Windows.Automation.TreeScope]
$SK = [System.Windows.Forms.SendKeys]
$desk = $AE::RootElement

# --- 撮影用のサンプルファイル（タイトルバーとタブにファイル名を出すため）
$sample = Join-Path $env:TEMP 'memopad_sample.txt'
@(
  'memopad プロジェクト step1：メモ帳の機能洗い出し',
  '',
  'このファイルはスクリーンショット撮影用のサンプルです。',
  '背景色と文字色を変えられるメモ帳を作るために、まず本家メモ帳の機能を調べます。',
  '',
  '1. ファイル操作（新規・開く・保存・印刷）',
  '2. 編集操作（元に戻す・検索・置換・行へ移動）',
  '3. 表示（ズーム・ステータスバー・右端で折り返す）',
  '4. 設定（テーマ・フォント・起動時の動作・スペルチェック）'
) | Set-Content -Path $sample -Encoding UTF8

# --- 起動中のメモ帳を終了してから、サンプルを開いて起動する
Get-Process Notepad -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
Start-Process notepad.exe -ArgumentList "`"$sample`""
$np = $null
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 500
    $np = Get-Process Notepad -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($np) { break }
}
if (-not $np) { throw 'メモ帳のウィンドウが見つかりません。' }
$hwnd = $np.MainWindowHandle
[NativeWin]::MoveWindow($hwnd, 80, 60, 1000, 720, $true) | Out-Null
Start-Sleep -Seconds 2
$root = $AE::FromHandle($hwnd)

# --- ヘルパー
function PC($prop, $val) { New-Object System.Windows.Automation.PropertyCondition($prop, $val) }
function AndC { param([System.Windows.Automation.Condition[]]$c) New-Object System.Windows.Automation.AndCondition($c) }
function ProcFirst($cond) { $desk.FindFirst($TS::Descendants, (AndC @((PC $AE::ProcessIdProperty $np.Id), $cond))) }
function ProcAll($cond) { @($desk.FindAll($TS::Descendants, (AndC @((PC $AE::ProcessIdProperty $np.Id), $cond)))) }
# 指定ウィンドウを前面化し、実際に前面になったことを確認する。
# 確認できないままキーを送ると VS Code など別アプリに入力が飛ぶので、失敗時は中断する。
function FgIsNotepad {
    [uint32]$fgPid = 0
    [NativeWin]::GetWindowThreadProcessId([NativeWin]::GetForegroundWindow(), [ref]$fgPid) | Out-Null
    return ($fgPid -eq $np.Id)
}
function Fg([IntPtr]$h = $hwnd) {
    if (FgIsNotepad) { return }
    for ($i = 0; $i -lt 10; $i++) {
        $fgThread = [NativeWin]::GetWindowThreadProcessId([NativeWin]::GetForegroundWindow(), [IntPtr]::Zero)
        $me = [NativeWin]::GetCurrentThreadId()
        [NativeWin]::AttachThreadInput($me, $fgThread, $true) | Out-Null
        [NativeWin]::ShowWindow($h, 9) | Out-Null
        [NativeWin]::SetForegroundWindow($h) | Out-Null
        [NativeWin]::AttachThreadInput($me, $fgThread, $false) | Out-Null
        Start-Sleep -Milliseconds 500
        if (FgIsNotepad) { return }
    }
    throw "ウィンドウを前面化できませんでした（hwnd=$h）。キー入力の誤送信を防ぐため中断します。"
}
# 前面がメモ帳（メイン or 指定ハンドル）であることを確認してからキーを送る
function Keys($k, $wait = 800, [IntPtr]$h = $hwnd) {
    if (-not (FgIsNotepad)) { Fg $h }
    $SK::SendWait($k); Start-Sleep -Milliseconds $wait
}
function Invoke-El($el) {
    if (-not $el) { Write-Warning '対象の要素が見つかりません'; return }
    try { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    catch { $el.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand() }
    Start-Sleep -Milliseconds 800
}
# UI の描画が間に合わないと FindFirst が空振りする。見つかるまで少し待って探し直す
function ProcWait($cond, [int]$timeoutMs = 4000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $el = ProcFirst $cond
        if ($el) { return $el }
        Start-Sleep -Milliseconds 250
    }
    return $null
}
function Btn($name) { ProcWait (AndC @((PC $AE::NameProperty $name), (PC $AE::ControlTypeProperty $CT::Button))) }
# extraBottom：ウィンドウの下にはみ出すドロップダウンなどを含めたいときに、撮影範囲を下へ広げる量（px）
# keepRect  ：広げた範囲のうち残す矩形（スクリーン座標）。それ以外は背景色で塗りつぶし、背後の別アプリを写さない
function Shot($name, [IntPtr]$h = $hwnd, [int]$extraBottom = 0, $keepRect = $null) {
    # 別アプリが前面に出ていると、その画面が写り込む。撮影前に必ずメモ帳を前面へ戻す
    if (-not (FgIsNotepad)) { Fg $h }
    Start-Sleep -Milliseconds 300
    $r = New-Object NativeWin+RECT
    [NativeWin]::GetWindowRect($h, [ref]$r) | Out-Null
    if ($keepRect) { $extraBottom = [Math]::Max($extraBottom, [int]($keepRect.Bottom - $r.Bottom) + 8) }
    $w = $r.Right - $r.Left; $base = $r.Bottom - $r.Top; $hh = $base + $extraBottom
    $bmp = New-Object System.Drawing.Bitmap($w, $hh)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    if ($extraBottom -gt 0 -and $keepRect) {
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(243, 243, 243))
        $kx1 = [int]($keepRect.Left - $r.Left) - 2; $kx2 = [int]($keepRect.Right - $r.Left) + 2
        $ky2 = [int]($keepRect.Bottom - $r.Top) + 2
        $g.FillRectangle($brush, 0, $base, [Math]::Max(0, $kx1), $extraBottom)
        $g.FillRectangle($brush, $kx2, $base, [Math]::Max(0, $w - $kx2), $extraBottom)
        if ($ky2 -lt $hh) { $g.FillRectangle($brush, 0, $ky2, $w, $hh - $ky2) }
        $brush.Dispose()
    }
    $g.Dispose()
    $path = Join-Path $OutDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  撮影: $name.png ($w x $hh)"
}

# メモ帳は前回セッションのタブを復元するため、サンプル以外のタブが混ざるとタイトルや
# 保存ダイアログの既定ファイル名がずれる。サンプル タブだけ残す。
function SampleTabs { ProcAll (PC $AE::ControlTypeProperty $CT::TabItem) }
function CloseExtraTabs {
    for ($i = 0; $i -lt 8; $i++) {
        $extra = SampleTabs | Where-Object { $_.Current.Name -notlike '*memopad_sample*' } | Select-Object -First 1
        if (-not $extra) { return }
        try { $extra.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { }
        Start-Sleep -Milliseconds 400
        Keys '^w' 1000
        $dont = Btn '保存しない'
        if ($dont) { Invoke-El $dont }
    }
    Write-Warning 'サンプル以外のタブを閉じきれませんでした'
}
function SelectSampleTab {
    $t = SampleTabs | Where-Object { $_.Current.Name -like '*memopad_sample*' } | Select-Object -First 1
    if ($t) { try { $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 500 } catch { } }
    else { Write-Warning 'サンプル タブが見つかりません' }
}

Write-Host "出力先: $OutDir"
Fg; CloseExtraTabs

# 01 メイン画面
Fg; Shot '01_main'

# 02-05 メニュー
Fg; Keys '%f'; Shot '02_menu_file'; Keys '{ESC}' 400
Fg; Keys '%e'; Shot '03_menu_edit'; Keys '{ESC}' 400
Fg; Keys '%v'; Shot '04_menu_view'; Keys 'z'; Shot '05_menu_view_zoom'; Keys '{ESC}{ESC}{ESC}' 400  # 3 回目の ESC でアクセスキー表示も消す

# 06-07 検索バーとそのオプション
Fg; Keys '^f' 1000; Shot '06_find'
$opt = ProcWait (AndC @((PC $AE::NameProperty 'その他のオプション'), (PC $AE::AutomationIdProperty 'SettingsButton')))
Invoke-El $opt; Shot '07_find_options'; Keys '{ESC}' 400
# 08 置換バー
Fg; Keys '^h' 1000; Shot '08_replace'
# AutomationId=CloseButton はタブの閉じるボタンにも付いているので、名前も条件に入れる
Invoke-El (ProcWait (AndC @((PC $AE::NameProperty '検索と置換の終了'), (PC $AE::AutomationIdProperty 'CloseButton'))))

# 09 行に移動
Fg; Keys '^g' 1000; Shot '09_goto'
Invoke-El (Btn 'キャンセル')

# 10 右クリックメニュー
Fg
$doc = $root.FindFirst($TS::Descendants, (PC $AE::ControlTypeProperty $CT::Document))
$doc.SetFocus(); Start-Sleep -Milliseconds 300
Keys '+{F10}' 1000; Shot '10_context_menu'; Keys '{ESC}' 400

# 11-13 設定ページ（テーマ展開 → PageDown でフォント欄）
Fg; Invoke-El (Btn '設定'); Start-Sleep -Milliseconds 800; Shot '11_settings'
Invoke-El (Btn 'アプリのテーマ'); Shot '12_settings_theme'
Invoke-El (Btn 'フォント'); Keys '{PGDN}' 800; Shot '13_settings_font'
Invoke-El (ProcWait (AndC @((PC $AE::NameProperty '戻る'), (PC $AE::ControlTypeProperty $CT::Button))))

# 14-16 マークダウン タブ：新規タブは「マークダウン構文」表示で開くので、
# 表示 > マークダウン で書式ビューへ切り替えると書式ツールバーが出る
Fg; Keys '%f' 600; Keys 'm' 1500
# 長文を SendKeys で打つと、途中で前面が奪われたときに残りが別アプリへ流れてしまう。
# クリップボード経由なら実際のキー入力は Ctrl+V の一瞬だけで済む。
$md = "# Heading 1`r`nPlain paragraph with **bold** and *italic*.`r`n- list item 1`r`nlist item 2`r`n"
Set-Clipboard -Value $md
Keys '^v' 1000
Shot '14_markdown_source'
Keys '%v' 600; Keys 'v' 700; Keys 's' 1500; Shot '15_markdown_formatted'   # 表示 > マークダウン > 書式付き
Invoke-El (Btn '見出し'); Shot '16_markdown_heading'; Keys '{ESC}' 400
# 作ったマークダウン タブは保存せずに閉じる
Fg; Keys '^w' 1000
$dont = Btn '保存しない'
if ($dont) { Invoke-El $dont }

# 17 名前を付けて保存ダイアログ（エンコードの選択肢）
Fg; CloseExtraTabs; SelectSampleTab
Keys '^+s' 2500
# ダイアログはメイン ウィンドウ配下に現れることがあるので、プロセス全体から名前で探す
$dlg = ProcWait (AndC @((PC $AE::ControlTypeProperty $CT::Window), (PC $AE::NameProperty '名前を付けて保存'))) 6000
if ($dlg) {
    # エンコードのコンボボックスは UI Automation から ComboBox として見えないため、
    # ラベル「エンコード:」の右隣をマウスでクリックして一覧を開く
    $label = ProcWait (PC $AE::NameProperty 'エンコード:')
    if ($label) {
        $lr = $label.Current.BoundingRectangle
        $cx = [int]($lr.Right + 110); $cy = [int]($lr.Top + $lr.Height / 2)
        [NativeWin]::SetCursorPos($cx, $cy) | Out-Null; Start-Sleep -Milliseconds 200
        [NativeWin]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [NativeWin]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 900
    } else { Write-Warning '「エンコード:」ラベルが見つかりません' }
    $dh = [IntPtr]$dlg.Current.NativeWindowHandle
    # エンコード一覧はダイアログの下にはみ出すので、一覧の矩形だけ残して範囲を広げる
    # 一覧の矩形は、取得できれば実測値を使い、取れなければコンボボックスの位置から組み立てる
    $keep = $null
    $ansi = ProcWait (AndC @((PC $AE::NameProperty 'ANSI'), (PC $AE::ControlTypeProperty $CT::ListItem))) 2000
    if ($ansi) { $keep = ([System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($ansi)).Current.BoundingRectangle }
    if ((-not $keep) -and $label) {
        $lr = $label.Current.BoundingRectangle
        $keep = New-Object System.Windows.Rect(($lr.Right + 4), $lr.Bottom, 230, 138)
    }
    Shot '17_saveas_encoding' $dh 0 $keep
    Keys '{ESC}' 500 $dh
    if (ProcFirst (AndC @((PC $AE::ControlTypeProperty $CT::Window), (PC $AE::NameProperty '名前を付けて保存')))) { Keys '{ESC}' 500 $dh }
} else {
    Write-Warning '名前を付けて保存ダイアログが見つかりませんでした'
}

# 後片付け：ウィンドウを閉じる（サンプルは未変更なので確認は出ない）
Fg; Keys '^+w' 800
Write-Host '完了'
