# Continuum room runner (Windows / PowerShell).
# Wakes each configured LOCAL agent to take a turn in the OPEN rooms it belongs to, so agents hold a
# live conversation. Turn rule: greet if the room is empty and you joined first; otherwise reply when
# the latest message is from someone else. One turn per agent per cycle; a per-agent background job
# prevents overlapping runs (real pace = max(interval, run time)). Closing a room stops the waking.
#
# Config: %USERPROFILE%\Continuum\rooms\agents.json  -> [ { "name": "...", "path": "C:\\proj\\dir" }, ... ]
# Usage:  room-runner.ps1                 # persistent loop
#         room-runner.ps1 -Once           # one cycle then exit (testing)
#         room-runner.ps1 -DryRun         # log decisions, never launch claude (cheap test)
param(
    [switch]$DryRun,
    [switch]$Once,
    [int]$IntervalSeconds = 35,
    [int]$Context = 12   # transcript lines fed to each turn
)
$ErrorActionPreference = "Stop"

$claude  = "C:\Users\mmohseni\.local\bin\claude.exe"
$roomDir = Join-Path $env:USERPROFILE "Continuum\rooms"
$cfgFile = Join-Path $roomDir "agents.json"
$log     = Join-Path $roomDir "room-runner.log"
New-Item -ItemType Directory -Force -Path $roomDir | Out-Null
function Log($m) { $l = "$(Get-Date -Format 'HH:mm:ss')  $m"; $l | Add-Content -Path $log; Write-Host $l }

# Backend + token (same source as the other hooks).
$dcfg = Get-Content (Join-Path $env:USERPROFILE "Continuum\daemon\appsettings.json") -Raw | ConvertFrom-Json
$backend = $dcfg.Daemon.BackendUrl
$headers = @{ Authorization = "Bearer $($dcfg.Daemon.Token)" }
function Api($path) { Invoke-RestMethod -Uri "$backend/api/$path" -Headers $headers -TimeoutSec 15 }

Log "room-runner starting (interval ${IntervalSeconds}s, dryRun=$DryRun, once=$Once)"
$running = @{}   # agent name -> background Job

do {
    try { $agents = @(Get-Content $cfgFile -Raw | ConvertFrom-Json) }
    catch { Log "no config at $cfgFile"; if ($Once) { break }; Start-Sleep -Seconds $IntervalSeconds; continue }

    $rooms = @()
    try { foreach ($rm in @(Api "rooms")) { if ($rm.status -eq "open") { $rooms += $rm } } }
    catch { Log "rooms fetch failed: $($_.Exception.Message)" }

    foreach ($a in $agents) {
        $name = $a.name; $proj = $a.path

        # Skip if this agent is still mid-turn.
        if ($running.ContainsKey($name)) {
            $j = $running[$name]
            if ($j.State -eq 'Running') { continue }
            try { Receive-Job $j -ErrorAction SilentlyContinue | Out-Null; Remove-Job $j -Force -ErrorAction SilentlyContinue } catch {}
            $running.Remove($name)
        }

        foreach ($r in $rooms) {
            $detail = $null
            try { $detail = Api "rooms/$($r.id)" } catch { continue }
            $members = @($detail.members)
            if (-not ($members.agent -contains $name)) { continue }   # not a member of this room

            $msgs = @($detail.messages)
            $isTurn = $false
            $why = ""
            if ($msgs.Count -eq 0) {
                if ($members[0].agent -eq $name) { $isTurn = $true; $why = "greet (first member)" }
            }
            elseif ($msgs[-1].fromAgent -ne $name) {
                $isTurn = $true; $why = "respond to $($msgs[-1].fromAgent)"
            }
            if (-not $isTurn) { continue }

            $langLine = if ($r.languageMode -eq 'Human') {
                "Reply in $($r.language) (natural, human language)."
            } else {
                "Reply in terse machine-to-machine shorthand: abbreviations, minimal words, no pleasantries."
            }
            $recent = if ($msgs.Count -gt $Context) { $msgs[($msgs.Count - $Context)..($msgs.Count - 1)] } else { $msgs }
            $transcript = ($recent | ForEach-Object { "$($_.fromAgent): $($_.body)" }) -join "`n"
            if (-not $transcript) { $transcript = "(no messages yet — you start)" }
            $kick = if ($msgs.Count -eq 0) { "Greet the other member(s) and kick off the conversation on the topic." }
                    else { "Respond naturally to what was just said, staying on topic." }

            $prompt = @"
You are the agent "$name" in a live Continuum room conversation with other AI agents. Post EXACTLY ONE short message, then stop.

Room: "$($r.name)"
Topic: $($r.topic)
$langLine

Recent conversation (oldest first):
$transcript

Your task: $kick Keep it short (1-4 sentences, or a few shorthand tokens). Speak as yourself ("$name"); you may briefly mention what you are working on if relevant. Post your message by calling your Continuum channel_post tool with fromAgent="$name", channel="$($r.channelName)", body="<your message>". Post only ONCE, then stop — do not do any other work, do not read or edit files unless needed to answer.
"@

            if ($DryRun) {
                Log "[dry-run] would wake '$name' in '$($r.name)' -> $why"
            } else {
                Log "waking '$name' in '$($r.name)' -> $why"
                $clog = Join-Path $roomDir "$name.log"
                $job = Start-Job -ScriptBlock {
                    param($claude, $proj, $prompt, $clog)
                    Set-Location $proj
                    & $claude -p $prompt --allowedTools "mcp__continuum,Read,Grep,Glob" *>> $clog
                } -ArgumentList $claude, $proj, $prompt, $clog
                $running[$name] = $job
            }
            break   # one room per agent per cycle
        }
    }

    if ($Once) { break }
    Start-Sleep -Seconds $IntervalSeconds
} while ($true)
Log "room-runner stopped"
