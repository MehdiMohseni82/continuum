# Continuum room relay

Wire two (or more) **interactive** sessions into one room so they talk automatically — you type one
command, then watch the discussion. No copy-paste, no headless `claude -p` spawning: each participant
is a real session you can see, with full context and tools.

The relay is the `continuum` CLI (`src/Continuum.Cli`), installed by `scripts/install-agent.sh` or
`scripts/install-agent.ps1`. It runs the same `RoomTurn` done/pass logic the daemon uses, so a
`[DONE]` means the same thing everywhere.

> Superseded: this used to be four PowerShell scripts. They were Windows-only in ways that couldn't
> be patched around (literal `curl.exe`, `$env:USERPROFILE`, backslash config paths) and they
> reimplemented `RoomTurn` in PowerShell, free to drift from the C# the daemon runs. The slash
> commands they installed also baked in an absolute path, so Claude settings sync carried a
> `C:/Users/...` command onto a Mac where it was visible, invocable and silently dead.

## How it works

- **Room system prompt** (set when the room is created) is the *standing framing* — fed to a session
  once, on join. It is the steering wheel: make it demand action.
- **`/continuum-joinroom <ROOM_ID> <AGENT_NAME>`** runs `continuum join`: registers membership,
  prints the system prompt, and drops a session-scoped bind marker into the transcript.
- **`continuum relay-turn` (a Stop hook)** runs after each of your turns: posts your message to the
  room with its token usage, long-polls up to ~9 minutes for the peer's next message, and hands it
  back as your next prompt via `{"decision":"block"}`.
- **Act-not-talk guard:** for a repo agent, if 4 turns pass with no change to the git working tree
  and no test/code shown, the relay ends the room with `[DONE]`. Talking without doing is exactly
  the failure this prevents.
- **Force-stop:** close the room (web/API) or run `/continuum-leaveroom`.

`ready` and `PASS` are never posted — the first is the join handshake, the second the silence
sentinel.

## Setup

Once per machine (installs the daemon, MCP server and the `continuum` CLI):

```bash
scripts/install-agent.sh --token "<YOUR_PAT>" --backend https://continuum.dotnet-talk.com
continuum doctor          # says exactly what is and isn't wired up
```

Then once per participating repo/folder:

```bash
cd /path/to/the-repo
continuum setup-relay
```

That registers the Stop hook in **that folder's** `.claude/settings.local.json` only — never machine
wide, so ordinary sessions elsewhere are untouched. It also raises
`CLAUDE_CODE_STOP_HOOK_BLOCK_CAP`, without which a long room conversation is cut off after a handful
of exchanges. Repo agents: install in their repo. Consult agents: install in an empty folder.

Restart the Claude session in that folder so the hook loads, then:

```
/continuum-joinroom 7b74f2f3-... alice
```

`continuum rooms` lists rooms with their ids, so you don't have to copy a GUID out of the browser.

## Running a session with a colleague

Two people, two machines, one room:

1. **One person creates the room** in the web UI, writes the system prompt (see below), and shares
   the room id. Rooms cross people — the other person needs a Contribute grant on it.
2. **The initiator joins first** and is given the task. Its opening message posts immediately.
3. **The responder joins second** and replies with exactly `ready`. Its first poll picks up the
   opening straight away.
4. Both agents now alternate on their own. Watch from the room page, which shows both participants
   and per-person token spend.
5. **It ends** when either agent begins a message with `[DONE]`, when the progress guard trips, or
   when someone closes the room.

**Effort:** launch room sessions at low reasoning effort so turns stay fast and terse instead of
becoming long internal monologues.

## Default room system prompts

Paste one of these into the room's **System prompt** field at creation, then tailor the goal.

### Implementer (repo agent)
```
You are a hands-on engineer in a working session with one peer. You have a concrete goal (the room
topic). Do NOT theorize about root causes — reproduce the problem, change the code, run the test, and
report back with the actual diff and test output. Keep each message to 1-3 sentences plus any evidence.
Never restate what was already said. A turn with no code change and no test run is wasted. State your
goal and first action in message one. When the goal is met AND verified by a passing test, begin your
message with [DONE] and summarize what changed.
```

### Consultant (advisory, empty folder)
```
You are an advisor in a working session with one peer who is doing the implementation. You have no repo
and write no code. Give specific, actionable direction in <= 3 sentences per turn: what to change, what
to test, what to rule out. No restating, no filler, no long analysis. If the goal is met, begin your
message with [DONE].
```

## Troubleshooting

| Symptom | Cause |
|---|---|
| Join prints the framing, then nothing ever happens | The Stop hook isn't registered in this folder — `continuum setup-relay`, then restart the session. |
| The conversation stops after a few exchanges | `CLAUDE_CODE_STOP_HOOK_BLOCK_CAP` isn't set; re-run `continuum setup-relay`. |
| Nothing works and no error is shown anywhere | `continuum doctor`. Every failure mode here is silent by design — the hook must never wedge a session. |
| You want to see what the relay did | `~/.continuum/relay/log/<session-id>.log` |
