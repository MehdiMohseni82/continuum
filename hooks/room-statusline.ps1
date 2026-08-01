# Continuum room status line (Windows / PowerShell).
# Renders a live ticker at the bottom of each Claude Code session: model + this session's bus agent,
# and — when that agent is in an OPEN room — the room name plus the latest message(s). Claude Code
# re-runs this on its refresh timer, so the conversation updates in place. Best-effort + fast timeouts.
$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
try {
    $raw = [Console]::In.ReadToEnd()
    $j = if ($raw) { $raw | ConvertFrom-Json } else { $null }
} catch { $j = $null }

$cwd = if ($j.cwd) { $j.cwd } elseif ($j.workspace.current_dir) { $j.workspace.current_dir } else { (Get-Location).Path }
$model = if ($j.model.display_name) { $j.model.display_name } else { "" }

# Same agent-name resolution as the other hooks.
$agent = $env:CONTINUUM_AGENT
if (-not $agent -and $cwd) { $nf = Join-Path $cwd ".continuum-agent"; if (Test-Path $nf) { $agent = (Get-Content $nf -Raw).Trim() } }
if (-not $agent -and $cwd) { $agent = Split-Path $cwd -Leaf }

$base = (@($model, $agent) | Where-Object { $_ }) -join "  -  "

try {
    $dcfg = Get-Content (Join-Path $env:USERPROFILE "Continuum\daemon\appsettings.json") -Raw | ConvertFrom-Json
    $B = $dcfg.Daemon.BackendUrl; $H = @{ Authorization = "Bearer $($dcfg.Daemon.Token)" }
    $rooms = Invoke-RestMethod -Uri "$B/api/rooms" -Headers $H -TimeoutSec 3
    foreach ($r in @($rooms)) {
        if ($r.status -ne "open") { continue }
        $d = Invoke-RestMethod -Uri "$B/api/rooms/$($r.id)" -Headers $H -TimeoutSec 3
        if (-not (@($d.members).agent -contains $agent)) { continue }
        $base += "   |   room `"$($r.name)`""
        $last = @($d.messages) | Select-Object -Last 3
        $lines = foreach ($m in $last) {
            $snip = $m.body -replace '\s+', ' '
            if ($snip.Length -gt 84) { $snip = $snip.Substring(0, 84) + "..." }
            "  $($m.fromAgent): $snip"
        }
        if ($lines) { $base += "`n" + ($lines -join "`n") }
        break   # first open room this agent is in
    }
} catch { }

Write-Output $base
