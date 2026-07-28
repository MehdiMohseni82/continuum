using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>
/// Builds the header activity feed from the inter-agent bus — the deliberate, low-volume events
/// worth notifying on: direct/channel messages and task hand-offs. Merged and sorted newest-first.
/// </summary>
public sealed class NotificationsService(ContinuumDbContext db)
{
    public async Task<IReadOnlyList<NotificationDto>> RecentAsync(int take, CancellationToken ct)
    {
        var messages = await db.AgentMessages
            .OrderByDescending(m => m.Id)
            .Take(take)
            .Select(m => new
            {
                m.Id,
                m.Body,
                m.CreatedAt,
                From = m.FromAgent!.Name,
                To = m.ToAgentId == null ? null : db.Agents.Where(a => a.Id == m.ToAgentId).Select(a => a.Name).FirstOrDefault(),
                Channel = m.ChannelId == null ? null : db.Channels.Where(c => c.Id == m.ChannelId).Select(c => c.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var handoffs = await db.Handoffs
            .OrderByDescending(h => h.CreatedAt)
            .Take(take)
            .Select(h => new
            {
                h.Id,
                h.Title,
                h.Status,
                h.CreatedAt,
                From = h.FromAgent!.Name,
                Claimed = h.ClaimedByAgent == null ? null : h.ClaimedByAgent.Name,
            })
            .ToListAsync(ct);

        var msgNotes = messages.Select(m => new NotificationDto(
            $"msg-{m.Id}",
            "message",
            m.Channel != null ? $"{m.From} → #{m.Channel}" : $"{m.From} → {m.To ?? "?"}",
            m.Body.Length > 90 ? m.Body[..90] + "…" : m.Body,
            m.CreatedAt,
            "info"));

        var hoNotes = handoffs.Select(h => new NotificationDto(
            $"handoff-{h.Id}",
            "handoff",
            h.Status == "claimed" ? $"Hand-off claimed by {h.Claimed}" : $"New hand-off from {h.From}",
            h.Title,
            h.CreatedAt,
            h.Status == "open" ? "warning" : "info"));

        return msgNotes.Concat(hoNotes)
            .OrderByDescending(n => n.Timestamp)
            .Take(take)
            .ToList();
    }
}
