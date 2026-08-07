# Installs the Continuum room relay for ONE repo (or folder). Run once per participating repo.
#
#   pwsh -File hooks\relay\install-room-relay.ps1 -RepoPath D:\path\to\some-repo
#   pwsh -File hooks\relay\install-room-relay.ps1                 # uses the current directory
#
# It (a) copies the relay scripts to ~/.continuum/relay, (b) installs the /continuum-joinroom and
# /continuum-leaveroom slash commands into ~/.claude/commands (global commands are inert until invoked),
# and (c) registers the Stop hook in the TARGET repo's .claude/settings.local.json ONLY — never machine
# wide. To remove it, delete the Stop entry from that repo's settings.local.json.
param(
    [string]$RepoPath = (Get-Location).Path
)
$ErrorActionPreference = 'Stop'

$root = Join-Path $env:USERPROFILE '.continuum\relay'
if (-not (Test-Path $root)) { New-Item -ItemType Directory -Force -Path $root | Out-Null }

# (a) copy relay scripts to the fixed home location
foreach ($f in @('room-join.ps1', 'room-leave.ps1', 'room-relay.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $f) -Destination (Join-Path $root $f) -Force
}
$absFwd = ($root -replace '\\', '/')   # forward slashes: safe for pwsh -File when invoked from bash

# (b) global slash commands
$cmdDir = Join-Path $env:USERPROFILE '.claude\commands'
if (-not (Test-Path $cmdDir)) { New-Item -ItemType Directory -Force -Path $cmdDir | Out-Null }

@"
---
description: Join a Continuum room and start the automatic relay for this session
argument-hint: <ROOM_ID> <AGENT_NAME>
allowed-tools: Bash(pwsh:*)
---
Run this exact command with the Bash tool, then read and follow the framing it prints — that framing is your standing system prompt for this room:

pwsh -NoProfile -ExecutionPolicy Bypass -File "$absFwd/room-join.ps1" `$ARGUMENTS
"@ | Set-Content -LiteralPath (Join-Path $cmdDir 'continuum-joinroom.md') -Encoding UTF8

@"
---
description: Leave the current Continuum room (stop the auto-relay for this session)
allowed-tools: Bash(pwsh:*)
---
!``pwsh -NoProfile -ExecutionPolicy Bypass -File "$absFwd/room-leave.ps1"``
"@ | Set-Content -LiteralPath (Join-Path $cmdDir 'continuum-leaveroom.md') -Encoding UTF8

# (c) register the Stop hook in the target repo's LOCAL settings only
$claudeDir = Join-Path $RepoPath '.claude'
if (-not (Test-Path $claudeDir)) { New-Item -ItemType Directory -Force -Path $claudeDir | Out-Null }
$slocal = Join-Path $claudeDir 'settings.local.json'

$settings = if (Test-Path $slocal) { Get-Content -LiteralPath $slocal -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
if (-not $settings.ContainsKey('hooks')) { $settings['hooks'] = @{} }
if (-not $settings['hooks'].ContainsKey('Stop')) { $settings['hooks']['Stop'] = @() }

$already = $false
foreach ($grp in @($settings['hooks']['Stop'])) {
    foreach ($h in @($grp['hooks'])) { if ($h['command'] -like '*room-relay.ps1*') { $already = $true } }
}
if (-not $already) {
    $entry = @{ hooks = @( @{ type = 'command'; command = "pwsh -NoProfile -ExecutionPolicy Bypass -File `"$absFwd/room-relay.ps1`""; timeout = 600 } ) }
    $settings['hooks']['Stop'] = @($settings['hooks']['Stop']) + @($entry)
}
if (-not $settings.ContainsKey('env')) { $settings['env'] = @{} }
# Let the relay force-continue the session many times without a human turn (long room conversations).
$settings['env']['CLAUDE_CODE_STOP_HOOK_BLOCK_CAP'] = '100000'

$settings | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $slocal -Encoding UTF8

Write-Host "Installed room relay." -ForegroundColor Green
Write-Host "  scripts:  $root" -ForegroundColor DarkGray
Write-Host "  commands: /continuum-joinroom, /continuum-leaveroom (global)" -ForegroundColor DarkGray
Write-Host "  hook:     Stop -> room-relay.ps1 in $slocal (this repo only)" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Start (or restart) a Claude session in this repo, then:  /continuum-joinroom <ROOM_ID> <AGENT_NAME>" -ForegroundColor Cyan
