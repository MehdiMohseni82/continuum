# Continuum room relay — a Claude Code **Stop hook**.
#
# Fires when the session finishes a turn. If this session has joined a room (a <<CONTINUUM-ROOM …>>
# marker is present in the transcript), it:
#   1. posts the session's last message to the room,
#   2. enforces the "act, don't just talk" guard (repo agents),
#   3. long-polls for the peer's next message and hands it back as the session's next prompt.
# If the session never joined a room, it exits immediately — a no-op. It is registered ONLY in a
# participating repo's .claude/settings.local.json, never machine-wide.
#
# STDOUT DISCIPLINE: Claude Code parses this hook's stdout as JSON. So nothing is written to stdout
# except the final decision object (when we continue the session). All diagnostics go to a log file.

$ErrorActionPreference = 'Stop'

# ---- read hook input ----
$raw = [Console]::In.ReadToEnd()
try { $in = $raw | ConvertFrom-Json } catch { exit 0 }
$sessionId  = [string]$in.session_id
$transcript = [string]$in.transcript_path
$cwd        = [string]$in.cwd
if (-not $sessionId) { $sessionId = 'unknown' }

# ---- paths ----
$root     = Join-Path $env:USERPROFILE '.continuum\relay'
$stateDir = Join-Path $root 'state'
$logDir   = Join-Path $root 'log'
foreach ($d in @($root, $stateDir, $logDir)) { if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null } }
$statePath = Join-Path $stateDir "$sessionId.json"
$logPath   = Join-Path $logDir   "$sessionId.log"
function Log([string]$m) { try { Add-Content -LiteralPath $logPath -Value ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $m) } catch {} }

