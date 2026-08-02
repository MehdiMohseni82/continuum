# Continuum room relay

Wire two (or more) **interactive** Claude Code sessions into one room so they talk automatically — you
type one command, then watch the discussion. No copy-paste, no headless `claude -p` spawning: each
participant is a real session you can see, with full context and tools.

## How it works

- **Room system prompt** (set when the room is created) is the *standing framing* — fed to a session
  once, on join. It is the steering wheel: make it demand action.
- **`/continuum-joinroom <ROOM_ID> <AGENT_NAME>`** joins this session, prints the system prompt, and
  drops a session-scoped marker into the transcript.
- **`room-relay.ps1` (a Stop hook)** runs after each of your turns: posts your message to the room,
  long-polls for the peer's next message, and hands it back as your next prompt.
- **Act-not-talk guard:** for a repo agent, if 4 turns pass with no change to the git working tree and
  no test/code shown, the relay stops the room with `[DONE] no progress`. Talking without doing is
  exactly the failure this prevents.
- **Force-stop:** close the room (web/API) or run `/continuum-leaveroom`.

## Install (once per participating repo/folder)

```powershell
pwsh -File hooks\relay\install-room-relay.ps1 -RepoPath D:\path\to\the-repo
```

This registers the Stop hook in that repo's `.claude/settings.local.json` **only** — never machine-wide.
Repo agents: install in their repo. Consult agents: install in an empty folder and run there.

Then start a Claude session **in that folder** (restart if it was already open, so the hook loads), and:

```
/continuum-joinroom 7b74f2f3-... alice
```

**Kickoff order:** set up the *initiator* first and give it the task (it posts the opening), then join
the *responder* (its first poll picks up the opening immediately). Run `hooks\room-follow.ps1` in a side
terminal to watch the whole room.

**Effort:** launch room sessions at low reasoning effort (`/config` or the effort selector) so turns stay
fast and terse instead of long internal monologues.

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

## Files

| File | Role |
|---|---|
| `room-join.ps1` | Slash-command helper: join + feed system prompt + drop bind marker |
| `room-leave.ps1` | Slash-command helper: unbind this session |
| `room-relay.ps1` | The Stop hook: post → poll → deliver peer message; progress guard |
| `install-room-relay.ps1` | Per-repo installer (scripts + commands + local hook) |
