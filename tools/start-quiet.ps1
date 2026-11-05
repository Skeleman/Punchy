# Start a program (Unity editor or a Punchy build) without it taking keyboard focus,
# then guard focus for the life of the process: if one of its windows grabs the
# foreground on its own (load finished, play mode entered), hand focus back to the
# window Zach was using. A switch Zach makes himself (mouse over the window, or Alt
# held for Alt-Tab) is left alone.
#
# Usage (from Bash):
#   powershell -ExecutionPolicy Bypass -File tools/start-quiet.ps1 -Exe "<path to exe>" -ArgLine "<arguments>"
# The script prints the child PID, then keeps running until the child exits.
# Run it in the background (Bash run_in_background) so the guard stays alive.
# -NoGuard: only start unfocused, don't guard.
param(
  [Parameter(Mandatory = $true)][string]$Exe,
  [string]$ArgLine = "",
  [switch]$NoGuard,
  [int]$PollMs = 150
)

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Quiet {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct STARTUPINFO {
    public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
    public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
    public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
  }
  [StructLayout(LayoutKind.Sequential)]
  public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit,
    uint flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
  [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

  const short SW_SHOWMINNOACTIVE = 7;
  public static int Start(string exe, string args) {
    var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(si);
    si.dwFlags = 1; si.wShowWindow = SW_SHOWMINNOACTIVE;   // STARTF_USESHOWWINDOW
    var cmd = new StringBuilder("\"" + exe + "\" " + args);
    PROCESS_INFORMATION pi;
    if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero,
                       System.IO.Path.GetDirectoryName(exe), ref si, out pi))
      throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    return pi.dwProcessId;
  }
  public static uint PidOf(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
  public static uint PidUnderCursor() {
    POINT p; GetCursorPos(out p);
    var w = GetAncestor(WindowFromPoint(p), 2);   // GA_ROOT
    return w == IntPtr.Zero ? 0 : PidOf(w);
  }
  public static bool AltDown() { return (GetAsyncKeyState(0x12) & 0x8000) != 0; }
  // Windows only lets the foreground process move focus; a tapped Alt lifts that lock.
  public static void GiveBack(IntPtr h) {
    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
    SetForegroundWindow(h);
  }
}
"@

$exePath = (Resolve-Path $Exe).Path
$prev = [Quiet]::GetForegroundWindow()
$childPid = [Quiet]::Start($exePath, $ArgLine)
Write-Output "started pid $childPid"
if ($NoGuard) { exit 0 }

$name = [IO.Path]::GetFileNameWithoutExtension($exePath)
function Get-Family {
  # The program plus any processes it spawned (Unity uses helper processes for some windows).
  $ids = @($childPid)
  $kids = Get-CimInstance Win32_Process -Filter "ParentProcessId=$childPid" -ErrorAction SilentlyContinue
  if ($kids) { $ids += $kids.ProcessId }
  return $ids
}
$family = Get-Family; $tick = 0
while (Get-Process -Id $childPid -ErrorAction SilentlyContinue) {
  Start-Sleep -Milliseconds $PollMs
  if ((++$tick % 40) -eq 0) { $family = Get-Family }
  $fg = [Quiet]::GetForegroundWindow()
  if ($fg -eq [IntPtr]::Zero) { continue }
  $fgPid = [int][Quiet]::PidOf($fg)
  if ($family -notcontains $fgPid) { $prev = $fg; continue }   # Zach's current window
  # A family window has focus. Leave it if Zach chose it (mouse over it, or Alt-Tab).
  $underCursor = [int][Quiet]::PidUnderCursor()
  if (($family -contains $underCursor) -or [Quiet]::AltDown()) { continue }
  if ($prev -ne [IntPtr]::Zero -and [Quiet]::IsWindow($prev)) {
    [Quiet]::GiveBack($prev)
    Write-Output "gave focus back from $name"
  }
}
Write-Output "pid $childPid exited"