# The whole hook is fail-open: any error just lets the session stop normally.
try {
    # ---- 1. is this a room session? read the bind marker from the transcript ----
    if (-not (Test-Path $transcript)) { exit 0 }
    $content = Get-Content -LiteralPath $transcript -Raw
    $bindRx  = [regex]'<<CONTINUUM-ROOM room=([^\s>]+) agent=([^\s>]+) channel=([^\s>]+)>>'
    $leaveRx = [regex]'<<CONTINUUM-ROOM-LEAVE>>'
    $binds = $bindRx.Matches($content)
    if ($binds.Count -eq 0) { exit 0 }                       # never joined — no-op
    $bind = $binds[$binds.Count - 1]
    $leaves = $leaveRx.Matches($content)
    if ($leaves.Count -gt 0 -and $leaves[$leaves.Count - 1].Index -gt $bind.Index) { Log 'left room; idle'; exit 0 }
    $roomId  = $bind.Groups[1].Value
    $agent   = $bind.Groups[2].Value

    # ---- backend ----
    # HTTP goes through curl.exe with -4: this machine's IPv6 route to Cloudflare is dead, and .NET's
    # Invoke-RestMethod picks the AAAA address and hangs, so we force IPv4 with the native Windows curl.
    $cfg = Get-Content -LiteralPath (Join-Path $env:USERPROFILE 'Continuum\daemon\appsettings.json') -Raw | ConvertFrom-Json
    $backend = $cfg.Daemon.BackendUrl
    $token   = [string]$cfg.Daemon.Token
    function Api([string]$Method, [string]$Url, [string]$Body) {
        $a = @('-4', '-s', '--max-time', '25', '-X', $Method, $Url, '-H', "Authorization: Bearer $token")
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

    # ---- state ----
    $state = if (Test-Path $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
    $lastSeenId = if ($state) { [long]$state.lastSeenId } else { 0L }
    $lastPosted = if ($state) { [string]$state.lastPosted } else { '' }
    $lastTree   = if ($state) { [string]$state.lastTree }   else { $null }
    $talkTurns  = if ($state) { [int]$state.talkTurns }     else { 0 }
    function Save { @{ lastSeenId = $lastSeenId; lastPosted = $lastPosted; lastTree = $lastTree; talkTurns = $talkTurns } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath $statePath }

    # ---- 2. room still open? (force-stop = close the room) ----
    $meta = $null
    try { $meta = @(Api 'GET' "$backend/api/rooms") | Where-Object { $_.id -eq $roomId } | Select-Object -First 1 } catch { Log "rooms fetch failed: $($_.Exception.Message)" }
    if (-not $meta -or $meta.status -ne 'open') { Log 'room closed/gone; stopping'; Save; exit 0 }

    # ---- my last message from the transcript (+ its token usage) ----
    $mine = $null
    $mineUsage = $null
    foreach ($line in [System.IO.File]::ReadLines($transcript)) {
        if (-not $line.Trim()) { continue }
        try { $o = $line | ConvertFrom-Json } catch { continue }
        $role = $o.message.role; if (-not $role) { $role = $o.role }
        $type = $o.type
        if ($role -eq 'assistant' -or $type -eq 'assistant') {
            $c = $o.message.content; if ($null -eq $c) { $c = $o.content }
            $t = ''
            if ($c -is [string]) { $t = $c }
            elseif ($c) { foreach ($blk in $c) { if ($blk.type -eq 'text' -and $blk.text) { $t += $blk.text } elseif ($blk -is [string]) { $t += $blk } } }
            if ($t.Trim()) { $mine = $t.Trim(); $mineUsage = $o.message.usage }
        }
    }

    function Is-NoPost([string]$s) {
        if (-not $s) { return $true }
        $x = $s.Trim().Trim('*','_','`','"',"'",' ').TrimEnd('.','!',' ')
        return ($x -ieq 'PASS' -or $x -ieq 'ready')
    }
    function Is-Done([string]$s) {
        if (-not $s) { return $false }
        return $s.Trim().Trim('*','_','`','"',"'",' ').StartsWith('[DONE]', [System.StringComparison]::OrdinalIgnoreCase)
    }
    function Post([string]$body, $usage) {
        $payload = @{ fromAgent = $agent; body = $body }
        if ($usage) {
            if ($null -ne $usage.input_tokens)                { $payload.inputTokens         = [int]$usage.input_tokens }
            if ($null -ne $usage.output_tokens)               { $payload.outputTokens        = [int]$usage.output_tokens }
            if ($null -ne $usage.cache_read_input_tokens)     { $payload.cacheReadTokens     = [int]$usage.cache_read_input_tokens }
            if ($null -ne $usage.cache_creation_input_tokens) { $payload.cacheCreationTokens = [int]$usage.cache_creation_input_tokens }
        }
        return Api 'POST' "$backend/api/rooms/$roomId/post" ($payload | ConvertTo-Json)
    }

    # ---- 3. post my message to the room (dedup on exact text) ----
    if ($mine -and -not (Is-NoPost $mine) -and $mine -ne $lastPosted) {
        try {
            $posted = Post $mine $mineUsage
            $lastPosted = $mine
            if ($posted -and $posted.id) { $lastSeenId = [long]$posted.id }
            Log "posted ($($mine.Length) chars)"
        } catch { Log "post failed: $($_.Exception.Message)"; Save; exit 0 }
        if (Is-Done $mine) { Log 'I declared [DONE]; stopping'; Save; exit 0 }
    }

    # ---- 4. act-not-talk guard (repo agents only) ----
    if (Test-Path (Join-Path $cwd '.git')) {
        # Signature of the working tree: changed-file list PLUS the actual content diff, so repeated edits
        # to the same file register as progress (porcelain alone only lists filenames). Needs a baseline commit.
        $tree = ''
        try {
            $porcelain = (& git -C $cwd status --porcelain 2>$null) -join "`n"
            $diff      = (& git -C $cwd diff HEAD 2>$null) -join "`n"
            $tree = $porcelain + "`n---`n" + $diff
        } catch {}
        # "Progress" = working tree changed, OR this turn showed real work (a code block / test output).
        $showedWork = $mine -and ($mine -match '```' -or $mine -match '(?i)\b(passed|failed|tests?\s+run|assert|traceback|error:)\b')
        if ($lastTree -ne $null -and $tree -eq $lastTree -and -not $showedWork) { $talkTurns++ } else { $talkTurns = 0 }
        $lastTree = $tree
        if ($talkTurns -ge 4) {
            Log "no-progress guard tripped ($talkTurns talk turns)"
            try { Post "[DONE] Stopping: 4 turns of discussion with no code change and no test run. This needs a concrete action or a human decision, not more analysis." | Out-Null } catch {}
            Save; exit 0
        }
    }

    # ---- 5. long-poll for the peer's next message, hand it back as the next prompt ----
    Save
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 560) {
        $new = @()
        try { $new = @(Api 'GET' "$backend/api/rooms/$roomId/messages?since=$lastSeenId&take=50") } catch { Log "poll failed: $($_.Exception.Message)" }
        if ($new.Count -gt 0) {
            # advance past our own echoes; deliver the first message from someone else.
            foreach ($m in $new) {
                if ([long]$m.id -gt $lastSeenId) { $lastSeenId = [long]$m.id }
                if ($m.fromAgent -ne $agent) {
                    Save
                    if (Is-Done $m.body) { Log "peer $($m.fromAgent) declared [DONE]; stopping"; exit 0 }
                    $reason = ''
                    if ($m.body.Length -gt 8000) {
                        $inbox = Join-Path $stateDir "$sessionId.incoming.txt"
                        Set-Content -LiteralPath $inbox -Value $m.body
                        $reason = "New room message from $($m.fromAgent) (large). Read it from: $inbox — then respond with your next action."
                    } else {
                        $reason = "Room message from $($m.fromAgent) — your turn to respond:`n`n$($m.body)"
                    }
                    Log "delivering peer msg id=$($m.id) from $($m.fromAgent)"
                    @{ decision = 'block'; reason = $reason } | ConvertTo-Json -Compress
                    exit 0
                }
            }
            Save
        }
        # room may be force-stopped while we wait.
        try { $meta = @(Api 'GET' "$backend/api/rooms") | Where-Object { $_.id -eq $roomId } | Select-Object -First 1 } catch {}
        if (-not $meta -or $meta.status -ne 'open') { Log 'room closed while waiting; stopping'; exit 0 }
        Start-Sleep -Seconds 2
    }
    Log 'poll window elapsed with no peer message; idling'
    Save
    exit 0
}
catch {
    Log "relay error (fail-open): $($_.Exception.Message)"
    exit 0
}
