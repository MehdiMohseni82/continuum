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
public sealed class RoomService(ContinuumDbContext db, BusBroadcaster bus, ICurrentUser current)
{
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
        var admin = current.IsAdmin;
        var uid = current.UserId;
        var rooms = await db.Rooms
            .Where(r => admin || r.OwnerId == uid)
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
            .Select(g => new { ChannelId = g.Key, Count = g.Count(), Last = g.Max(x => x.CreatedAt) })
            .ToListAsync(ct);
        var statByChannel = stats.ToDictionary(s => s.ChannelId, s => s);

        var memberCounts = await db.RoomMembers
            .Where(m => rooms.Select(r => r.Id).Contains(m.RoomId))
            .GroupBy(m => m.RoomId).Select(g => new { RoomId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoomId, x => x.Count, ct);

        return rooms.Select(r =>
        {
            var mc = memberCounts.TryGetValue(r.Id, out var m) ? m : 0;
            int msgCount = 0; DateTimeOffset? last = null;
            if (idByName.TryGetValue(r.ChannelName, out var chId) && statByChannel.TryGetValue(chId, out var s))
            { msgCount = s.Count; last = s.Last; }
            return ToDto(r, mc, msgCount, last);
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
                .Select(m => new MessageDto(m.Id, m.FromAgent!.Name, null, null, m.Body, m.CreatedAt))
                .ToListAsync(ct);
            messages.Reverse();
        }

        var (count, last) = await ChannelStats(chId, ct);
        return new RoomDetailDto(ToDto(room, members.Count, count, last), members, messages);
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
            .Select(m => new MessageDto(m.Id, m.FromAgent!.Name, null, null, m.Body, m.CreatedAt))
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
    public async Task<MessageDto?> PostAsync(Guid id, string fromAgent, string body, CancellationToken ct)
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
        };
        db.AgentMessages.Add(msg);
        await db.SaveChangesAsync(ct);

        var dto = new MessageDto(msg.Id, from.Name, null, room.ChannelName, msg.Body, msg.CreatedAt);
        bus.PublishMessage(dto);
        return dto;
    }

    // ---- helpers ----

    private async Task<Room?> FindVisibleAsync(Guid id, CancellationToken ct)
    {
        var admin = current.IsAdmin;
        var uid = current.UserId;
        return await db.Rooms.FirstOrDefaultAsync(r => r.Id == id && (admin || r.OwnerId == uid), ct);
    }

    private Task<Guid?> ChannelIdAsync(string channelName, CancellationToken ct) =>
        db.Channels.Where(c => c.Name == channelName).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

    private IQueryable<AgentMessage> ChannelQuery(Guid channelId) =>
        db.AgentMessages.Where(m => m.ChannelId == channelId);

    private async Task<(int Count, DateTimeOffset? Last)> ChannelStats(Guid? channelId, CancellationToken ct)
    {
        if (channelId is null) return (0, null);
        var cid = channelId.Value;
        var q = db.AgentMessages.Where(m => m.ChannelId == cid);
        var count = await q.CountAsync(ct);
        var last = count == 0 ? (DateTimeOffset?)null : await q.MaxAsync(m => m.CreatedAt, ct);
        return (count, last);
    }

    private async Task<Agent> GetOrCreateAgentAsync(string name, CancellationToken ct)
    {
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.Name == name, ct);
        if (agent is null)
        {
            var now = DateTimeOffset.UtcNow;
            agent = new Agent { Id = Guid.NewGuid(), Name = name, RegisteredAt = now, LastSeenAt = now };
            db.Agents.Add(agent);
            await db.SaveChangesAsync(ct);
        }
        return agent;
    }

    private async Task<Channel> EnsureChannelAsync(string name, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Name == name, ct);
        if (channel is null)
        {
            channel = new Channel { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTimeOffset.UtcNow };
            db.Channels.Add(channel);
            await db.SaveChangesAsync(ct);
        }
        return channel;
    }

    private static RoomDto ToDto(Room r, int memberCount, int messageCount, DateTimeOffset? lastActivity) =>
        new(r.Id, r.Name, r.Topic, r.LanguageMode, r.Language, r.Status, r.ChannelName,
            r.CreatedAt, r.ClosedAt, memberCount, messageCount, lastActivity, r.SystemPrompt);
}
