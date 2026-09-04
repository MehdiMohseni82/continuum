#!/usr/bin/env bash
# Installs the Continuum agent (daemon + MCP server) on macOS/Linux, pointed at a remote backend.
# The daemon now hosts the room runner too, so this one supervised process keeps history backfill
# AND room-driving alive — with self fail-over via launchd (macOS) KeepAlive.
#
# Usage:
#   scripts/install-agent.sh --token "<CONTINUUM_TOKEN>"
#   scripts/install-agent.sh --token "..." --backend "https://continuum.dotnet-talk.com" --machine "macbook"
#
# Requires: .NET 9 SDK (dotnet on PATH) and the `claude` CLI for MCP registration. No sudo needed.
set -euo pipefail

BACKEND="https://continuum.dotnet-talk.com"
MACHINE="$(hostname -s 2>/dev/null || hostname)"
TOKEN=""
INSTALL_DIR="$HOME/Continuum"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --token)    TOKEN="$2"; shift 2 ;;
    --backend)  BACKEND="$2"; shift 2 ;;
    --machine)  MACHINE="$2"; shift 2 ;;
    --install-dir) INSTALL_DIR="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done
[[ -n "$TOKEN" ]] || { echo "ERROR: --token is required." >&2; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
DAEMON_DIR="$INSTALL_DIR/daemon"
MCP_DIR="$INSTALL_DIR/mcp"
CLI_DIR="$HOME/.continuum/bin"
DOTNET="$(command -v dotnet || true)"
[[ -n "$DOTNET" ]] || { echo "ERROR: dotnet not found on PATH." >&2; exit 1; }

echo "Continuum agent install"
echo "  repo:    $REPO"
echo "  backend: $BACKEND"
echo "  machine: $MACHINE"
echo "  target:  $INSTALL_DIR"

IS_MAC=false
[[ "$(uname -s)" == "Darwin" ]] && IS_MAC=true

# 1) stop any running daemon so we can overwrite the binaries
pkill -f "Continuum.Daemon.dll" 2>/dev/null || true
if $IS_MAC; then launchctl unload "$HOME/Library/LaunchAgents/com.continuum.daemon.plist" 2>/dev/null || true; fi
sleep 2

# 2) publish daemon + mcp + the `continuum` CLI
echo "Publishing daemon + MCP + CLI..."
"$DOTNET" publish "$REPO/src/Continuum.Daemon" -c Release -o "$DAEMON_DIR" --nologo >/dev/null
"$DOTNET" publish "$REPO/src/Continuum.Mcp"    -c Release -o "$MCP_DIR"    --nologo >/dev/null
"$DOTNET" publish "$REPO/src/Continuum.Cli"    -c Release -o "$CLI_DIR"    --nologo >/dev/null

# 2b) One config file every Continuum piece can read. The hooks and the `continuum` CLI look here
# when CONTINUUM_BACKEND/CONTINUUM_TOKEN aren't exported — previously they silently fell back to
# localhost and swallowed the error, producing a session with no memory and no explanation.
mkdir -p "$HOME/.continuum"
cat > "$HOME/.continuum/config.json" <<JSON
{
  "backend": "$BACKEND",
  "token": "$TOKEN",
  "machine": "$MACHINE",
  "agent": "$MACHINE"
}
JSON
chmod 600 "$HOME/.continuum/config.json"

# 2c) Put `continuum` on PATH. The slash commands invoke it by bare name so they survive Claude
# settings sync across machines — a path-free command is the whole point, so PATH has to be real.
if ! command -v continuum >/dev/null 2>&1 || [ "$(command -v continuum)" != "$CLI_DIR/continuum" ]; then
  case "${SHELL:-}" in
    */zsh) RC="$HOME/.zshrc" ;;
    */bash) RC="$HOME/.bashrc" ;;
    *) RC="$HOME/.profile" ;;
  esac
  # Marker-guarded so re-running doesn't stack duplicate PATH entries.
  if ! grep -q '# continuum-cli' "$RC" 2>/dev/null; then
    printf '\nexport PATH="%s:$PATH"  # continuum-cli\n' "$CLI_DIR" >> "$RC"
    echo "  added $CLI_DIR to PATH in $RC (open a new terminal, or: source $RC)"
  fi
fi
export PATH="$CLI_DIR:$PATH"

