# Continuum

[![CI](https://github.com/MehdiMohseni82/continuum/actions/workflows/ci.yml/badge.svg)](https://github.com/MehdiMohseni82/continuum/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

An external brain and sync backbone for Claude Code. It captures every session, carries
context between machines, gives Claude a **durable memory** it can't forget, and lets
parallel agents **talk to each other** — with an API, a Blazor UI, and an MCP server on top.

All four phases are implemented and verified end-to-end:

| Phase | What it adds |
|---|---|
| **0 — Archive** | Crash-proof capture of every session; unified history; full-text search |
| **1 — Cross-machine** | Reconstruct a session's transcript to `--resume` elsewhere; markdown hand-off bundles |
| **2 — The brain** | MCP server + durable memory (pgvector), checkpoints, secret redaction, SessionStart/PreCompact hooks |
| **3 — The bus** | Agent registration, direct messages, channels, task hand-offs; live UI |
| **4 — Polish** | Memory decay/dedupe/prune, analytics dashboard, redaction review, opt-in retention |

## Layout

| Project | Role |
|---|---|
| `src/Continuum.Core` | Entities, EF Core `DbContext`, migrations, tolerant JSONL parser, embedders, redaction |
| `src/Continuum.Host` | ASP.NET Core: ingest/query/memory/bus API **and** the Blazor Server UI |
| `src/Continuum.Daemon` | Worker service: tails `~/.claude/projects/**/*.jsonl`, uploads, resumable cursors |
| `src/Continuum.Mcp` | C# MCP server (stdio) exposing 15 tools to Claude Code |
| `tests/Continuum.Tests` | Parser, redaction, embedding, and hand-off tests |
| `hooks/` | `session-start.sh`, `pre-compact.sh` + registration docs |

## Design decisions (locked)

- **Stack:** all C# / .NET 9, Blazor Server, `ModelContextProtocol` C# SDK.
- **Deploy:** single-user, self-hosted, fully containerized. Team support is a later flip
  (every `Workspace`/`Agent`/`MemoryItem` already carries an `OwnerId`).
- **DB:** Postgres + pgvector (HNSW cosine index on memory embeddings).
- **Format drift:** every event stores its raw JSON (`jsonb`) + a version tag, so an
  unrecognized line from a newer Claude Code still lands intact and searchable.
- **Safety:** secrets are redacted before memory is stored/embedded; destructive maintenance
  (dedupe, prune, retention) is **never** automatic — only decay runs on a schedule.

## Embeddings — self-hosted, open-source

Semantic memory uses a **self-hosted open-source model via Ollama** by default
(`nomic-embed-text`, 768-dim) — **nothing leaves your network**, which matters because
transcripts hold IAM/DevOps secrets. The Docker stack includes an `ollama` service and pulls
the model automatically on first `up`.

Providers (`Embeddings:Provider`):
- **`ollama`** (default) — self-hosted; `Endpoint` is the Ollama base URL, no key.
- **`local`** — a no-dependency lexical fallback (deterministic, not truly semantic); use for
  tests or when you don't want to run Ollama.
- **`openai-compatible`** — opt-in external service; needs `ApiKey`.

To use a different Ollama model, set `EMBEDDING_MODEL` **and** make sure its width matches
`EmbeddingConfig.Dimensions` (currently 768) — pgvector columns are fixed-dimension, so a
different width needs that constant + a migration for `Memories.Embedding` updated together.

## Deploy

```bash
infra/deploy.sh              # deploy origin/main
infra/deploy.sh <git-ref>    # deploy something else
```

Replaces the tracked tree on the server (so files deleted upstream actually go away), carries
`infra/.env` across, and rebuilds. Migrations apply on host boot. Override the target with
`CONTINUUM_SERVER`, `CONTINUUM_SSH_KEY`, `CONTINUUM_DIR`.

## Run it (Docker)

```bash
cd infra
cp .env.example .env        # edit CLAUDE_DIR, CONTINUUM_TOKEN, MACHINE_NAME
docker compose up --build
```

- UI + API: <http://localhost:5000> — pages: History · Search · Memory · Agents · Analytics · Redaction
- The daemon mounts your `~/.claude` **read-only** and backfills immediately.
- Register the MCP server + hooks per `hooks/README.md`.

## Run it (local dev)

```bash
docker run -d --name continuum-db -p 5432:5432 \
  -e POSTGRES_DB=continuum -e POSTGRES_USER=continuum -e POSTGRES_PASSWORD=continuum \
  pgvector/pgvector:pg16
docker run -d --name continuum-ollama -p 11434:11434 ollama/ollama   # self-hosted embeddings
docker exec continuum-ollama ollama pull nomic-embed-text            # ~274 MB, once
dotnet run --project src/Continuum.Host      # http://localhost:5000  (applies migrations)
dotnet run --project src/Continuum.Daemon    # tails your real ~/.claude
dotnet test                                  # 22 tests

# Prefer not to run Ollama locally? Use the no-dependency fallback:
#   Embeddings__Provider=local dotnet run --project src/Continuum.Host
```

## MCP tools (15)

`memory_save` · `memory_search` · `memory_list` · `memory_forget` · `context_checkpoint`
· `history_search` · `agent_register` · `agent_list` · `bus_send` · `bus_inbox`
· `channel_post` · `channel_read` · `handoff_create` · `handoff_claim` · `handoff_list`

## API (all `/api/*` require `Authorization: Bearer <CONTINUUM_TOKEN>`)

Ingest & history: `POST /api/ingest/batch`, `GET /api/sessions`, `GET /api/sessions/{id}`,
`GET /api/search`, `GET /api/workspaces`, `GET /api/sessions/{id}/export.jsonl`, `…/bundle.md`.
Memory & context: `POST/GET/DELETE /api/memory`, `GET /api/memory/search`,
`POST /api/checkpoints`, `GET /api/context/session-start`.
Bus: `POST /api/agents/register`, `GET /api/agents`, `POST /api/bus/send`, `GET /api/bus/inbox`,
`POST/GET /api/bus/channel`, `POST /api/handoffs`, `POST /api/handoffs/{id}/claim`, `GET /api/handoffs`.
Ops: `GET /api/analytics`, `GET /api/redaction/scan`, `POST /api/maintenance/{decay,dedupe,prune,retention}`.
