<#
.SYNOPSIS
  Installs the Continuum agent (daemon + MCP server) on this machine, pointed at a remote backend.
  The backend (DB + Ollama + API) runs on the server; this only sets up the local pieces.

.EXAMPLE
  .\scripts\install-agent.ps1 -Token "<CONTINUUM_TOKEN>"
  .\scripts\install-agent.ps1 -BackendUrl "https://continuum.dotnet-talk.com" -Token "..." -MachineName "laptop"

.NOTES
  Run from the repo root (or anywhere; it locates the repo from its own path). Requires .NET 9 SDK,
  PowerShell 7+ (`pwsh`), and the `claude` CLI for MCP registration. No admin needed (uses the
  per-user Startup folder).
#>
#Requires -Version 7.0
param(
  [Parameter(Mandatory = $true)][string]$Token,
  [string]$BackendUrl = "https://continuum.dotnet-talk.com",
  [string]$MachineName = $env:COMPUTERNAME,
  [string]$InstallDir  = "$env:USERPROFILE\Continuum"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$daemonDir = Join-Path $InstallDir "daemon"
$mcpDir    = Join-Path $InstallDir "mcp"
$cliDir    = Join-Path $env:USERPROFILE ".continuum\bin"
$dotnet    = (Get-Command dotnet).Source

Write-Host "Continuum agent install" -ForegroundColor Cyan
Write-Host "  repo:    $repo"
Write-Host "  backend: $BackendUrl"
Write-Host "  machine: $MachineName"
Write-Host "  target:  $InstallDir"

# 1) stop any running daemon so we can overwrite the binaries
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
  Where-Object { $_.CommandLine -like "*Continuum.Daemon.dll*" } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2

# 2) publish daemon + mcp + the `continuum` CLI
Write-Host "Publishing daemon + MCP + CLI..." -ForegroundColor Cyan
dotnet publish (Join-Path $repo "src\Continuum.Daemon") -c Release -o $daemonDir --nologo | Out-Null
dotnet publish (Join-Path $repo "src\Continuum.Mcp")    -c Release -o $mcpDir    --nologo | Out-Null
dotnet publish (Join-Path $repo "src\Continuum.Cli")    -c Release -o $cliDir    --nologo | Out-Null

# 2b) One config file every Continuum piece reads. The hooks and the CLI look here when
# CONTINUUM_BACKEND/CONTINUUM_TOKEN aren't set; without it they fall back to localhost silently.
New-Item -ItemType Directory -Force -Path (Join-Path $env:USERPROFILE ".continuum") | Out-Null
@{ backend = $BackendUrl; token = $Token; machine = $MachineName; agent = $MachineName } |
  ConvertTo-Json | Out-File (Join-Path $env:USERPROFILE ".continuum\config.json") -Encoding utf8

# 2c) Put `continuum` on PATH for this user. The slash commands invoke it by bare name so they
# survive Claude settings sync between machines — which only works if PATH actually resolves it.
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -notlike "*$cliDir*") {
  [Environment]::SetEnvironmentVariable("Path", "$cliDir;$userPath", "User")
  Write-Host "  added $cliDir to your user PATH (open a new terminal to pick it up)" -ForegroundColor DarkGray
}
$env:Path = "$cliDir;$env:Path"

# 3) production daemon config (overwrites the dev appsettings that publish copies in)
$cursor = (Join-Path $daemonDir "continuum-cursors.db").Replace('\','\\')
@"
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.Hosting.Lifetime": "Information" } },
  "Daemon": {
    "BackendUrl": "$BackendUrl",
    "Token": "$Token",
    "MachineName": "$MachineName",
    "PollSeconds": 10,
    "BatchSize": 500,
    "CursorDbPath": "$cursor"
  }
}
"@ | Out-File (Join-Path $daemonDir "appsettings.json") -Encoding utf8

