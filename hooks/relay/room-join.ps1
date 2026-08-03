# Continuum room join — invoked by the /continuum-joinroom slash command.
# Joins THIS interactive session to a room as <AgentName>: registers membership, fetches the room's
# system prompt, and prints the standing framing + a session-scoped bind marker into the conversation.
# The Stop-hook relay (room-relay.ps1) reads that marker from the transcript and drives the back-and-forth.
#
# Everything printed here becomes part of the session (it's the slash command's output), so this is the
# ONE place the room's "system prompt" is delivered — once, on join. Per-turn messages stay raw.
param(
    [string]$RoomId,
    [string]$AgentName
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

# The /continuum-joinroom slash command passes "<ROOM_ID> <AGENT_NAME>" via $ARGUMENTS. If both values
# land in $RoomId as one string, split them — so both the slash command and direct calls work.
if ([string]::IsNullOrWhiteSpace($AgentName) -and $RoomId -match '\s') {
    $__p = $RoomId.Trim() -split '\s+', 2
    $RoomId = $__p[0]; $AgentName = $__p[1]
}
if ([string]::IsNullOrWhiteSpace($RoomId) -or [string]::IsNullOrWhiteSpace($AgentName)) {
    Write-Output "Usage: /continuum-joinroom <ROOM_ID> <AGENT_NAME>"
    return
}

function Backend {
    $cfgPath = Join-Path $env:USERPROFILE 'Continuum\daemon\appsettings.json'
    $cfg = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json
    return @{ Url = $cfg.Daemon.BackendUrl; Token = [string]$cfg.Daemon.Token }
}

# HTTP via curl.exe with -4: this machine's IPv6 route to Cloudflare is dead and .NET's Invoke-RestMethod
# hangs on the AAAA address, so force IPv4 with the native Windows curl.
function Api([string]$Method, [string]$Url, [string]$Token, [string]$Body) {
    $a = @('-4', '-s', '--max-time', '25', '-X', $Method, $Url, '-H', "Authorization: Bearer $Token")
    $tmp = $null
    if ($Body) {
        $tmp = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllText($tmp, $Body, (New-Object System.Text.UTF8Encoding($false)))
        $a += @('-H', 'Content-Type: application/json', '--data-binary', "@$tmp")
    }
    try { $out = & curl.exe @a; if ($LASTEXITCODE -ne 0) { throw "curl exit $LASTEXITCODE" } }
    finally { if ($tmp) { Remove-Item -LiteralPath $tmp -ErrorAction SilentlyContinue } }
    if ([string]::IsNullOrWhiteSpace($out)) { return $null }
    return ($out | ConvertFrom-Json)
}

try {
    $b = Backend
} catch {
    Write-Output "Could not read the Continuum backend config ($($_.Exception.Message)). Is the daemon installed?"
    return
}

# Look the room up on the list endpoint (name/topic/status/systemPrompt live there).
try {
    $list = @(Api 'GET' "$($b.Url)/api/rooms" $b.Token)
    $room = $list | Where-Object { $_.id -eq $RoomId } | Select-Object -First 1
} catch {
    Write-Output "Failed to reach the room API: $($_.Exception.Message)"
    return
}
if (-not $room) { Write-Output "No room with id '$RoomId' (is it visible to your token?)."; return }
if ($room.status -ne 'open') { Write-Output "Room '$($room.name)' is $($room.status) — cannot join."; return }

# Register membership (best-effort; the relay works even if this 403s under a non-admin token).
try {
    Api 'POST' "$($b.Url)/api/rooms/$RoomId/members" $b.Token (@{ agent = $AgentName } | ConvertTo-Json) | Out-Null
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
