# Continuum hooks + MCP registration

Two ways Claude Code plugs into Continuum's Phase 2 brain: **hooks** (automatic context
injection + checkpointing) and the **MCP server** (tools Claude calls on purpose).

## 1. MCP server

Register the C# MCP server so Claude Code can call `memory_save`, `memory_search`,
`memory_list`, `memory_forget`, `context_checkpoint`, and `history_search`.

Copy `.mcp.json.example` (repo root) to `.mcp.json` in a project, or add via CLI:

```bash
claude mcp add --transport stdio continuum \
  --env CONTINUUM_BACKEND=http://localhost:5000 \
  --env CONTINUUM_TOKEN=your-token \
  -- dotnet run --project /abs/path/to/src/Continuum.Mcp
```

For speed, publish once and point at the DLL instead of `dotnet run`:

```bash
dotnet publish src/Continuum.Mcp -c Release -o ./mcp-publish
# then command: dotnet, args: ["/abs/path/mcp-publish/Continuum.Mcp.dll"]
```

## 2. Hooks

Add to `~/.claude/settings.json` (or a project `.claude/settings.json`). Requires `bash`,
`curl`, `jq` on PATH (Git Bash on Windows). Set `CONTINUUM_BACKEND` / `CONTINUUM_TOKEN` in
your environment so the scripts can reach the backend.

```json
{
  "hooks": {
    "SessionStart": [
      { "matcher": "*", "hooks": [
        { "type": "command", "command": "/abs/path/to/hooks/session-start.sh" } ] }
    ],
    "PreCompact": [
      { "matcher": "*", "hooks": [
        { "type": "command", "command": "/abs/path/to/hooks/pre-compact.sh" } ] }
    ]
  }
}
```

- **SessionStart** → injects your saved memories + the latest checkpoint for the project.
- **PreCompact** → snapshots the transcript tail as a checkpoint before compaction.

Make the scripts executable: `chmod +x hooks/*.sh`.

## 3. One workspace per repo, not per machine

A session's memory and history are filed under a *project key*. By default that key is the Claude
Code project directory name — your cwd with every non-alphanumeric character replaced by a dash — so
the same repo produces `-Users-you-dev-Foo` on a Mac and `D--projects-Foo` on Windows. Two keys, two
workspaces, two memories that never meet.

Declare the key instead, and commit it:

```bash
continuum project              # what this folder uses today
continuum project set your-org/your-repo
```

That writes `.continuum-project` at the repo root **and** re-keys the existing workspace on the
backend, so the history you already have moves with you rather than being stranded under the old
key. Commit the file and every other machine that checks the repo out joins the same workspace.

Re-keying is admin-only. If `continuum project set` reports that, the marker is still written — ask
an admin to `PATCH /api/workspaces/{id}/project-key` for the move.