# 3) production daemon config (overwrites the dev appsettings that publish copies in)
#
# MaxAutonomousTurns is 200 here, not the code default of 16. The cap closes a room once that many
# agent messages pass with no human speaking — a backstop against agents talking forever. At 16 it
# fired on real work: three agents held a productive seventeen-minute exchange and the runner closed
# the room under them. A backstop should catch a runaway, not interrupt a conversation.
mkdir -p "$DAEMON_DIR"
CURSOR="$DAEMON_DIR/continuum-cursors.db"
cat > "$DAEMON_DIR/appsettings.json" <<JSON
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.Hosting.Lifetime": "Information" } },
  "Daemon": {
    "BackendUrl": "$BACKEND",
    "Token": "$TOKEN",
    "MachineName": "$MACHINE",
    "PollSeconds": 10,
    "BatchSize": 500,
    "CursorDbPath": "$CURSOR",
    "RoomRunner": {
      "Enabled": true,
      "IntervalSeconds": 35,
      "ContextLines": 20,
      "MaxAutonomousTurns": 200
    }
  }
}
JSON
chmod 600 "$DAEMON_DIR/appsettings.json"

# The room runner reads this path unconditionally (DaemonOptions.cs hardcodes ~/Continuum/rooms,
# ignoring --install-dir), and nothing has ever created it. Seed it with every key it accepts —
# the old printed example omitted runtime/write/role, so users had to read C# to find them.
ROOMS_DIR="$HOME/Continuum/rooms"
mkdir -p "$ROOMS_DIR"
if [ ! -f "$ROOMS_DIR/agents.json" ]; then
  cat > "$ROOMS_DIR/agents.json" <<'JSON'
[
  {
    "name": "example-agent",
    "path": "/absolute/path/to/a/repo",
    "runtime": "claude",
    "write": false,
    "role": "consultant"
  }
]
JSON
  echo "  seeded $ROOMS_DIR/agents.json (edit it, then the daemon picks it up next cycle)"
fi

# 4) auto-start WITH self fail-over
DAEMON_DLL="$DAEMON_DIR/Continuum.Daemon.dll"
if $IS_MAC; then
  echo "Installing launchd agent (RunAtLoad + KeepAlive)..."
  PLIST="$HOME/Library/LaunchAgents/com.continuum.daemon.plist"
  mkdir -p "$HOME/Library/LaunchAgents"
  cat > "$PLIST" <<PLISTXML
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.continuum.daemon</string>
  <key>ProgramArguments</key>
  <array>
    <string>$DOTNET</string>
    <string>$DAEMON_DLL</string>
  </array>
  <key>WorkingDirectory</key><string>$DAEMON_DIR</string>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>StandardOutPath</key><string>$DAEMON_DIR/daemon.out.log</string>
  <key>StandardErrorPath</key><string>$DAEMON_DIR/daemon.err.log</string>
</dict>
</plist>
PLISTXML
  launchctl load -w "$PLIST"
else
  # Linux: a systemd user service with Restart=always gives the same self-healing.
  echo "Installing systemd user service (Restart=always)..."
  mkdir -p "$HOME/.config/systemd/user"
  cat > "$HOME/.config/systemd/user/continuum-daemon.service" <<UNIT
[Unit]
Description=Continuum agent daemon (history backfill + room runner)
After=network-online.target

[Service]
ExecStart=$DOTNET $DAEMON_DLL
WorkingDirectory=$DAEMON_DIR
Restart=always
RestartSec=5

[Install]
WantedBy=default.target
UNIT
  systemctl --user daemon-reload
  systemctl --user enable --now continuum-daemon.service
  loginctl enable-linger "$USER" 2>/dev/null || true
fi

# 5) register the MCP server at user scope (all projects)
echo "Registering MCP server..."
MCP_DLL="$MCP_DIR/Continuum.Mcp.dll"
# Guarded: this used to abort the whole script under `set -e` if the claude CLI was missing, after
# the daemon was already installed and before Codex/Cursor were wired — a half-installed machine.
if command -v claude >/dev/null 2>&1; then
  claude mcp remove continuum -s user >/dev/null 2>&1 || true
  if claude mcp add continuum --scope user -e CONTINUUM_BACKEND="$BACKEND" -e CONTINUUM_TOKEN="$TOKEN" -- dotnet "$MCP_DLL" >/dev/null 2>&1; then
    echo "  claude: registered"
  else
    echo "  claude: registration FAILED — run 'claude mcp add' by hand (see hooks/README.md)" >&2
  fi
else
  echo "  claude: CLI not on PATH — skipped (install it, then re-run this script)" >&2
fi

