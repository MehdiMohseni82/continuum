#!/usr/bin/env bash
#
# Deploy Continuum to the server.
#
#   infra/deploy.sh                 # deploy origin/main
#   infra/deploy.sh <git-ref>       # deploy some other ref
#
# Why this exists rather than a one-line `git archive | ssh tar x`:
# tar OVERLAYS files onto what is already there and has no way to remove a file that was deleted
# upstream. That was fine for a year, then a refactor moved ~20 files between projects and every
# stale copy stayed behind — the server ended up compiling two definitions of the same types and a
# stale constants file shadowing the new one. Two failed deploys in one afternoon.
#
# So this replaces the tracked tree outright, carrying infra/.env across, which is the only thing on
# the server that is not in git.
set -euo pipefail

REF="${1:-origin/main}"
SERVER="${CONTINUUM_SERVER:-root@152.53.226.44}"
SSH_KEY="${CONTINUUM_SSH_KEY:-$HOME/.ssh/private_key_no_pass}"
REMOTE_DIR="${CONTINUUM_DIR:-/opt/continuum}"


repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

command -v git >/dev/null || { echo "git not found" >&2; exit 1; }
[[ -f "$SSH_KEY" ]] || { echo "SSH key not found: $SSH_KEY" >&2; exit 1; }

git rev-parse --verify --quiet "$REF" >/dev/null || { echo "Unknown git ref: $REF" >&2; exit 1; }
sha="$(git rev-parse --short "$REF")"

echo "Continuum deploy"
echo "  ref:     $REF ($sha)"
echo "  server:  $SERVER:$REMOTE_DIR"
echo

archive="$(mktemp -t continuum-deploy-XXXXXX).tar.gz"
trap 'rm -f "$archive"' EXIT
git archive --format=tar.gz -o "$archive" "$REF"
echo "Built archive ($(du -h "$archive" | cut -f1)) — uploading..."
scp -q -i "$SSH_KEY" -o ConnectTimeout=20 "$archive" "$SERVER:/tmp/continuum-deploy.tar.gz"

# The remote half runs as one script so a failure can't leave the tree half-replaced. The target
# directory arrives as a positional argument: ssh joins its arguments into one command string, so
# `VAR=value ssh-command` would be re-parsed by the remote shell rather than set as an environment.
ssh -i "$SSH_KEY" -o ConnectTimeout=20 "$SERVER" "bash -s -- $(printf %q "$REMOTE_DIR")" <<'REMOTE'
set -euo pipefail
REMOTE_DIR="$1"
COMPOSE=(docker compose -f docker-compose.server.yml -p continuum)
cd "$REMOTE_DIR"

# Refuse to wipe a directory that isn't a Continuum checkout.
[[ -f infra/docker-compose.server.yml ]] || { echo "ERROR: $PWD does not look like the Continuum tree." >&2; exit 1; }

env_backup=""
if [[ -f infra/.env ]]; then
  env_backup="$(mktemp)"
  cp infra/.env "$env_backup"
  echo "  preserved infra/.env ($(wc -l < infra/.env) lines)"
else
  echo "  WARNING: no infra/.env found — the stack will start without its secrets." >&2
fi

# Replace the tracked tree, so files deleted upstream actually disappear. infra/ is emptied of its
# tracked contents too, but the directory itself stays so the .env backup has somewhere to land.
find . -mindepth 1 -maxdepth 1 ! -name infra -exec rm -rf {} +
find infra -mindepth 1 -maxdepth 1 ! -name .env -exec rm -rf {} +

tar xzf /tmp/continuum-deploy.tar.gz
rm -f /tmp/continuum-deploy.tar.gz

if [[ -n "$env_backup" ]]; then
  cp "$env_backup" infra/.env
  rm -f "$env_backup"
fi
[[ -s infra/.env ]] || echo "  WARNING: infra/.env is missing or empty after extraction." >&2

echo "  tree replaced; rebuilding..."
cd infra
"${COMPOSE[@]}" up -d --build
REMOTE

echo
echo "Deployed $sha. Migrations apply on host boot — check they landed:"
echo "  ssh -i $SSH_KEY $SERVER 'docker exec continuum-db-1 psql -U continuum -d continuum -t -c \"SELECT \\\"MigrationId\\\" FROM \\\"__EFMigrationsHistory\\\" ORDER BY 1 DESC LIMIT 1;\"'"
