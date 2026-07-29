using Continuum.Core.Contracts;
using Continuum.Core.Domain;
using Continuum.Host.Auth;
using Continuum.Host.Services;

namespace Continuum.Host.Api;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapContinuumApi(this IEndpointRouteBuilder app, AuthOptions authOptions)
    {
        // Public (unauthenticated) endpoints — login.
        app.MapPublicAuthApi(authOptions);

        var api = app.MapGroup("/api")
            .AddEndpointFilter<AuthFilter>()
            .DisableAntiforgery(); // JSON API, not browser forms

        // Authenticated auth + account-management endpoints.
        api.MapAuthApi(authOptions);

        // --- ingest (daemon → server) ---
        api.MapPost("/ingest/batch", async (IngestBatch batch, IngestService ingest, CancellationToken ct) =>
            Results.Ok(await ingest.IngestAsync(batch, ct)));

        api.MapPost("/machines/heartbeat", (HeartbeatRequest _) => Results.NoContent());

        // --- query (UI / tools → server) ---
        api.MapGet("/workspaces", async (HistoryService history, CancellationToken ct) =>
            Results.Ok(await history.WorkspacesAsync(ct)));

        api.MapGet("/sessions", async (
            HistoryService history, CancellationToken ct,
            Guid? workspaceId, string? q, SessionStatus? status, int skip = 0, int take = 50) =>
            Results.Ok(await history.SessionsAsync(workspaceId, q, status, skip, Math.Clamp(take, 1, 200), ct)));

        api.MapGet("/sessions/{id:guid}", async (
            Guid id, HistoryService history, CancellationToken ct, int skip = 0, int take = 200) =>
        {
            var detail = await history.SessionAsync(id, skip, Math.Clamp(take, 1, 1000), ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        api.MapGet("/search", async (
            string q, HistoryService history, CancellationToken ct,
            Guid? workspaceId, string? type, int? sinceDays, int take = 50) =>
            Results.Ok(await history.SearchAsync(q, Math.Clamp(take, 1, 200), ct, workspaceId, type, sinceDays)));

        // Semantic session search (over session summaries).
        api.MapGet("/sessions/semantic", async (string q, HistoryService history, CancellationToken ct, int take = 30) =>
            Results.Ok(await history.SemanticSessionsAsync(q, Math.Clamp(take, 1, 100), ct)));

        // Opt-in sharing: owner (or admin) makes a session visible to everyone.
        api.MapPatch("/sessions/{id:guid}/share", async (Guid id, ShareRequest req, HistoryService history, CancellationToken ct) =>
            await history.SetSharedAsync(id, req.Shared, ct) ? Results.NoContent() : Results.NotFound());

        // Ask my history — RAG over memories + transcripts (Feature 2).
        api.MapPost("/ask", async (AskRequest req, RagService rag, CancellationToken ct) =>
            Results.Ok(await rag.AskAsync(req.Question, ct)));

        // --- cross-machine resume (Phase 1) ---
        api.MapGet("/sessions/{id:guid}/export.jsonl", async (Guid id, ResumeService resume, CancellationToken ct) =>
        {
            var jsonl = await resume.ExportJsonlAsync(id, ct);
            return jsonl is null
                ? Results.NotFound()
                : Results.Text(jsonl, "application/x-ndjson", System.Text.Encoding.UTF8);
        });

        api.MapGet("/sessions/{id:guid}/bundle.md", async (Guid id, ResumeService resume, CancellationToken ct) =>
        {
            var md = await resume.BundleMarkdownAsync(id, ct);
            return md is null
                ? Results.NotFound()
                : Results.Text(md, "text/markdown", System.Text.Encoding.UTF8);
        });

        // --- durable memory (Phase 2) ---
        api.MapPost("/memory", async (MemorySaveRequest req, MemoryService mem, CancellationToken ct) =>
            Results.Ok(await mem.SaveAsync(req, ct)));

        api.MapGet("/memory/search", async (
            string q, MemoryService mem, CancellationToken ct, Guid? workspaceId, int take = 8) =>
            Results.Ok(await mem.SearchAsync(q, workspaceId, Math.Clamp(take, 1, 50), ct)));

        api.MapGet("/memory", async (
            MemoryService mem, CancellationToken ct, Guid? workspaceId, MemoryType? type, int take = 50) =>
            Results.Ok(await mem.ListAsync(workspaceId, type, Math.Clamp(take, 1, 200), ct)));

        api.MapPatch("/memory/{id:guid}", async (Guid id, MemoryUpdateRequest req, MemoryService mem, CancellationToken ct) =>
        {
            var updated = await mem.UpdateAsync(id, req, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        api.MapDelete("/memory/{id:guid}", async (Guid id, MemoryService mem, CancellationToken ct) =>
            await mem.ForgetAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // Auto-memory extraction — manual trigger for one session (the worker does this on a schedule).
        api.MapPost("/memory/extract/{sessionId:guid}", async (Guid sessionId, MemoryExtractionService ex, CancellationToken ct) =>
            Results.Ok(new { extracted = await ex.ExtractAsync(sessionId, ct) }));

        // --- context checkpoints (Phase 2) ---
        api.MapPost("/checkpoints", async (CheckpointRequest req, CheckpointService cp, CancellationToken ct) =>
            Results.Ok(await cp.CreateAsync(req, ct)));

        api.MapGet("/checkpoints/session/{sessionId:guid}/latest", async (
            Guid sessionId, CheckpointService cp, CancellationToken ct) =>
        {
            var latest = await cp.LatestForSessionAsync(sessionId, ct);
            return latest is null ? Results.NotFound() : Results.Ok(latest);
        });

        // --- hook context injection (Phase 2) ---
        api.MapGet("/context/session-start", async (
            HookContextService hook, CancellationToken ct, string? projectKey, int maxMemories = 12) =>
            Results.Ok(new ContextInjection(await hook.BuildSessionStartAsync(projectKey, Math.Clamp(maxMemories, 1, 50), ct))));

        // --- inter-agent bus (Phase 3) ---
        api.MapPost("/agents/register", async (RegisterAgentRequest req, BusService bus, CancellationToken ct) =>
            Results.Ok(await bus.RegisterAsync(req, ct)));

        api.MapGet("/agents", async (BusService bus, CancellationToken ct) =>
            Results.Ok(await bus.ListAgentsAsync(ct)));

        api.MapPost("/bus/send", async (SendMessageRequest req, BusService bus, CancellationToken ct) =>
            Results.Ok(await bus.SendDirectAsync(req, ct)));

        api.MapGet("/bus/inbox", async (
            string agent, BusService bus, CancellationToken ct, bool unreadOnly = true, bool markRead = true) =>
            Results.Ok(await bus.InboxAsync(agent, unreadOnly, markRead, ct)));

        api.MapPost("/bus/channel", async (ChannelPostRequest req, BusService bus, CancellationToken ct) =>
            Results.Ok(await bus.PostChannelAsync(req, ct)));

        api.MapGet("/bus/channel", async (
            string channel, BusService bus, CancellationToken ct, long since = 0, int take = 100) =>
            Results.Ok(await bus.ReadChannelAsync(channel, since, Math.Clamp(take, 1, 500), ct)));

        api.MapPost("/handoffs", async (HandoffRequest req, BusService bus, CancellationToken ct) =>
            Results.Ok(await bus.CreateHandoffAsync(req, ct)));

        api.MapPost("/handoffs/{id:guid}/claim", async (
            Guid id, ClaimHandoffRequest req, BusService bus, CancellationToken ct) =>
        {
            var claimed = await bus.ClaimHandoffAsync(id, req.ByAgent, ct);
            return claimed is null ? Results.Conflict("Handoff not open.") : Results.Ok(claimed);
        });

        api.MapGet("/handoffs", async (BusService bus, CancellationToken ct, string? status) =>
            Results.Ok(await bus.ListHandoffsAsync(status, ct)));

        // --- rooms: admin-controlled group conversations (Phase 8) ---
        api.MapPost("/rooms", async (CreateRoomRequest req, ICurrentUser me, RoomService rooms, CancellationToken ct) =>
        {
            if (!me.IsAdmin) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Topic))
                return Results.BadRequest("Name and topic are required.");
            return Results.Ok(await rooms.CreateAsync(req, ct));
        });

        api.MapGet("/rooms", async (RoomService rooms, CancellationToken ct) =>
            Results.Ok(await rooms.ListAsync(ct)));

        api.MapGet("/rooms/{id:guid}", async (Guid id, RoomService rooms, CancellationToken ct, int take = 200) =>
        {
            var detail = await rooms.GetAsync(id, Math.Clamp(take, 1, 1000), ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        api.MapGet("/rooms/{id:guid}/messages", async (
            Guid id, RoomService rooms, CancellationToken ct, long since = 0, int take = 200) =>
            Results.Ok(await rooms.MessagesAsync(id, since, Math.Clamp(take, 1, 500), ct)));

        api.MapPost("/rooms/{id:guid}/members", async (Guid id, AddMemberRequest req, ICurrentUser me, RoomService rooms, CancellationToken ct) =>
        {
            if (!me.IsAdmin) return Results.Forbid();
            return await rooms.AddMemberAsync(id, req.Agent.Trim(), ct) ? Results.NoContent() : Results.NotFound();
        });

        api.MapDelete("/rooms/{id:guid}/members/{agent}", async (Guid id, string agent, ICurrentUser me, RoomService rooms, CancellationToken ct) =>
        {
            if (!me.IsAdmin) return Results.Forbid();
            return await rooms.RemoveMemberAsync(id, agent, ct) ? Results.NoContent() : Results.NotFound();
        });

        api.MapPost("/rooms/{id:guid}/close", async (Guid id, ICurrentUser me, RoomService rooms, CancellationToken ct) =>
        {
            if (!me.IsAdmin) return Results.Forbid();
            return await rooms.CloseAsync(id, ct) ? Results.NoContent() : Results.NotFound();
        });

        // Agents post here (or via channel_post to the room's channel). Rejected once the room is closed.
        api.MapPost("/rooms/{id:guid}/post", async (Guid id, RoomPostRequest req, RoomService rooms, CancellationToken ct) =>
        {
            var msg = await rooms.PostAsync(id, req.FromAgent.Trim(), req.Body, ct);
            return msg is null ? Results.Conflict("Room not found or closed.") : Results.Ok(msg);
        });

        // --- header activity feed (bus messages + hand-offs) ---
        api.MapGet("/notifications", async (NotificationsService n, CancellationToken ct, int take = 20) =>
            Results.Ok(await n.RecentAsync(Math.Clamp(take, 1, 100), ct)));

        // --- analytics + maintenance (Phase 4) ---
        api.MapGet("/analytics", async (AnalyticsService a, CancellationToken ct) =>
            Results.Ok(await a.GetAsync(ct)));

        api.MapGet("/analytics/tokens", async (TokenAnalyticsService t, CancellationToken ct) =>
            Results.Ok(await t.GetAsync(ct)));

        api.MapGet("/redaction/scan", async (RedactionReviewService r, CancellationToken ct, int scanLimit = 5000) =>
            Results.Ok(await r.ScanAsync(Math.Clamp(scanLimit, 100, 50000), ct)));

        // --- ops: daily digest + backup status (Phase 6) ---
        api.MapGet("/digest/latest", async (DigestService d, CancellationToken ct) =>
        {
            var latest = await d.LatestAsync(ct);
            return latest is null ? Results.NoContent() : Results.Ok(latest);
        });

        // Build + post the digest now (the worker also does this daily).
        api.MapPost("/digest/run", async (DigestService d, CancellationToken ct) =>
            Results.Ok(await d.PostDailyAsync(ct)));

        api.MapGet("/backups", (BackupService b, int recent = 10) => Results.Ok(b.Status(recent)));

        api.MapPost("/maintenance/decay", async (MemoryMaintenanceService m, CancellationToken ct) =>
            Results.Ok(await m.DecayAsync(ct)));

        api.MapPost("/maintenance/dedupe", async (MemoryMaintenanceService m, CancellationToken ct, double threshold = 0.05) =>
            Results.Ok(await m.DedupeAsync(threshold, ct)));

        api.MapPost("/maintenance/prune", async (MemoryMaintenanceService m, CancellationToken ct) =>
            Results.Ok(await m.PruneAsync(ct)));

        // Destructive: requires an explicit day count; there is no default that deletes.
        api.MapPost("/maintenance/retention", async (RetentionService r, CancellationToken ct, int olderThanDays) =>
            Results.Ok(await r.PurgeOlderThanAsync(olderThanDays, ct)));

        // --- same-origin download links for the UI (no bearer; same trust boundary as the local UI) ---
        app.MapGet("/dl/{id:guid}/export.jsonl", async (Guid id, ResumeService resume, CancellationToken ct) =>
        {
            var jsonl = await resume.ExportJsonlAsync(id, ct);
            return jsonl is null ? Results.NotFound() : Results.File(
                System.Text.Encoding.UTF8.GetBytes(jsonl), "application/x-ndjson", $"{id}.jsonl");
        });

        app.MapGet("/dl/{id:guid}/bundle.md", async (Guid id, ResumeService resume, CancellationToken ct) =>
        {
            var md = await resume.BundleMarkdownAsync(id, ct);
            return md is null ? Results.NotFound() : Results.File(
                System.Text.Encoding.UTF8.GetBytes(md), "text/markdown", $"{id}-bundle.md");
        });

        return app;
    }
}

public sealed record HeartbeatRequest(string MachineName, string[] WatchedFiles);
