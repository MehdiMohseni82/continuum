#!/usr/bin/env bash
# Continuum PreCompact hook: snapshots the tail of the transcript as a checkpoint before the
# context window compacts, so key recent state survives. For a curated snapshot, have the model
# call the context_checkpoint MCP tool instead — this is the automatic safety net.
# Requires: bash, curl, jq.
set -euo pipefail

INPUT=$(cat)
SESSION_ID=$(printf '%s' "$INPUT" | jq -r '.session_id // empty')
TRANSCRIPT=$(printf '%s' "$INPUT" | jq -r '.transcript_path // empty')
[ -z "$SESSION_ID" ] && exit 0

BACKEND="${CONTINUUM_BACKEND:-http://localhost:5000}"
TOKEN="${CONTINUUM_TOKEN:-dev-local-token-change-me}"

# Pull the last few user/assistant text turns from the transcript, if available.
TAIL=""
if [ -n "$TRANSCRIPT" ] && [ -f "$TRANSCRIPT" ]; then
  TAIL=$(tail -n 60 "$TRANSCRIPT" \
    | jq -r 'select(.message.content != null)
             | (.message.role // "?") as $r
             | (if (.message.content | type) == "string" then .message.content
                else ([.message.content[]? | .text // empty] | join(" ")) end) as $t
             | select($t != "") | "- **\($r)**: \($t[0:400])"' 2>/dev/null | tail -n 12 || true)
fi

CONTENT=$(printf '## Auto checkpoint (pre-compact)\n\n%s' "${TAIL:-"(no transcript tail available)"}")

jq -n --arg s "$SESSION_ID" --arg c "$CONTENT" '{sessionId: $s, content: $c, reason: "pre-compact"}' \
  | curl -s -X POST "$BACKEND/api/checkpoints" \
      -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
      -d @- >/dev/null 2>&1 || true

exit 0
