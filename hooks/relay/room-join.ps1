# Continuum room join — invoked by the /continuum-joinroom slash command.
# Joins THIS interactive session to a room as <AgentName>: registers membership, fetches the room's
# system prompt, and prints the standing framing + a session-scoped bind marker into the conversation.
# The Stop-hook relay (room-relay.ps1) reads that marker from the transcript and drives the back-and-forth.
#
# Everything printed here becomes part of the session (it's the slash command's output), so this is the
# ONE place the room's "system prompt" is delivered — once, on join. Per-turn messages stay raw.
param(
    [Parameter(Mandatory)][string]$RoomId,
    [Parameter(Mandatory)][string]$AgentName
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

function Backend {
    $cfgPath = Join-Path $env:USERPROFILE 'Continuum\daemon\appsettings.json'
    $cfg = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json
    return @{ Url = $cfg.Daemon.BackendUrl; Headers = @{ Authorization = "Bearer $($cfg.Daemon.Token)" } }
}

try {
    $b = Backend
} catch {
    Write-Output "Could not read the Continuum backend config ($($_.Exception.Message)). Is the daemon installed?"
    return
}

# Look the room up on the list endpoint (name/topic/status/systemPrompt live there).
try {
    $list = Invoke-RestMethod -Uri "$($b.Url)/api/rooms" -Headers $b.Headers -TimeoutSec 15
    $room = $list | Where-Object { $_.id -eq $RoomId } | Select-Object -First 1
} catch {
    Write-Output "Failed to reach the room API: $($_.Exception.Message)"
    return
}
if (-not $room) { Write-Output "No room with id '$RoomId' (is it visible to your token?)."; return }
if ($room.status -ne 'open') { Write-Output "Room '$($room.name)' is $($room.status) — cannot join."; return }

# Register membership (best-effort; the relay works even if this 403s under a non-admin token).
try {
    Invoke-RestMethod -Uri "$($b.Url)/api/rooms/$RoomId/members" -Method Post -Headers $b.Headers `
        -ContentType 'application/json' -Body (@{ agent = $AgentName } | ConvertTo-Json) -TimeoutSec 15 | Out-Null
} catch { }

$name    = $room.name
$channel = $room.channelName
$sys     = $room.systemPrompt
$topic   = $room.topic

# --- framing emitted into the session ---
Write-Output "You have joined Continuum room `"$name`" as agent `"$AgentName`"."
Write-Output ""
if ($sys) {
    Write-Output "===== ROOM SYSTEM PROMPT — your standing instructions for this room ====="
    Write-Output $sys
    Write-Output "========================================================================"
    Write-Output ""
}
if ($topic) { Write-Output "Goal / topic: $topic"; Write-Output "" }
Write-Output "HOW THE ROOM WORKS: after each reply you write, your message is sent to the room automatically, and the other participant's next message is delivered back to you as your next prompt — a live back-and-forth, no copy-paste. Keep every turn short and ACTION-oriented: change code, run the test, report the result — do not just discuss. When the goal is met and verified, begin a message with [DONE] to end the room."
Write-Output ""
Write-Output "If you are the initiator: state your concrete goal and take your first action now. If you joined to respond: reply with exactly the word  ready  and wait."
Write-Output ""
Write-Output "(system marker for the relay — ignore this line)"
Write-Output "<<CONTINUUM-ROOM room=$RoomId agent=$AgentName channel=$channel>>"