# 4) auto-start WITH self fail-over: a per-user scheduled task that runs at logon and, crucially,
#    restarts the daemon if it ever exits. The daemon now hosts the room runner too, so this one
#    supervisor keeps both history backfill and room-driving alive across crashes/reboots.
$dll  = Join-Path $daemonDir "Continuum.Daemon.dll"
$pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if (-not $pwsh) { $pwsh = (Get-Command powershell).Source }
$taskName = "ContinuumDaemon"
$startup  = [Environment]::GetFolderPath('Startup')

# pwsh -WindowStyle Hidden runs windowless and hosts a watchdog loop that relaunches the daemon
# whenever it exits — reliable self fail-over that doesn't depend on Task Scheduler's flaky
# restart-on-failure semantics. Task Scheduler only has to keep this loop alive at logon.
$runCmd = "Set-Location '$daemonDir'; while (`$true) { try { & '$dotnet' '$dll' } catch {}; Start-Sleep -Seconds 3 }"
$autoStarted = $false
try {
  $action   = New-ScheduledTaskAction -Execute $pwsh -Argument "-NoProfile -WindowStyle Hidden -Command `"$runCmd`""
  $trigger  = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
  $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                -StartWhenAvailable -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
                -ExecutionTimeLimit ([TimeSpan]::Zero)
  Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
  Remove-Item (Join-Path $startup "ContinuumDaemon.vbs") -ErrorAction SilentlyContinue  # retire the old fire-and-forget launcher
  $autoStarted = $true
  Write-Host "Registered scheduled task '$taskName' (logon start + restart-on-failure)." -ForegroundColor Cyan
} catch {
  # Fallback for locked-down machines where task registration is denied: hidden Startup launcher (no failover).
  Write-Host "Scheduled task registration failed ($($_.Exception.Message)); falling back to Startup launcher." -ForegroundColor Yellow
  $vbs = @(
    'Set sh = CreateObject("WScript.Shell")'
    'sh.CurrentDirectory = "' + $daemonDir + '"'
    'sh.Run "' + '""' + $dotnet + '""' + ' ' + '""' + $dll + '""' + '", 0, False'
  ) -join "`r`n"
  $vbs | Out-File (Join-Path $daemonDir "launch-hidden.vbs") -Encoding ascii
  Copy-Item (Join-Path $daemonDir "launch-hidden.vbs") (Join-Path $startup "ContinuumDaemon.vbs") -Force
}

# 5) register the MCP server at user scope (all projects)
Write-Host "Registering MCP server..." -ForegroundColor Cyan
$mcpDll = Join-Path $mcpDir "Continuum.Mcp.dll"
claude mcp remove continuum -s user 2>&1 | Out-Null
claude mcp add continuum --scope user -e CONTINUUM_BACKEND=$BackendUrl -e CONTINUUM_TOKEN=$Token -- dotnet $mcpDll | Out-Null

# 5b) install the SessionStart + PreCompact hooks (inject memory, auto-checkpoint before compaction)
Write-Host "Installing Claude Code hooks..." -ForegroundColor Cyan
$hookDir = Join-Path $env:USERPROFILE ".continuum\hooks"
New-Item -ItemType Directory -Force -Path $hookDir | Out-Null
Copy-Item (Join-Path $repo "hooks\session-start.ps1") (Join-Path $hookDir "session-start.ps1") -Force
if (Test-Path (Join-Path $repo "hooks\pre-compact.ps1")) {
  Copy-Item (Join-Path $repo "hooks\pre-compact.ps1") (Join-Path $hookDir "pre-compact.ps1") -Force
}

$settingsPath = Join-Path $env:USERPROFILE ".claude\settings.json"
New-Item -ItemType Directory -Force -Path (Split-Path $settingsPath) | Out-Null
$settings = if (Test-Path $settingsPath) { Get-Content $settingsPath -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
if (-not $settings.ContainsKey('hooks')) { $settings['hooks'] = @{} }

foreach ($h in @(
    @{ Event = 'SessionStart'; Script = 'session-start.ps1' },
    @{ Event = 'PreCompact';   Script = 'pre-compact.ps1'   })) {
  $script = Join-Path $hookDir $h.Script
  if (-not (Test-Path $script)) { continue }
  $cmd = "powershell -NoProfile -File `"$script`""
  # MERGE, never replace. This used to be `Add-Member <Event> @($entry) -Force`, which overwrote the
  # whole array and destroyed every hook the user had registered for that event. Drop only ours.
  $kept = @(@($settings['hooks'][$h.Event]) | Where-Object {
      $_ -and -not (@($_['hooks']) | Where-Object { "$($_['command'])" -like '*continuum*' })
    })
  $entry = @{ matcher = '*'; hooks = @(@{ type = 'command'; command = $cmd; timeout = 20 }) }
  $settings['hooks'][$h.Event] = @($kept) + @($entry)
}
$settings | ConvertTo-Json -Depth 12 | Out-File $settingsPath -Encoding utf8

