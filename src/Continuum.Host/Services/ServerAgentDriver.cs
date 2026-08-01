using System.Text;
using System.Text.RegularExpressions;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Continuum.Core.Generation;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>
/// Drives a server-side (Claude-API) agent's turn in a room: read the transcript, build a prompt,
/// generate one message, and post it to the room's channel. Shared by the autonomous
/// <see cref="ServerAgentWorker"/> (push) and the on-demand "Lead" endpoint. It runs without an
/// authenticated user, so it reads room state directly (system-level) and posts via
/// <see cref="BusService.PostChannelAsync"/> — the same path <see cref="DigestService"/> uses.
/// </summary>
public sealed partial class ServerAgentDriver(
    ContinuumDbContext db,
    BusService bus,
    AnthropicChatCompleter completer,
    ServerAgentOptions options)
{
    [GeneratedRegex(@"@([\w.\-]+)")]
    private static partial Regex MentionRegex();

    /// <summary>A room's live turn context: enough to decide a turn and build a prompt.</summary>
    public sealed record RoomContext(
        Guid RoomId,
        string Name,
        string Topic,
        LanguageMode LanguageMode,
        string? Language,
        string ChannelName,
        IReadOnlyList<string> MemberNames,
        IReadOnlyList<(string From, string Body)> Recent);

    public readonly record struct TurnDecision(bool IsTurn, string Why);

    // ---- turn rule (ports RoomRunnerService.DecideTurn) ----

    public static TurnDecision DecideTurn(string name, IReadOnlyList<string> memberNames, IReadOnlyList<(string From, string Body)> recent)
    {
        if (recent.Count == 0)
            return memberNames.Count > 0 && memberNames[0] == name
                ? new(true, "greet (first member)")
                : new(false, "");

        var last = recent[^1];
        if (last.From == name) return new(false, "");

        var mentioned = MentionRegex().Matches(last.Body)
            .Select(m => m.Groups[1].Value)
            .Select(v => memberNames.FirstOrDefault(n => string.Equals(n, v, StringComparison.OrdinalIgnoreCase)))
            .Where(n => n is not null).Select(n => n!).ToList();

        if (mentioned.Count > 0)
            return mentioned.Contains(name)
                ? new(true, $"answer @mention from {last.From}")
                : new(false, "");

        return new(true, $"respond to {last.From}");
    }

    // ---- taking a turn ----

    /// <summary>
    /// Generate and post one message for <paramref name="agentName"/> in the room. When
    /// <paramref name="steer"/> is set (the "Lead" action), the message is directed by that instruction.
    /// Returns the posted message, or null if the room is missing/closed or the model produced nothing.
    /// </summary>
    public async Task<MessageDto?> TakeTurnAsync(Guid roomId, string agentName, string? steer, CancellationToken ct)
    {
        var ctx = await LoadContextAsync(roomId, ct);
        if (ctx is null) return null;

        var (system, user) = BuildPrompt(ctx, agentName, steer);
        var body = (await completer.CompleteAsync(system, user, jsonMode: false, ct)).Trim();
        if (string.IsNullOrWhiteSpace(body)) return null;

        return await bus.PostChannelAsync(new ChannelPostRequest(agentName, ctx.ChannelName, body), ct);
    }

    /// <summary>
    /// Choose which server agent should lead a room: the caller's preferred one if it is a member,
    /// otherwise the first configured server agent that belongs to the room. Null if none qualifies.
    /// </summary>
    public async Task<string?> ResolveLeadAgentAsync(Guid roomId, string? preferred, CancellationToken ct)
    {
        var memberNames = await db.RoomMembers
            .Where(m => m.RoomId == roomId)
            .Select(m => m.Agent!.Name)
            .ToListAsync(ct);
        if (memberNames.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(preferred) && memberNames.Contains(preferred)) return preferred;
        return options.Agents
            .Select(a => a.Name)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) && memberNames.Contains(n));
    }

    // ---- reading room state (no user scoping — this is a system component) ----

    /// <summary>Load one open room's turn context, or null if it is missing or closed.</summary>
    public async Task<RoomContext?> LoadContextAsync(Guid roomId, CancellationToken ct)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);
        if (room is null || room.Status == "closed") return null;
        return await BuildContextAsync(room, ct);
    }

    /// <summary>Load turn contexts for every open room (used by the autonomous loop).</summary>
    public async Task<IReadOnlyList<RoomContext>> LoadOpenContextsAsync(CancellationToken ct)
    {
        var rooms = await db.Rooms.Where(r => r.Status == "open").ToListAsync(ct);
        var result = new List<RoomContext>(rooms.Count);
        foreach (var room in rooms)
            result.Add(await BuildContextAsync(room, ct));
        return result;
    }

    private async Task<RoomContext> BuildContextAsync(Room room, CancellationToken ct)
    {
        var members = await db.RoomMembers
            .Where(m => m.RoomId == room.Id)
            .OrderBy(m => m.JoinedAt)
            .Select(m => m.Agent!.Name)
            .ToListAsync(ct);

        var chId = await db.Channels
            .Where(c => c.Name == room.ChannelName)
            .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

        List<(string From, string Body)> recent = [];
        if (chId is { } cid)
        {
            var rows = await db.AgentMessages
                .Where(m => m.ChannelId == cid)
                .OrderByDescending(m => m.Id).Take(Math.Max(1, options.ContextLines))
                .Select(m => new { From = m.FromAgent!.Name, m.Body })
                .ToListAsync(ct);
            rows.Reverse();
            recent = rows.Select(r => (r.From, r.Body)).ToList();
        }

        return new RoomContext(room.Id, room.Name, room.Topic, room.LanguageMode, room.Language,
            room.ChannelName, members, recent);
    }

    // ---- prompt (adapts RoomRunnerService.BuildPrompt; posts directly, no tool instruction) ----

    private (string System, string User) BuildPrompt(RoomContext ctx, string name, string? steer)
    {
        var role = options.Agents
            .FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))?.Role;
        var roleLine = string.IsNullOrWhiteSpace(role) ? "" : $" Your role in this room: {role}.";

        var langLine = ctx.LanguageMode == LanguageMode.Human
            ? $"Reply in {ctx.Language ?? "the room's language"} (natural, human language)."
            : "Reply in terse machine-to-machine shorthand: abbreviations, minimal words, no pleasantries.";

        var system =
            $"You are the agent \"{name}\" in a live Continuum room conversation with other AI agents and possibly a human.{roleLine} " +
            "You take part by posting short chat messages. Your entire reply IS the single message that will be posted to the room verbatim — " +
            "so output only the message body: no name prefix, no surrounding quotes, no preamble, and never call any tools. " +
            "Keep it to 1–4 sentences (or a few tokens in shorthand). " + langLine;

        // Humans = anyone who has spoken but isn't a member agent (the room owner joining in).
        var members = ctx.MemberNames.ToHashSet();
        var humans = ctx.Recent.Select(r => r.From).Distinct().Where(f => !members.Contains(f)).ToHashSet();
        var humanLine = humans.Count > 0
            ? $"Human operator(s) in this room (people, NOT agents): {string.Join(", ", humans)}. " +
              "Treat them as the human running you — when one speaks or @mentions you, answer them directly and do what they ask; their word overrides agent-to-agent chatter."
            : "";

        var last = ctx.Recent.Count > 0 ? ctx.Recent[^1] : ((string From, string Body)?)null;
        var lastIsHuman = last is not null && humans.Contains(last.Value.From);

        var transcript = ctx.Recent.Count == 0
            ? "(no messages yet — you start)"
            : string.Join("\n", ctx.Recent.Select(m =>
                $"{m.From}{(humans.Contains(m.From) ? " (human)" : "")}: {m.Body}"));

        string task;
        if (!string.IsNullOrWhiteSpace(steer))
            task = $"A human operator asked you to steer the room: \"{steer.Trim()}\". Do that now in one message — " +
                   "summarize, redirect, push toward a decision, or unblock as the steer requires. Move the conversation forward.";
        else if (ctx.Recent.Count == 0)
            task = "Greet the other member(s) and kick off the conversation on the topic.";
        else if (lastIsHuman)
            task = $"The human '{last!.Value.From}' just addressed the room. Answer them directly and helpfully — do the specific thing they asked.";
        else
            task = "Respond naturally to what was just said, staying on topic. If you have nothing genuinely new to add, " +
                   "say so in one short line or ask a pointed question — do not repeat a prior message.";

        var other = ctx.MemberNames.FirstOrDefault(n => n != name);
        var mentionHint = other is null ? "" : $" You can @mention another member by name (e.g. @{other}) to direct a question at them.";

        var sb = new StringBuilder();
        sb.Append("Room: \"").Append(ctx.Name).Append("\"\n");
        sb.Append("Topic: ").Append(ctx.Topic).Append('\n');
        if (humanLine.Length > 0) sb.Append(humanLine).Append('\n');
        sb.Append('\n');
        sb.Append("Recent conversation (oldest first; \"(human)\" marks a human, everyone else is an AI agent):\n");
        sb.Append(transcript).Append("\n\n");
        sb.Append("Your task: ").Append(task).Append(mentionHint);

        return (system, sb.ToString());
    }
}
