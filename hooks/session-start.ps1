# Continuum SessionStart hook (Windows / PowerShell). Registers this session on the inter-agent
# bus and injects remembered context + the session's bus identity. Best-effort: never fails the session.
# Reads backend + token from env, falling back to the installed daemon's appsettings.json.
$ErrorActionPreference = "Stop"
try {
    $raw = [Console]::In.ReadToEnd()
    $j = $raw | ConvertFrom-Json

    $backend = $env:CONTINUUM_BACKEND
    $token   = $env:CONTINUUM_TOKEN
    if (-not $backend -or -not $token) {
        $cfgPath = Join-Path $env:USERPROFILE "Continuum\daemon\appsettings.json"
        if (Test-Path $cfgPath) {
            $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
            if (-not $backend) { $backend = $cfg.Daemon.BackendUrl }
            if (-not $token)   { $token   = $cfg.Daemon.Token }
        }
    }
    if (-not $backend -or -not $token) { exit 0 }  # not configured; inject nothing

    $cwd = $j.cwd
    $sid = $j.session_id
    $base = if ($cwd) { Split-Path $cwd -Leaf } else { "session" }
    # Agent name: CONTINUUM_AGENT env  ->  .continuum-agent file in the repo  ->  folder name.
    # Stable per-project name (no random suffix) so peers can address it reliably.
    $agent = $env:CONTINUUM_AGENT
    if (-not $agent -and $cwd) {
        $nameFile = Join-Path $cwd ".continuum-agent"
        if (Test-Path $nameFile) { $agent = (Get-Content $nameFile -Raw).Trim() }
    }
    if (-not $agent) { $agent = $base }
    # The derived key encodes an absolute path, so the same repo on Windows and on a Mac would be two
    # separate workspaces with two disjoint piles of memory. A repo can declare its own key in a
    # committed .continuum-project file so every machine agrees. Mirrors Continuum.Core.Domain.ProjectKey.
    $projectKey = if ($cwd) { ($cwd -replace '[^A-Za-z0-9]', '-') } else { "" }
    if ($cwd) {
        $marker = Join-Path $cwd ".continuum-project"
        if (Test-Path $marker) {
            try {
                $declared = Get-Content $marker -ErrorAction Stop |
                    Where-Object { $_.Trim() -and -not $_.Trim().StartsWith('#') } |
                    Select-Object -First 1
                if ($declared) { $projectKey = $declared.Trim() }
            } catch { }
        }
    }
    $headers = @{ Authorization = "Bearer $token" }

    # 1) register this session as a bus agent (best-effort)
    try {
        $body = @{ name = $agent; machineName = $env:COMPUTERNAME; currentSessionId = $sid; capabilities = "project:$base" } | ConvertTo-Json
        Invoke-RestMethod -Method Post -Uri "$backend/api/agents/register" -Headers $headers -ContentType "application/json" -Body $body -TimeoutSec 8 | Out-Null
    } catch {}

    # 2) pull remembered context
    $ctx = ""
    try {
        # Escaped, not interpolated: a declared key may contain '/', spaces or '&', unlike the
        # alphanumeric-and-dash key derived from a path.
        $qs = [System.Uri]::EscapeDataString($projectKey)
        $r = Invoke-RestMethod -Uri "$backend/api/context/session-start?projectKey=$qs" -Headers $headers -TimeoutSec 8
        $ctx = $r.additionalContext
    } catch {}

    # 3) compose injection: memory context + bus identity
    $busNote = "## Continuum bus`nYou are agent `"$agent`" on the inter-agent bus. Tools: agent_list (see peers), bus_send / bus_inbox (direct messages), channel_post / channel_read (topics), handoff_create / handoff_claim (pass tasks). Check bus_inbox for messages addressed to you."
    $full = ("$ctx`n`n$busNote").Trim()

    $out = @{ hookSpecificOutput = @{ hookEventName = "SessionStart"; additionalContext = $full } } | ConvertTo-Json -Depth 6
    Write-Output $out
} catch {
    exit 0  # never break session start
}
