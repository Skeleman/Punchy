# b/fix-stall-hang: full memory dump of one process by exact PID, through dbghelp's MiniDumpWriteDump (ships with
# Windows; no install, no elevation for a process this user started). Output stays local (results/, never committed).
# powershell -NoProfile -File dump.ps1 -ProcessId <pid> -Out <file.dmp>
param([Parameter(Mandatory)][int]$ProcessId, [Parameter(Mandatory)][string]$Out)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Dumper {
    [DllImport("dbghelp.dll", SetLastError = true)]
    static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, IntPtr hFile, uint dumpType,
        IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
    public static bool Write(System.Diagnostics.Process p, string path) {
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create)) {
            // MiniDumpWithFullMemory | MiniDumpWithHandleData | MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules
            return MiniDumpWriteDump(p.Handle, (uint)p.Id, fs.SafeFileHandle.DangerousGetHandle(), 0x2 | 0x4 | 0x1000 | 0x20,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
"@
$p = Get-Process -Id $ProcessId -ErrorAction Stop
if ($p.ProcessName -ne 'Punchy') { throw "PID $ProcessId is $($p.ProcessName), not Punchy: refusing" }
$ok = [Dumper]::Write($p, $Out)
"dump $ProcessId -> $Out : $ok, $((Get-Item $Out).Length) bytes"
