# タブをマウスでクリックして切り替わるかを確かめる（起動中の他の MemoPad には触らず、自分で起動した PID だけ扱う）。
#
# v0.8.0 でタイトル行を自前描画に変えたため、タブは UI Automation の PageTab（TitleBar の
# アクセシブル オブジェクト）として見える。位置を決め打ちせず、そこから矩形を取ってクリックする。
param([string]$Exe = 'D:\claude\memopad\src\memopad\bin\Debug\net9.0-windows\memopad.exe')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class TC {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hgt, bool r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder sb, int n);
}
"@
[TC]::SetProcessDPIAware() | Out-Null
$p = Start-Process $Exe -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 300; $p.Refresh(); if ($p.MainWindowHandle -ne 0) { $h = $p.MainWindowHandle; break } }
if ($h -eq [IntPtr]::Zero) { Write-Host 'ウィンドウが出ません'; exit 1 }
[TC]::MoveWindow($h, 80, 60, 1000, 720, $true) | Out-Null
[TC]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 1200
[uint32]$fp = 0
[TC]::GetWindowThreadProcessId([TC]::GetForegroundWindow(), [ref]$fp) | Out-Null
if ($fp -ne $p.Id) { Write-Host '前面化できず中断'; Stop-Process -Id $p.Id -Force; exit 1 }

function Title { $sb = New-Object System.Text.StringBuilder 256; [TC]::GetWindowTextW($h, $sb, 256) | Out-Null; $sb.ToString() }
function ClickRect($r) {
    $x = [int]($r.Left + $r.Width / 3); $y = [int]($r.Top + $r.Height / 2)
    [TC]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 200
    [TC]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80
    [TC]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 700
}

# タブを 2 つにして（Ctrl+N）、2 つ目に文字を入れる
[System.Windows.Forms.SendKeys]::SendWait('^n'); Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('abc'); Start-Sleep -Milliseconds 400
"Ctrl+N 後のタイトル: $(Title)"

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$root = $AE::FromHandle($h)
$tabs = @($root.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::TabItem))))
"タブ数: $($tabs.Count)"
if ($tabs.Count -lt 2) { Write-Host 'タブが 2 つ見つかりません'; Stop-Process -Id $p.Id -Force; exit 1 }

ClickRect $tabs[0].Current.BoundingRectangle
"1 つ目のタブをクリック後のタイトル: $(Title)"
ClickRect $tabs[1].Current.BoundingRectangle
"2 つ目のタブをクリック後のタイトル: $(Title)"

# クリック後にキー入力がエディタへ届くか
[System.Windows.Forms.SendKeys]::SendWait('xyz'); Start-Sleep -Milliseconds 400
$doc = $root.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Document)))
if ($doc) { "本文: $($doc.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern).DocumentRange.GetText(-1))" }
else { Write-Warning '本文（Document）が見つかりません' }
Stop-Process -Id $p.Id -Force