# 5c) wire the Continuum MCP into Codex and Cursor too, so those runtimes can join rooms as agents.
Write-Host "Wiring Continuum MCP into Codex + Cursor..." -ForegroundColor Cyan
# Codex: ~/.codex/config.toml — append the section only if it isn't already there.
$codexCfg = Join-Path $env:USERPROFILE ".codex\config.toml"
New-Item -ItemType Directory -Force -Path (Split-Path $codexCfg) | Out-Null
if (-not ((Test-Path $codexCfg) -and (Select-String -Path $codexCfg -Pattern '\[mcp_servers\.continuum\]' -Quiet))) {
  $tomlArgs = '["' + ($mcpDll -replace '\\', '\\') + '"]'
  @"

[mcp_servers.continuum]
command = "dotnet"
args = $tomlArgs
env = { CONTINUUM_BACKEND = "$BackendUrl", CONTINUUM_TOKEN = "$Token" }
"@ | Add-Content -Path $codexCfg -Encoding utf8
}
# Cursor: ~/.cursor/mcp.json — merge the server (env must be inline for cursor's headless print mode).
$cursorCfg = Join-Path $env:USERPROFILE ".cursor\mcp.json"
New-Item -ItemType Directory -Force -Path (Split-Path $cursorCfg) | Out-Null
$cursor = if (Test-Path $cursorCfg) { Get-Content $cursorCfg -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
if (-not $cursor.PSObject.Properties['mcpServers']) { $cursor | Add-Member mcpServers ([pscustomobject]@{}) }
$cont = [pscustomobject]@{ command = "dotnet"; args = @($mcpDll);
  env = [pscustomobject]@{ CONTINUUM_BACKEND = $BackendUrl; CONTINUUM_TOKEN = $Token } }
$cursor.mcpServers | Add-Member continuum $cont -Force
$cursor | ConvertTo-Json -Depth 12 | Out-File $cursorCfg -Encoding utf8

# 6) start the daemon now
if ($autoStarted) {
  Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
  Start-ScheduledTask -TaskName $taskName
} else {
  Start-Process "wscript.exe" -ArgumentList "`"$(Join-Path $startup 'ContinuumDaemon.vbs')`""
}
Start-Sleep -Seconds 5
$proc = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -like "*Continuum.Daemon.dll*" }

Write-Host ""
if ($proc) { Write-Host "Daemon running (pid $($proc.ProcessId)); auto-starts at logon and restarts on failure." -ForegroundColor Green }
else       { Write-Host "Daemon did not start - check $daemonDir\appsettings.json and try 'dotnet $dll'." -ForegroundColor Yellow }
Write-Host "MCP 'continuum' registered (user scope). Start a NEW claude session to use it." -ForegroundColor Green
Write-Host "Room runner is now hosted inside the daemon (reads ~/Continuum/rooms/agents.json)." -ForegroundColor Green
Write-Host "Backfill of ~/.claude begins immediately; watch it at $BackendUrl"
