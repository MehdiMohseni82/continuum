#!/usr/bin/env bash
# Continuum SessionStart hook: (1) registers this session on the inter-agent bus, and
# (2) injects remembered facts + the latest checkpoint + the session's bus identity.
# Requires: bash, curl, jq. On Windows use Git Bash.
# Configure via env: CONTINUUM_BACKEND (default http://localhost:5000), CONTINUUM_TOKEN.
set -euo pipefail

INPUT=$(cat)
CWD=$(printf '%s' "$INPUT" | jq -r '.cwd // empty')
SID=$(printf '%s' "$INPUT" | jq -r '.session_id // empty')

# Config: env first, then ~/.continuum/config.json written by the installer. Without that fallback
# an unexported CONTINUUM_BACKEND meant every call went to localhost and was swallowed by the
# best-effort error handling below — a session that looks fine but has no memory and no bus identity.
CONFIG="$HOME/.continuum/config.json"
BACKEND="${CONTINUUM_BACKEND:-}"
TOKEN="${CONTINUUM_TOKEN:-}"
if [ -f "$CONFIG" ]; then
  [ -z "$BACKEND" ] && BACKEND=$(jq -r '.backend // empty' "$CONFIG" 2>/dev/null || true)
  [ -z "$TOKEN" ] && TOKEN=$(jq -r '.token // empty' "$CONFIG" 2>/dev/null || true)
fi

# Unconfigured is a real state, not a default to guess at: emit nothing rather than talk to a
# localhost that isn't there.
if [ -z "$BACKEND" ] || [ -z "$TOKEN" ]; then
  exit 0
fi
AUTH="Authorization: Bearer $TOKEN"

# Claude Code names project dirs by replacing every non-alphanumeric char in the cwd with '-'.
PROJECT_KEY=$(printf '%s' "$CWD" | sed -E 's/[^A-Za-z0-9]/-/g')

# Stable per-project agent name: CONTINUUM_AGENT env -> .continuum-agent file -> folder name.
BASENAME=$(basename "$CWD" 2>/dev/null || echo session)
AGENT="${CONTINUUM_AGENT:-}"
if [ -z "$AGENT" ] && [ -n "$CWD" ] && [ -f "$CWD/.continuum-agent" ]; then
  AGENT=$(tr -d '\r\n' < "$CWD/.continuum-agent")
fi
[ -z "$AGENT" ] && AGENT="$BASENAME"

# 1) register this session as a bus agent (best-effort).
if [ -n "$SID" ]; then
  jq -n --arg n "$AGENT" --arg m "$(hostname)" --arg s "$SID" --arg c "project:$BASENAME" \
      '{name:$n, machineName:$m, currentSessionId:$s, capabilities:$c}' \
    | curl -s -X POST "$BACKEND/api/agents/register" -H "$AUTH" -H "Content-Type: application/json" -d @- >/dev/null 2>&1 || true
fi

# 2) pull remembered context. The `|| true` matters: under `set -euo pipefail` a failing curl (or a
# jq parse of an error page) aborts the whole hook, so a backend blip meant the session got NO
# injection at all — not even the bus identity below, which needs no network.
CTX=$(curl -s --max-time 10 -G "$BACKEND/api/context/session-start" \
  --data-urlencode "projectKey=$PROJECT_KEY" -H "$AUTH" 2>/dev/null | jq -r '.additionalContext // ""' 2>/dev/null || true)

# 3) compose the injection: memory context + this session's bus identity.
BUS_NOTE=$(printf '## Continuum bus\nYou are agent "%s" on the inter-agent bus. Tools: agent_list (see peers), bus_send / bus_inbox (direct messages), channel_post / channel_read (topics), handoff_create / handoff_claim (pass tasks). Check bus_inbox for messages addressed to you.' "$AGENT")
FULL=$(printf '%s\n\n%s' "$CTX" "$BUS_NOTE")

jq -n --arg c "$FULL" '{hookSpecificOutput: {hookEventName: "SessionStart", additionalContext: $c}}'
