# Continuum UserPromptSubmit hook (Windows / PowerShell). Surfaces unread inter-agent bus messages
# addressed to this session's agent, so peers' messages appear automatically on your next prompt —
# no /bus-check needed. Marks them read so each surfaces exactly once. Best-effort: never blocks a
# prompt, never fails. Reads backend + token from env, falling back to the daemon's appsettings.json.
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
    if (-not $backend -or -not $token) { exit 0 }  # not configured; surface nothing

    $cwd = $j.cwd
    $base = if ($cwd) { Split-Path $cwd -Leaf } else { "session" }
    # Same agent-name resolution as session-start.ps1: CONTINUUM_AGENT -> .continuum-agent -> folder.
    $agent = $env:CONTINUUM_AGENT
    if (-not $agent -and $cwd) {
        $nameFile = Join-Path $cwd ".continuum-agent"
        if (Test-Path $nameFile) { $agent = (Get-Content $nameFile -Raw).Trim() }
    }
    if (-not $agent) { $agent = $base }

    $headers = @{ Authorization = "Bearer $token" }
    $enc = [uri]::EscapeDataString($agent)
    $resp = Invoke-RestMethod -Uri "$backend/api/bus/inbox?agent=$enc&unreadOnly=true&markRead=true" -Headers $headers -TimeoutSec 6
    $msgs = @($resp) | Where-Object { $_ -ne $null }
    if ($msgs.Count -eq 0) { exit 0 }  # nothing waiting; inject nothing

    $lines = foreach ($m in $msgs) { "- from $($m.fromAgent): $($m.body)" }
    $note = "## New Continuum bus messages (addressed to you, agent `"$agent`")`n" +
            ($lines -join "`n") +
            "`n`nThese just arrived from other agents on the inter-agent bus. If any needs a reply, use bus_send (or the bus tools) to respond."

    $out = @{ hookSpecificOutput = @{ hookEventName = "UserPromptSubmit"; additionalContext = $note } } | ConvertTo-Json -Depth 6
    Write-Output $out
} catch {
    exit 0  # never block a prompt
}
