<#
.SYNOPSIS
  Installs the Continuum agent (daemon + MCP server) on this machine, pointed at a remote backend.
  The backend (DB + Ollama + API) runs on the server; this only sets up the local pieces.

.EXAMPLE
  .\scripts\install-agent.ps1 -Token "<CONTINUUM_TOKEN>"
  .\scripts\install-agent.ps1 -BackendUrl "https://continuum.dotnet-talk.com" -Token "..." -MachineName "laptop"

.NOTES
  Run from the repo root (or anywhere; it locates the repo from its own path). Requires .NET 9 SDK,
  and the `claude` CLI for MCP registration. No admin needed (uses the per-user Startup folder).
#>
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

# 2) publish daemon + mcp
Write-Host "Publishing daemon + MCP..." -ForegroundColor Cyan
dotnet publish (Join-Path $repo "src\Continuum.Daemon") -c Release -o $daemonDir --nologo | Out-Null
dotnet publish (Join-Path $repo "src\Continuum.Mcp")    -c Release -o $mcpDir    --nologo | Out-Null

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

# pwsh -WindowStyle Hidden runs the console daemon windowless AND waits on it, so the task can
# supervise it (restart on failure). $host.SetShouldExit surfaces a non-zero code if it dies.
$runCmd = "Set-Location '$daemonDir'; & '$dotnet' '$dll'; exit `$LASTEXITCODE"
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

# 5b) install the SessionStart hook (auto-register on the bus + inject memory)
Write-Host "Installing SessionStart hook..." -ForegroundColor Cyan
$hookScript = Join-Path $InstallDir "session-start.ps1"
Copy-Item (Join-Path $repo "hooks\session-start.ps1") $hookScript -Force
$settingsPath = Join-Path $env:USERPROFILE ".claude\settings.json"
$settings = if (Test-Path $settingsPath) { Get-Content $settingsPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
if (-not $settings.PSObject.Properties['hooks']) { $settings | Add-Member hooks ([pscustomobject]@{}) }
$hookCmd = "powershell -NoProfile -File `"$hookScript`""
$entry = [pscustomobject]@{ matcher = '*'; hooks = @([pscustomobject]@{ type = 'command'; command = $hookCmd; timeout = 20 }) }
$settings.hooks | Add-Member SessionStart @($entry) -Force   # replaces any prior Continuum SessionStart
$settings | ConvertTo-Json -Depth 12 | Out-File $settingsPath -Encoding utf8

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
