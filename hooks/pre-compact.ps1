# Continuum PreCompact hook (Windows): snapshots the tail of the transcript as a checkpoint before
# the context window compacts, so key recent state survives. For a curated snapshot, have the model
# call the context_checkpoint MCP tool instead — this is the automatic safety net.
#
# The bash version of this hook shipped in Phase 1; Windows had none, so compaction on a Windows
# machine silently lost the working context. Behaviour matches hooks/pre-compact.sh exactly.
#
# Fail-open everywhere: a hook that errors must never block compaction.
$ErrorActionPreference = 'SilentlyContinue'

try {
    $input_ = [Console]::In.ReadToEnd()
    $hook = $input_ | ConvertFrom-Json
    $sessionId = $hook.session_id
    $transcript = $hook.transcript_path
    if (-not $sessionId) { exit 0 }

    # Config: env first, then ~/.continuum/config.json. There is deliberately no localhost default —
    # talking to a backend that isn't there produced sessions that looked fine and remembered nothing.
    $backend = $env:CONTINUUM_BACKEND
    $token   = $env:CONTINUUM_TOKEN
    $cfgPath = Join-Path $env:USERPROFILE '.continuum\config.json'
    if (Test-Path $cfgPath) {
        $cfg = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json
        if (-not $backend) { $backend = $cfg.backend }
        if (-not $token)   { $token   = $cfg.token }
    }
    if (-not $backend -or -not $token) { exit 0 }

    # Last few user/assistant text turns, newest 12 of the final 60 transcript lines.
    $lines = @()
    if ($transcript -and (Test-Path -LiteralPath $transcript)) {
        foreach ($line in (Get-Content -LiteralPath $transcript -Tail 60)) {
            if (-not $line.Trim()) { continue }
            $obj = $line | ConvertFrom-Json
            if (-not $obj.message.content) { continue }
            $role = if ($obj.message.role) { $obj.message.role } else { '?' }
            $text = if ($obj.message.content -is [string]) { $obj.message.content }
                    else { (($obj.message.content | ForEach-Object { $_.text }) -join ' ').Trim() }
            if (-not $text) { continue }
            if ($text.Length -gt 400) { $text = $text.Substring(0, 400) }
            $lines += "- **$role**: $text"
        }
    }
    if ($lines.Count -gt 12) { $lines = $lines[-12..-1] }
    $tail = if ($lines.Count) { $lines -join "`n" } else { '(no transcript tail available)' }

    $body = @{
        sessionId = $sessionId
        content   = "## Auto checkpoint (pre-compact)`n`n$tail"
        reason    = 'pre-compact'
    } | ConvertTo-Json -Depth 4

    Invoke-RestMethod -Method Post -Uri "$backend/api/checkpoints" -TimeoutSec 15 `
        -Headers @{ Authorization = "Bearer $token" } -ContentType 'application/json' -Body $body | Out-Null
}
catch { }

exit 0
