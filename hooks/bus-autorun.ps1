# Continuum bus auto-runner for the dotnet-talk-website agent.
# Polls that agent's bus inbox; ONLY when a message is waiting, wakes a headless `claude` run
# (draft-safe: auto-accept file edits + Continuum bus tools; nothing risky) to handle it — draft the
# article as a LOCAL file and reply on the bus for review. No WordPress/publish (gated for a human).
# Cheap when idle (one API call, no agent). Lock-guarded so runs never overlap. Best-effort logging.
$ErrorActionPreference = "Stop"

$agent  = "dotnet-talk-website"
$proj   = "D:\dotnet-talk-projects\dotnet-talk-website"
$claude = "C:\Users\mmohseni\.local\bin\claude.exe"
$logDir = Join-Path $env:USERPROFILE "Continuum\autorun"
$log    = Join-Path $logDir "$agent.log"
$lock   = Join-Path $logDir "$agent.lock"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
function Log($m) { "$(Get-Date -Format o)  $m" | Add-Content -Path $log }

# Resolve backend + token (same source as the hooks).
try {
    $cfg = Get-Content (Join-Path $env:USERPROFILE "Continuum\daemon\appsettings.json") -Raw | ConvertFrom-Json
    $backend = $cfg.Daemon.BackendUrl
    $headers = @{ Authorization = "Bearer $($cfg.Daemon.Token)" }
} catch { Log "no config; exiting"; exit 0 }

# Lock: skip if a run is in progress (auto-clear if stale > 30 min).
if (Test-Path $lock) {
    $age = (Get-Date) - (Get-Item $lock).LastWriteTime
    if ($age.TotalMinutes -lt 30) { exit 0 }
    Log "clearing stale lock ($([int]$age.TotalMinutes)m)"; Remove-Item $lock -Force -ErrorAction SilentlyContinue
}

# Peek the inbox (do NOT consume — the headless run's own hook/read handles that).
try {
    $enc = [uri]::EscapeDataString($agent)
    $inbox = Invoke-RestMethod -Uri "$backend/api/bus/inbox?agent=$enc&unreadOnly=true&markRead=false" -Headers $headers -TimeoutSec 10
} catch { Log "inbox check failed: $($_.Exception.Message)"; exit 0 }
$msgs = @($inbox) | Where-Object { $_ -ne $null }
if ($msgs.Count -eq 0) { exit 0 }  # nothing waiting — stay quiet

Log "found $($msgs.Count) unread message(s) for $agent; launching headless run"
New-Item -ItemType File -Path $lock -Force | Out-Null
try {
    $prompt = @"
You are the dotnet-talk-website agent, woken to handle inter-agent bus work. New bus messages from agent-talk have been surfaced into your context and concern writing/maintaining an article about Continuum for the DotNet Talk blog. Act now:

1. Write the article as a LOCAL draft file inside this project. Follow agent-talk's decisions: a launch/announcement post, standalone (not tied to the series). Respect the keep-out list — do NOT include server IP addresses, hostnames, tokens, passwords, SSH keys, or internal infra details. You MAY link the public repo (https://github.com/MehdiMohseni82/continuum) and the live site (https://continuum.dotnet-talk.com).
2. Do NOT publish or deploy to the live site or any server — publishing is gated for a human. Produce a local draft only.
3. When done, reply to agent-talk on the bus (use your Continuum bus_send tool, from dotnet-talk-website) with the draft file's path and a 2-3 line summary, and ask for review before any publishing.

Use your own knowledge of this site and its publishing workflow; do not run destructive, deploy, or publish commands. If nothing is actionable, reply briefly on the bus and stop.
"@
    Push-Location $proj
    & $claude -p $prompt --permission-mode acceptEdits --allowedTools "mcp__continuum,Write,Edit,Read,Glob,Grep,TodoWrite,LS" 2>&1 | Add-Content -Path $log
    $code = $LASTEXITCODE
    Pop-Location
    Log "headless run complete (exit $code)"
} catch {
    Log "run error: $($_.Exception.Message)"
} finally {
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
}
