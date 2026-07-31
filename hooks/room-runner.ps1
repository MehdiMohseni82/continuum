# DEPRECATED: the room runner now lives INSIDE the daemon (src/Continuum.Daemon/RoomRunnerService.cs),
# which is cross-platform (Windows/macOS/Linux) and self-healing via its supervisor (Task Scheduler /
# launchd / systemd). Do NOT run this at the same time as an updated daemon — both would wake the same
# agents and post duplicate turns. Kept only for quick manual testing on Windows.
#
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
            $memberNames = @($members | ForEach-Object { $_.agent })
            # Humans = anyone who has spoken but isn't a member agent (the room owner joining in).
            $humans = @($msgs | ForEach-Object { $_.fromAgent } | Select-Object -Unique |
                        Where-Object { $memberNames -notcontains $_ })

            $last = if ($msgs.Count -gt 0) { $msgs[-1] } else { $null }
            # @mentions in the latest message that resolve to a member agent.
            $mentioned = @()
            if ($last) {
                foreach ($mm in [regex]::Matches($last.body, '@([\w.\-]+)')) {
                    $hit = $memberNames | Where-Object { $_ -ieq $mm.Groups[1].Value } | Select-Object -First 1
                    if ($hit) { $mentioned += $hit }
                }
            }

            $isTurn = $false
            $why = ""
            if ($msgs.Count -eq 0) {
                if ($members[0].agent -eq $name) { $isTurn = $true; $why = "greet (first member)" }
            }
            elseif ($last.fromAgent -ne $name) {
                if ($mentioned.Count -gt 0) {
                    # Someone @mentioned specific agents — only they answer.
                    if ($mentioned -contains $name) { $isTurn = $true; $why = "answer @mention from $($last.fromAgent)" }
                } else {
                    $isTurn = $true; $why = "respond to $($last.fromAgent)"
                }
            }
            if (-not $isTurn) { continue }

            $lastIsHuman = $last -and ($humans -contains $last.fromAgent)

            $langLine = if ($r.languageMode -eq 'Human') {
                "Reply in $($r.language) (natural, human language)."
            } else {
                "Reply in terse machine-to-machine shorthand: abbreviations, minimal words, no pleasantries."
            }
            $humanLine = if ($humans.Count -gt 0) {
                "Human operator(s) in this room (people, NOT agents): $($humans -join ', '). Treat them as the human user running you — when one speaks or @mentions you, answer them directly and concretely, and do what they ask. Their word overrides agent-to-agent chatter."
            } else { "" }
            $recent = if ($msgs.Count -gt $Context) { $msgs[($msgs.Count - $Context)..($msgs.Count - 1)] } else { $msgs }
            $transcript = ($recent | ForEach-Object {
                $tag = if ($humans -contains $_.fromAgent) { " (human)" } else { "" }
                "$($_.fromAgent)$($tag): $($_.body)"
            }) -join "`n"
            if (-not $transcript) { $transcript = "(no messages yet — you start)" }
            $kick = if ($msgs.Count -eq 0) { "Greet the other member(s) and kick off the conversation on the topic." }
                    elseif ($lastIsHuman) { "The human '$($last.fromAgent)' just addressed the room. Answer them directly and helpfully — do the specific thing they asked." }
                    else { "Respond naturally to what was just said, staying on topic. If you have nothing genuinely new to add, say so in one short line or ask a pointed question — do not repeat a prior message." }

            $prompt = @"
You are the agent "$name" in a live Continuum room conversation with other AI agents and possibly a human. Post EXACTLY ONE short message, then stop.

Room: "$($r.name)"
Topic: $($r.topic)
$langLine
$humanLine

Recent conversation (oldest first; "(human)" marks a human, everyone else is an AI agent):
$transcript

Your task: $kick Keep it short (1-4 sentences, or a few shorthand tokens). Speak as yourself ("$name"); you may briefly mention what you are working on if relevant. You can @mention another member by name (e.g. @$($memberNames | Where-Object { $_ -ne $name } | Select-Object -First 1)) to direct a question at them. Post your message by calling your Continuum channel_post tool with fromAgent="$name", channel="$($r.channelName)", body="<your message>". Post only ONCE, then stop — do not do any other work, do not read or edit files unless needed to answer.
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
