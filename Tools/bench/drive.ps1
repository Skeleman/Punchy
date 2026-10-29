param([string]$cmd, [string]$a = "", [string]$b = "", [int]$ms = 0)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref System.Drawing.Point p);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint c, uint t);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
}
'@ -ReferencedAssemblies System.Drawing
$p = Get-Process Punchy | Select-Object -First 1; $h = $p.MainWindowHandle
[W]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 200
$r = New-Object W+R; [W]::GetClientRect($h, [ref]$r) | Out-Null
$o = New-Object System.Drawing.Point 0,0; [W]::ClientToScreen($h, [ref]$o) | Out-Null
function Key([byte]$vk, [int]$hold) { $sc=[byte][W]::MapVirtualKey($vk,0); [W]::keybd_event($vk,$sc,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds ([Math]::Max($hold,60)); [W]::keybd_event($vk,$sc,2,[UIntPtr]::Zero) }
switch ($cmd) {
  "shot"  { $bmp = New-Object System.Drawing.Bitmap $r.Rt, $r.B; $g=[System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($o.X,$o.Y,0,0,$bmp.Size); $bmp.Save($a); "saved $a ($($r.Rt)x$($r.B))" }
  "wshot" { $bmp = New-Object System.Drawing.Bitmap $r.Rt, $r.B; $g=[System.Drawing.Graphics]::FromImage($bmp); $dc=$g.GetHdc(); [W]::PrintWindow($h,$dc,3) | Out-Null; $g.ReleaseHdc($dc); $bmp.Save($a); "saved $a" }
  "click" { [W]::SetCursorPos($o.X+[int]$a, $o.Y+[int]$b); Start-Sleep -Milliseconds 100; [W]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [W]::mouse_event(4,0,0,0,[UIntPtr]::Zero); "clicked $a,$b" }
  "look"  { for ($i=0; $i -lt [int]$b; $i++) { [W]::mouse_event(1,[int]$a,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 15 }; "look $a x $b" }
  "key"   { Key ([byte][int]$a) $ms; "key $a held $ms ms" }
  "keys"  { foreach ($k in $a.Split(',')) { [W]::keybd_event([byte][int]$k,[byte][W]::MapVirtualKey([int]$k,0),0,[UIntPtr]::Zero) }; Start-Sleep -Milliseconds $ms; foreach ($k in $a.Split(',')) { [W]::keybd_event([byte][int]$k,[byte][W]::MapVirtualKey([int]$k,0),2,[UIntPtr]::Zero) }; "keys $a held $ms ms" }
}
