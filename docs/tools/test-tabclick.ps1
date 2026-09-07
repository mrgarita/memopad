# タブをマウスでクリックして切り替わるかを確かめる（起動中の他の memopad には触らず、自分で起動した PID だけ扱う）
param([string]$Exe = 'D:\claude\memopad\src\memopad\bin\Debug\net9.0-windows\memopad.exe')
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
$h=[IntPtr]::Zero; for ($i=0;$i -lt 40;$i++){ Start-Sleep -Milliseconds 300; $p.Refresh(); if ($p.MainWindowHandle -ne 0){ $h=$p.MainWindowHandle; break } }
[TC]::MoveWindow($h, 80, 60, 1000, 720, $true) | Out-Null; [TC]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 1200
[uint32]$fp=0; [TC]::GetWindowThreadProcessId([TC]::GetForegroundWindow(), [ref]$fp) | Out-Null
if ($fp -ne $p.Id) { Write-Host '前面化できず中断'; Stop-Process -Id $p.Id -Force; exit 1 }
function Title { $sb = New-Object System.Text.StringBuilder 256; [TC]::GetWindowTextW($h, $sb, 256) | Out-Null; $sb.ToString() }
# タブを 2 つにして（Ctrl+N）、2 つ目に文字を入れる
[System.Windows.Forms.SendKeys]::SendWait('^n'); Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait('abc'); Start-Sleep -Milliseconds 400
"Ctrl+N 後のタイトル: $(Title)"
# UI Automation でタブ一覧の項目の位置を取り、1 つ目をマウスでクリック
$AE=[System.Windows.Automation.AutomationElement]; $TS=[System.Windows.Automation.TreeScope]
$root = $AE::FromHandle($h)
$list = $root.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'TabList')))
$items = $list.FindAll($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
"タブ数: $($items.Count)"
$r = $items.Item(0).Current.BoundingRectangle
$x = [int]($r.Left + 40); $y = [int]($r.Top + $r.Height / 2)
[TC]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 200
[TC]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [TC]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 700
"1 つ目のタブをクリック後のタイトル: $(Title)"
# 2 つ目のタブをクリック
$r = $items.Item(1).Current.BoundingRectangle
$x = [int]($r.Left + 40); $y = [int]($r.Top + $r.Height / 2)
[TC]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 200
[TC]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [TC]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 700
"2 つ目のタブをクリック後のタイトル: $(Title)"
# クリック後にキー入力がエディタへ届くか
[System.Windows.Forms.SendKeys]::SendWait('xyz'); Start-Sleep -Milliseconds 400
$doc = $root.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Document)))
"本文: $($doc.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern).DocumentRange.GetText(-1))"
Stop-Process -Id $p.Id -Force
