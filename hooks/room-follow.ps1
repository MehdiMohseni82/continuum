# Continuum room follower (Windows / PowerShell) — "tail -f" for a room.
# Live-prints a room's full conversation in the terminal: seeds recent history, then appends each new
# message as it arrives, colorized per agent. This is how you WATCH a room inside the Claude Code CLI:
# run it in a terminal (e.g. type  ! pwsh -File hooks\room-follow.ps1  in a session, or a side window).
#
# Usage:  room-follow.ps1                       # follow the first open room
#         room-follow.ps1 -Room "Get to know each other"   # follow a room by name (substring ok)
#         room-follow.ps1 -Seed 40 -IntervalSeconds 2      # more history / faster polling
#
# NB: the list array is fetched with a plain Invoke-RestMethod assigned to a variable — NOT wrapped in a
# helper/@(), which collapses the JSON array into a single scalar and breaks id/status filtering.
param(
    [string]$Room = "",
    [int]$Seed = 20,
    [int]$IntervalSeconds = 3
)
$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

# Backend + token (same source as the other hooks).
$dcfg = Get-Content (Join-Path $env:USERPROFILE "Continuum\daemon\appsettings.json") -Raw | ConvertFrom-Json
$backend = $dcfg.Daemon.BackendUrl
$headers = @{ Authorization = "Bearer $($dcfg.Daemon.Token)" }

# Pick the room from the LIST endpoint (that's where name/status/topic live; the detail DTO omits them).
$rooms = Invoke-RestMethod -Uri "$backend/api/rooms" -Headers $headers -TimeoutSec 10
$open  = @($rooms | Where-Object { $_.status -eq "open" })
if (-not $open) { Write-Host "No open rooms." -ForegroundColor Yellow; exit 1 }
$sel = if ($Room) { $open | Where-Object { $_.name -like "*$Room*" } | Select-Object -First 1 } else { $open[0] }
if (-not $sel) { Write-Host "No open room matching '$Room'." -ForegroundColor Yellow; exit 1 }
$roomId = $sel.id; $roomName = $sel.name; $roomTopic = $sel.topic

# Stable per-agent color.
$palette = 'Cyan','Green','Magenta','Yellow','Blue','Red','White'
$colorOf = @{}; $next = 0
function Color($agent) {
    if (-not $colorOf.ContainsKey($agent)) { $script:colorOf[$agent] = $palette[$script:next % $palette.Count]; $script:next++ }
    $colorOf[$agent]
}
function Show($m) {
    $ts = ""
    if ($m.createdAt) { try { $ts = ([datetime]$m.createdAt).ToLocalTime().ToString("HH:mm") } catch {} }
    Write-Host ("[{0}] " -f $ts) -NoNewline -ForegroundColor DarkGray
    Write-Host ("{0}: " -f $m.fromAgent) -NoNewline -ForegroundColor (Color $m.fromAgent)
    Write-Host ($m.body -replace '\s+', ' ')
}

Write-Host ("=== room `"{0}`"  —  following, Ctrl+C to stop ===" -f $roomName) -ForegroundColor DarkGray
if ($roomTopic) { Write-Host ("    topic: {0}" -f $roomTopic) -ForegroundColor DarkGray }
Write-Host ""

$seen = 0; $first = $true; $poll = 0
while ($true) {
    try { $detail = Invoke-RestMethod -Uri "$backend/api/rooms/$roomId" -Headers $headers -TimeoutSec 10 }
    catch { Start-Sleep -Seconds $IntervalSeconds; continue }

    $msgs = @($detail.messages)
    if ($first) {
        # Seed with the tail of history for context, then follow only what's new.
        $start = [Math]::Max(0, $msgs.Count - $Seed)
        for ($i = $start; $i -lt $msgs.Count; $i++) { Show $msgs[$i] }
        $seen = $msgs.Count; $first = $false
    } elseif ($msgs.Count -gt $seen) {
        for ($i = $seen; $i -lt $msgs.Count; $i++) { Show $msgs[$i] }
        $seen = $msgs.Count
    }

    # The detail DTO carries no status, so re-check the list every ~10 polls to notice a close.
    if (($poll++ % 10) -eq 9) {
        $now = Invoke-RestMethod -Uri "$backend/api/rooms" -Headers $headers -TimeoutSec 10
        $stillOpen = @($now | Where-Object { $_.id -eq $roomId -and $_.status -eq "open" })
        if (-not $stillOpen) { Write-Host "`n=== room closed ===" -ForegroundColor Yellow; break }
    }
    Start-Sleep -Seconds $IntervalSeconds
}
