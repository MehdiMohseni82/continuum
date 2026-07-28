using System.Text;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>
/// Builds a daily rollup of activity and posts it to the "digest" bus channel, where any agent
/// (or the UI) can read it. Self-contained: no email/Slack, so no external secrets required.
/// </summary>
public sealed class DigestService(ContinuumDbContext db, BusService bus)
{
    public const string Channel = "digest";
    public const string Author = "continuum";

    public async Task<DigestDto> BuildAsync(DateTimeOffset since, CancellationToken ct)
    {
        var sessions = await db.Sessions.CountAsync(s => s.LastEventAt >= since, ct);
        var events = await db.Events.CountAsync(e => e.Timestamp >= since, ct);
        var memories = await db.Memories.CountAsync(m => m.CreatedAt >= since, ct);

        // Count from the Workspaces side (correlated subquery) — mirrors AnalyticsService, which
        // EF translates reliably; a GroupBy over the Session→Workspace join does not.
        var topWorkspaces = (await db.Workspaces
            .Select(w => new CountByLabel(w.DisplayName, w.Sessions.Count(s => s.LastEventAt >= since)))
            .ToListAsync(ct))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var sb = new StringBuilder();
        sb.Append("**Continuum daily digest** — ").Append(now.ToString("yyyy-MM-dd")).Append('\n');
        sb.Append($"- {sessions} session(s) active, {events} event(s) captured\n");
        sb.Append($"- {memories} new memory(ies) learned\n");
        if (topWorkspaces.Count > 0)
        {
            sb.Append("- Busiest: ");
            sb.Append(string.Join(", ", topWorkspaces.Select(w => $"{w.Label} ({w.Count})")));
            sb.Append('\n');
        }

        return new DigestDto(now, sessions, events, memories, topWorkspaces, sb.ToString().TrimEnd());
    }

    /// <summary>Build the last-24h digest and post it to the channel. Returns the posted digest.</summary>
    public async Task<DigestDto> PostDailyAsync(CancellationToken ct)
    {
        var digest = await BuildAsync(DateTimeOffset.UtcNow.AddHours(-24), ct);
        await bus.PostChannelAsync(new ChannelPostRequest(Author, Channel, digest.Markdown), ct);
        return digest;
    }

    /// <summary>The most recent digest message, or null if none posted yet.</summary>
    public async Task<MessageDto?> LatestAsync(CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Name == Channel, ct);
        if (channel is null) return null;

        return await db.AgentMessages
            .Where(m => m.ChannelId == channel.Id)
            .OrderByDescending(m => m.Id)
            .Select(m => new MessageDto(m.Id, m.FromAgent!.Name, null, Channel, m.Body, m.CreatedAt))
            .FirstOrDefaultAsync(ct);
    }
}
