using Continuum.Core.Access;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Continuum.Host.Auth;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>
/// Rooms: named group conversations for bus agents. A room owns a backing channel, so messages are
/// ordinary channel messages (agents talk via channel_post/channel_read). Admin creates/closes rooms;
/// a closed room rejects posts. Read side is scoped to the owner (admins see all), mirroring the app.
/// </summary>
public sealed class RoomService(ContinuumDbContext db, BusBroadcaster bus, ICurrentUser current, IAccessPolicy policy)
{
    private Guid Org => policy.WriteOrgId;

    public async Task<RoomDto> CreateAsync(CreateRoomRequest req, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var room = new Room
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            Topic = req.Topic.Trim(),
            LanguageMode = req.LanguageMode,
            Language = req.LanguageMode == LanguageMode.Human ? (req.Language?.Trim() ?? "English") : null,
            SystemPrompt = string.IsNullOrWhiteSpace(req.SystemPrompt) ? null : req.SystemPrompt.Trim(),
            Status = "open",
            ChannelName = "room:" + Guid.NewGuid().ToString("N")[..12],
            OrgId = Org,
            OwnerId = current.UserId ?? Defaults.DefaultOwnerId,
            CreatedAt = now,
        };
        await EnsureChannelAsync(room.ChannelName, ct);
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        return ToDto(room, 0, 0, null);
    }

    public async Task<IReadOnlyList<RoomDto>> ListAsync(CancellationToken ct)
    {
        var rooms = await db.Rooms
            .Where(policy.VisibleRooms())
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        if (rooms.Count == 0) return [];

        var names = rooms.Select(r => r.ChannelName).ToList();
        var channels = await db.Channels.Where(c => names.Contains(c.Name))
            .Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var idByName = channels.ToDictionary(c => c.Name, c => c.Id);
        var channelIds = channels.Select(c => c.Id).ToList();

        var stats = await db.AgentMessages
            .Where(m => m.ChannelId != null && channelIds.Contains(m.ChannelId!.Value))
            .GroupBy(m => m.ChannelId!.Value)
            .Select(g => new
            {
                ChannelId = g.Key,
                Count = g.Count(),
                Last = g.Max(x => x.CreatedAt),
                Tokens = g.Sum(x => (long)((x.InputTokens ?? 0) + (x.OutputTokens ?? 0) + (x.CacheReadTokens ?? 0) + (x.CacheCreationTokens ?? 0)))
            })
            .ToListAsync(ct);
        var statByChannel = stats.ToDictionary(s => s.ChannelId, s => s);

        var memberCounts = await db.RoomMembers
            .Where(m => rooms.Select(r => r.Id).Contains(m.RoomId))
            .GroupBy(m => m.RoomId).Select(g => new { RoomId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoomId, x => x.Count, ct);

        return rooms.Select(r =>
        {
            var mc = memberCounts.TryGetValue(r.Id, out var m) ? m : 0;
            int msgCount = 0; DateTimeOffset? last = null; long tokens = 0;
            if (idByName.TryGetValue(r.ChannelName, out var chId) && statByChannel.TryGetValue(chId, out var s))
            { msgCount = s.Count; last = s.Last; tokens = s.Tokens; }
            return ToDto(r, mc, msgCount, last, tokens);
        }).ToList();
    }

    public async Task<RoomDetailDto?> GetAsync(Guid id, int take, CancellationToken ct)
    {
        var room = await FindVisibleAsync(id, ct);
        if (room is null) return null;

        var members = await db.RoomMembers
            .Where(m => m.RoomId == id)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new RoomMemberDto(m.Agent!.Name, m.Agent.MachineName, m.JoinedAt))
            .ToListAsync(ct);

        var chId = await ChannelIdAsync(room.ChannelName, ct);
        List<MessageDto> messages = [];
        if (chId is { } cid)
        {
            messages = await ChannelQuery(cid)
                .OrderByDescending(m => m.Id).Take(take)
                .Select(m => new MessageDto(m.Id, m.FromAgent!.Name, null, null, m.Body, m.CreatedAt,
                    m.InputTokens, m.OutputTokens, m.CacheReadTokens, m.CacheCreationTokens))
                .ToListAsync(ct);
            messages.Reverse();
        }

        var (count, last, tokens) = await ChannelStats(chId, ct);
        return new RoomDetailDto(ToDto(room, members.Count, count, last, tokens), members, messages);
    }

    public async Task<IReadOnlyList<MessageDto>> MessagesAsync(Guid id, long sinceId, int take, CancellationToken ct)
    {
        var room = await FindVisibleAsync(id, ct);
        if (room is null) return [];
        var chId = await ChannelIdAsync(room.ChannelName, ct);
        if (chId is null) return [];
        return await ChannelQuery(chId.Value)
            .Where(m => m.Id > sinceId)
            .OrderBy(m => m.Id).Take(take)
            .Select(m => new MessageDto(m.Id, m.FromAgent!.Name, null, null, m.Body, m.CreatedAt,
                m.InputTokens, m.OutputTokens, m.CacheReadTokens, m.CacheCreationTokens))
            .ToListAsync(ct);
    }

    public async Task<bool> AddMemberAsync(Guid id, string agentName, CancellationToken ct)
    {
        var room = await FindVisibleAsync(id, ct);
        if (room is null) return false;
        var agent = await GetOrCreateAgentAsync(agentName, ct);
        var exists = await db.RoomMembers.AnyAsync(m => m.RoomId == id && m.AgentId == agent.Id, ct);
        if (!exists)
        {
            db.RoomMembers.Add(new RoomMember { Id = Guid.NewGuid(), RoomId = id, AgentId = agent.Id, JoinedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task<bool> RemoveMemberAsync(Guid id, string agentName, CancellationToken ct)
    {
        var room = await FindVisibleAsync(id, ct);
        if (room is null) return false;
        var removed = await db.RoomMembers
            .Where(m => m.RoomId == id && m.Agent!.Name == agentName)
            .ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    public async Task<bool> CloseAsync(Guid id, CancellationToken ct)
    {
        var room = await FindVisibleAsync(id, ct);
        if (room is null) return false;
        if (room.Status != "closed")
        {
            room.Status = "closed";
            room.ClosedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    /// <summary>Post a message into a room. Returns null if the room is missing/closed.</summary>
    public async Task<MessageDto?> PostAsync(Guid id, string fromAgent, string body, CancellationToken ct,
        int? inputTokens = null, int? outputTokens = null, int? cacheReadTokens = null, int? cacheCreationTokens = null)
    {
        var room = await FindVisibleAsync(id, ct);
        if (room is null || room.Status == "closed") return null;

        var from = await GetOrCreateAgentAsync(fromAgent, ct);
        var channel = await EnsureChannelAsync(room.ChannelName, ct);
        var msg = new AgentMessage
        {
            FromAgentId = from.Id,
            ChannelId = channel.Id,
            Body = body,
            CreatedAt = DateTimeOffset.UtcNow,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheCreationTokens = cacheCreationTokens,
        };
        db.AgentMessages.Add(msg);
        await db.SaveChangesAsync(ct);

        var dto = new MessageDto(msg.Id, from.Name, null, room.ChannelName, msg.Body, msg.CreatedAt,
            msg.InputTokens, msg.OutputTokens, msg.CacheReadTokens, msg.CacheCreationTokens);
        bus.PublishMessage(dto);
        return dto;
    }

    // ---- helpers ----

    private async Task<Room?> FindVisibleAsync(Guid id, CancellationToken ct) =>
        await db.Rooms.Where(policy.VisibleRooms()).FirstOrDefaultAsync(r => r.Id == id, ct);

    private Task<Guid?> ChannelIdAsync(string channelName, CancellationToken ct) =>
        db.Channels.Where(c => c.OrgId == Org && c.Name == channelName).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

    private IQueryable<AgentMessage> ChannelQuery(Guid channelId) =>
        db.AgentMessages.Where(m => m.ChannelId == channelId);

    private async Task<(int Count, DateTimeOffset? Last, long Tokens)> ChannelStats(Guid? channelId, CancellationToken ct)
    {
        if (channelId is null) return (0, null, 0);
        var cid = channelId.Value;
        var q = db.AgentMessages.Where(m => m.ChannelId == cid);
        var count = await q.CountAsync(ct);
        if (count == 0) return (0, null, 0);
        var last = await q.MaxAsync(m => m.CreatedAt, ct);
        var tokens = await q.SumAsync(m => (long)((m.InputTokens ?? 0) + (m.OutputTokens ?? 0) + (m.CacheReadTokens ?? 0) + (m.CacheCreationTokens ?? 0)), ct);
        return (count, last, tokens);
    }

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

    private static RoomDto ToDto(Room r, int memberCount, int messageCount, DateTimeOffset? lastActivity, long totalTokens = 0) =>
        new(r.Id, r.Name, r.Topic, r.LanguageMode, r.Language, r.Status, r.ChannelName,
            r.CreatedAt, r.ClosedAt, memberCount, messageCount, lastActivity, r.SystemPrompt, totalTokens);
}
