using Continuum.Core.Access;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>The inter-agent bus: registration/presence, direct messages, channels, and task hand-offs.</summary>
public sealed class BusService(ContinuumDbContext db, BusBroadcaster bus, IAccessPolicy policy)
{
    // Names are resolved inside the caller's organization: two tenants may both have an "alpha".
    private Guid Org => policy.WriteOrgId;

    // ---- agents ----

    public async Task<AgentDto> RegisterAsync(RegisterAgentRequest req, CancellationToken ct)
    {
        var agent = await GetOrCreateAgentAsync(req.Name, ct);
        agent.MachineName = req.MachineName ?? agent.MachineName;
        agent.CurrentSessionId = req.CurrentSessionId ?? agent.CurrentSessionId;
        agent.Capabilities = req.Capabilities ?? agent.Capabilities;
        agent.LastSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var dto = ToDto(agent);
        bus.PublishAgent(dto);
        return dto;
    }

    public async Task<IReadOnlyList<AgentDto>> ListAgentsAsync(CancellationToken ct) =>
        await db.Agents.OrderByDescending(a => a.LastSeenAt).Select(a => ToDto(a)).ToListAsync(ct);

    // ---- direct messages ----

    public async Task<MessageDto> SendDirectAsync(SendMessageRequest req, CancellationToken ct)
    {
        var from = await GetOrCreateAgentAsync(req.FromAgent, ct);
        var to = await GetOrCreateAgentAsync(req.ToAgent, ct);

        var msg = new AgentMessage
        {
            FromAgentId = from.Id,
            ToAgentId = to.Id,
            Body = req.Body,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.AgentMessages.Add(msg);
        await db.SaveChangesAsync(ct);

        var dto = new MessageDto(msg.Id, from.Name, to.Name, null, msg.Body, msg.CreatedAt);
        bus.PublishMessage(dto);
        return dto;
    }

    public async Task<IReadOnlyList<MessageDto>> InboxAsync(string agentName, bool unreadOnly, bool markRead, CancellationToken ct)
    {
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.OrgId == Org && a.Name == agentName, ct);
        if (agent is null) return [];

        var q = db.AgentMessages.Where(m => m.ToAgentId == agent.Id);
        if (unreadOnly) q = q.Where(m => !m.Read);

        var messages = await q.OrderBy(m => m.Id)
            .Select(m => new { m, from = m.FromAgent!.Name })
            .ToListAsync(ct);

        if (markRead && messages.Count > 0)
        {
            foreach (var x in messages) x.m.Read = true;
            await db.SaveChangesAsync(ct);
        }

        return messages.Select(x => new MessageDto(x.m.Id, x.from, agentName, null, x.m.Body, x.m.CreatedAt)).ToList();
    }

    // ---- channels ----

    public async Task<MessageDto> PostChannelAsync(ChannelPostRequest req, CancellationToken ct)
    {
        var from = await GetOrCreateAgentAsync(req.FromAgent, ct);
        var channel = await EnsureChannelAsync(req.Channel, ct);

        var msg = new AgentMessage
        {
            FromAgentId = from.Id,
            ChannelId = channel.Id,
            Body = req.Body,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.AgentMessages.Add(msg);
        await db.SaveChangesAsync(ct);

        var dto = new MessageDto(msg.Id, from.Name, null, channel.Name, msg.Body, msg.CreatedAt);
        bus.PublishMessage(dto);
        return dto;
    }

    public async Task<IReadOnlyList<MessageDto>> ReadChannelAsync(string channelName, long sinceId, int take, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.OrgId == Org && c.Name == channelName, ct);
        if (channel is null) return [];

        return await db.AgentMessages
            .Where(m => m.ChannelId == channel.Id && m.Id > sinceId)
            .OrderBy(m => m.Id).Take(take)
            .Select(m => new MessageDto(m.Id, m.FromAgent!.Name, null, channelName, m.Body, m.CreatedAt,
                m.InputTokens, m.OutputTokens, m.CacheReadTokens, m.CacheCreationTokens, m.FromUserId, null))
            .ToListAsync(ct);
    }

    // ---- hand-offs ----

    public async Task<HandoffDto> CreateHandoffAsync(HandoffRequest req, CancellationToken ct)
    {
        var from = await GetOrCreateAgentAsync(req.FromAgent, ct);
        var h = new Handoff
        {
            Id = Guid.NewGuid(),
            FromAgentId = from.Id,
            WorkspaceId = req.WorkspaceId,
            Title = req.Title,
            Task = req.Task,
            ContextRef = req.ContextRef,
            Status = "open",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Handoffs.Add(h);
        await db.SaveChangesAsync(ct);

        var dto = await LoadHandoffAsync(h.Id, ct);
        bus.PublishHandoff(dto!);
        return dto!;
    }

    public async Task<HandoffDto?> ClaimHandoffAsync(Guid id, string byAgent, CancellationToken ct)
    {
        var h = await db.Handoffs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null || h.Status != "open") return null;

        var claimer = await GetOrCreateAgentAsync(byAgent, ct);
        h.ClaimedByAgentId = claimer.Id;
        h.Status = "claimed";
        h.ClaimedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var dto = await LoadHandoffAsync(h.Id, ct);
        bus.PublishHandoff(dto!);
        return dto;
    }

    public async Task<IReadOnlyList<HandoffDto>> ListHandoffsAsync(string? status, CancellationToken ct)
    {
        var q = db.Handoffs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(h => h.Status == status);
        return await q.OrderByDescending(h => h.CreatedAt).Select(HandoffProjection).ToListAsync(ct);
    }

    /// <summary>Reusable EF-translatable projection (uses navigations, incl. an optional claimer).</summary>
    private static readonly System.Linq.Expressions.Expression<Func<Handoff, HandoffDto>> HandoffProjection =
        h => new HandoffDto(
            h.Id,
            h.FromAgent!.Name,
            h.ClaimedByAgent != null ? h.ClaimedByAgent.Name : null,
            h.Title, h.Task, h.ContextRef, h.Status, h.CreatedAt, h.ClaimedAt);

    // ---- helpers ----

    private async Task<Agent> GetOrCreateAgentAsync(string name, CancellationToken ct)
    {
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.OrgId == Org && a.Name == name, ct);
        if (agent is null)
        {
            var now = DateTimeOffset.UtcNow;
            agent = new Agent { Id = Guid.NewGuid(), OrgId = Org, Name = name, RegisteredAt = now, LastSeenAt = now };
            db.Agents.Add(agent);
            await db.SaveChangesAsync(ct);
        }
        return agent;
    }

    private async Task<Channel> EnsureChannelAsync(string name, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.OrgId == Org && c.Name == name, ct);
        if (channel is null)
        {
            channel = new Channel { Id = Guid.NewGuid(), OrgId = Org, Name = name, CreatedAt = DateTimeOffset.UtcNow };
            db.Channels.Add(channel);
            await db.SaveChangesAsync(ct);
        }
        return channel;
    }

    private async Task<HandoffDto?> LoadHandoffAsync(Guid id, CancellationToken ct) =>
        await db.Handoffs.Where(h => h.Id == id).Select(HandoffProjection).FirstOrDefaultAsync(ct);

    private static AgentDto ToDto(Agent a) => new(a.Id, a.Name, a.MachineName, a.Capabilities, a.LastSeenAt);
}
