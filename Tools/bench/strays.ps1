# Kills what a dead bench run left behind in one project tree, and nothing that belongs to another tree or to the user:
# - batch-mode Unity.exe whose command line holds the tree (never an interactive editor);
# - bench Punchy.exe (benchroute, benchclient) run from the tree or logging to it (never a game the user started);
# - bee_backend.exe whose parent is gone or is being killed here (a live editor's or another tree's build keeps its own).
param([Parameter(Mandatory = $true)][string]$Tree)

# The tree must end at a path boundary, or Projects/Punchy would also match Projects/Punchy-wt.
$pattern = [regex]::Escape($Tree.Replace('\', '/').TrimEnd('/')) + '([/"\s]|$)'
function InTree($p) { ("$($p.ExecutablePath) $($p.CommandLine)").Replace('\', '/') -match $pattern }

$all = @(Get-CimInstance Win32_Process)
$kill = @($all | Where-Object {
    (($_.Name -eq 'Unity.exe' -and $_.CommandLine -match 'batchmode') -or
     ($_.Name -eq 'Punchy.exe' -and $_.CommandLine -match 'benchroute|benchclient')) -and (InTree $_)
})
$alive = @{}; foreach ($p in $all) { $alive[[int]$p.ProcessId] = $true }
$killed = @{}; foreach ($p in $kill) { $killed[[int]$p.ProcessId] = $true }
$kill += @($all | Where-Object {
    $_.Name -eq 'bee_backend.exe' -and ((-not $alive[[int]$_.ParentProcessId]) -or $killed[[int]$_.ParentProcessId])
})
foreach ($p in $kill) {
    "killing stray $($p.Name) $($p.ProcessId)"
    Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
}
