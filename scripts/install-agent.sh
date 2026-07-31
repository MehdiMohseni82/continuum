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

# 2) publish daemon + mcp
echo "Publishing daemon + MCP..."
"$DOTNET" publish "$REPO/src/Continuum.Daemon" -c Release -o "$DAEMON_DIR" --nologo >/dev/null
"$DOTNET" publish "$REPO/src/Continuum.Mcp"    -c Release -o "$MCP_DIR"    --nologo >/dev/null

# 3) production daemon config (overwrites the dev appsettings that publish copies in)
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
    "CursorDbPath": "$CURSOR"
  }
}
JSON

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
claude mcp remove continuum -s user >/dev/null 2>&1 || true
claude mcp add continuum --scope user -e CONTINUUM_BACKEND="$BACKEND" -e CONTINUUM_TOKEN="$TOKEN" -- dotnet "$MCP_DLL" >/dev/null

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