# 5c) wire the Continuum MCP into Codex + Cursor too, so those runtimes can join rooms as agents.
echo "Wiring Continuum MCP into Codex + Cursor..."
CODEX_CFG="$HOME/.codex/config.toml"
mkdir -p "$(dirname "$CODEX_CFG")"
# Idempotent: the old version appended only when the block was absent, so re-running with a new
# token left the stale one in place and the machine kept authenticating as whoever it was before.
if [ -f "$CODEX_CFG" ] && grep -q '^\[mcp_servers\.continuum\]' "$CODEX_CFG"; then
  # Drop the existing block (from its header to the next header or EOF), then re-append.
  awk '
    /^\[mcp_servers\.continuum\]/ { skip = 1; next }
    /^\[/ { skip = 0 }
    !skip { print }
  ' "$CODEX_CFG" > "$CODEX_CFG.tmp" && mv "$CODEX_CFG.tmp" "$CODEX_CFG"
fi
cat >> "$CODEX_CFG" <<TOML

[mcp_servers.continuum]
command = "dotnet"
args = ["$MCP_DLL"]
env = { CONTINUUM_BACKEND = "$BACKEND", CONTINUUM_TOKEN = "$TOKEN" }
TOML
chmod 600 "$CODEX_CFG"
CURSOR_CFG="$HOME/.cursor/mcp.json"
mkdir -p "$(dirname "$CURSOR_CFG")"
if command -v python3 >/dev/null 2>&1; then
  python3 - "$CURSOR_CFG" "$MCP_DLL" "$BACKEND" "$TOKEN" <<'PY'
import json, os, sys
path, dll, backend, token = sys.argv[1:5]
cfg = {}
if os.path.exists(path):
    try: cfg = json.load(open(path))
    except Exception: cfg = {}
cfg.setdefault("mcpServers", {})["continuum"] = {
    "command": "dotnet", "args": [dll],
    "env": {"CONTINUUM_BACKEND": backend, "CONTINUUM_TOKEN": token},
}
json.dump(cfg, open(path, "w"), indent=2)
PY
elif [ ! -f "$CURSOR_CFG" ]; then
  cat > "$CURSOR_CFG" <<JSON
{ "mcpServers": { "continuum": { "command": "dotnet", "args": ["$MCP_DLL"],
  "env": { "CONTINUUM_BACKEND": "$BACKEND", "CONTINUUM_TOKEN": "$TOKEN" } } } }
JSON
else
  echo "  (install python3 or edit $CURSOR_CFG to add the 'continuum' MCP server)"
fi
[ -f "$CURSOR_CFG" ] && chmod 600 "$CURSOR_CFG"

# 5d) Claude Code hooks — SessionStart (inject memory) and PreCompact (auto-checkpoint).
# This script never did this; registering them was a manual settings.json edit documented in
# hooks/README.md, which meant almost nobody had memory injected. Copies the scripts to a stable
# location so the settings entry doesn't point into a git clone that may move.
echo "Installing Claude Code hooks..."
HOOK_DIR="$HOME/.continuum/hooks"
mkdir -p "$HOOK_DIR"
cp "$REPO/hooks/session-start.sh" "$REPO/hooks/pre-compact.sh" "$HOOK_DIR/"
chmod +x "$HOOK_DIR"/*.sh

if command -v python3 >/dev/null 2>&1; then
  python3 - "$HOME/.claude/settings.json" "$HOOK_DIR" <<'PY'
import json, os, sys
path, hook_dir = sys.argv[1], sys.argv[2]
os.makedirs(os.path.dirname(path), exist_ok=True)

cfg = {}
if os.path.exists(path):
    try:
        cfg = json.load(open(path))
    except Exception:
        # Never destroy a settings file we can't parse — bail loudly instead.
        print("  settings.json is not valid JSON; skipped hook registration", file=sys.stderr)
        raise SystemExit(0)

hooks = cfg.setdefault("hooks", {})
for event, script in (("SessionStart", "session-start.sh"), ("PreCompact", "pre-compact.sh")):
    cmd = f"{hook_dir}/{script}"
    groups = hooks.setdefault(event, [])
    # Merge, never replace: the Windows installer uses Add-Member -Force here and wipes out every
    # hook the user had registered for the event. Drop only our own previous entry.
    groups = [g for g in groups
              if not any("continuum" in (h.get("command") or "") for h in g.get("hooks", []))]
    groups.append({"matcher": "*", "hooks": [{"type": "command", "command": cmd, "timeout": 20}]})
    hooks[event] = groups

json.dump(cfg, open(path, "w"), indent=2)
print("  registered SessionStart + PreCompact")
PY
else
  echo "  (no python3 — add SessionStart/PreCompact by hand, see hooks/README.md)" >&2
fi

# 6) verify
sleep 4
if pgrep -f "Continuum.Daemon.dll" >/dev/null; then
  echo "Daemon running; auto-starts at login and restarts on failure."
else
  echo "Daemon did not start - check $DAEMON_DIR/appsettings.json and try 'dotnet $DAEMON_DLL'." >&2
fi
echo "MCP 'continuum' registered (user scope). Start a NEW claude session to use it."
echo "Room runner is hosted inside the daemon — list local agents in $INSTALL_DIR/rooms/agents.json:"
echo '  [ { "name": "GeonoAI", "path": "/Users/you/proj/GeonoAI" } ]'
echo "Backfill of ~/.claude begins immediately; watch it at $BACKEND"
echo
echo "CLI installed: $CLI_DIR/continuum"
echo "  continuum doctor                 verify this machine end to end"
echo "  continuum setup-relay <repo>     enable room relaying for one repo (run once per repo)"
