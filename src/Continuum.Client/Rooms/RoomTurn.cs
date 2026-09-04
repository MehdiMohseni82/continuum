using System.Text.RegularExpressions;

namespace Continuum.Core.Rooms;

/// <summary>
/// Shared turn logic for room agents, used by both the daemon's local-CLI room runner and the
/// server-side Claude-API driver so the two behave identically. Open-ended multi-agent chat runs
/// forever unless three things exist, all encoded here:
/// <list type="number">
///   <item>Agents may stay silent when they have nothing new to add — a generated message equal to
///   <see cref="PassToken"/> (server side) or simply not posting (daemon side).</item>
///   <item>A hard cap on consecutive agent turns (<see cref="Decide"/> via
///   <see cref="TrailingAgentStreak"/>) terminates a room even if the agents never fall silent.</item>
///   <item>An explicit end: an agent posts a message beginning with <see cref="DoneMarker"/> to declare
///   the objective met, which closes the room.</item>
/// </list>
/// A human speaking resets the autonomous-turn streak, so human-in-the-loop rooms are never capped.
/// </summary>
public static partial class RoomTurn
{
    /// <summary>An agent replies with exactly this (case-insensitive) to skip its turn and stay silent.</summary>
    public const string PassToken = "PASS";

    /// <summary>An agent begins its final message with this to declare the objective met and end the room.</summary>
    public const string DoneMarker = "[DONE]";

    public enum TurnKind
    {
        /// <summary>Not this agent's turn (or it should stay silent).</summary>
        Skip,
        /// <summary>Take a normal turn.</summary>
        Speak,
        /// <summary>The autonomous-turn cap was reached — stop and close the room.</summary>
        Exhausted,
        /// <summary>An agent already declared the room done — stop and close it.</summary>
        Done,
    }

    public readonly record struct Decision(TurnKind Kind, string Why)
    {
        public bool IsTurn => Kind == TurnKind.Speak;
        public bool IsTerminal => Kind is TurnKind.Exhausted or TurnKind.Done;
    }

    /// <summary>True when a generated message is the silence sentinel (nothing new to add).</summary>
    public static bool IsPass(string? body) =>
        Destyle(body).Equals(PassToken, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a message declares the room finished (begins with <see cref="DoneMarker"/>).</summary>
    public static bool IsDone(string? body) =>
        Destyle(body).StartsWith(DoneMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Consecutive agent turns at the tail of the transcript: the number of trailing messages sent by a
    /// room member, counting back until a non-member (a human) is found. A human message resets it to 0.
    /// </summary>
    public static int TrailingAgentStreak(IReadOnlyList<string> sendersOldestFirst, ISet<string> memberNames)
    {
        var n = 0;
        for (var i = sendersOldestFirst.Count - 1; i >= 0; i--)
        {
            if (!memberNames.Contains(sendersOldestFirst[i])) break; // a human (non-member) — streak ends
            n++;
        }
        return n;
    }

    /// <summary>
    /// Decide an agent's turn. <paramref name="trailingAgentStreak"/> is the consecutive agent-turn count
    /// (see <see cref="TrailingAgentStreak"/>); once it reaches <paramref name="maxAutonomousTurns"/> the
    /// room is terminated rather than continued (pass 0 to disable the cap). Check order matters: a
    /// <see cref="DoneMarker"/> always wins, and an agent never replies to its own last message.
    /// </summary>
    public static Decision Decide(
        string name,
        IReadOnlyList<string> memberNames,
        IReadOnlyList<(string From, string Body)> recent,
        int trailingAgentStreak,
        int maxAutonomousTurns,
        bool mentionedHaveHadTheirTurn = false)
    {
        if (recent.Count == 0)
            return memberNames.Count > 0 && memberNames[0] == name
                ? new(TurnKind.Speak, "greet (first member)")
                : new(TurnKind.Skip, "");

        var last = recent[^1];

        if (IsDone(last.Body))
            return new(TurnKind.Done, $"room declared done by {last.From}");

        if (last.From == name)
            return new(TurnKind.Skip, "");

        if (maxAutonomousTurns > 0 && trailingAgentStreak >= maxAutonomousTurns)
            return new(TurnKind.Exhausted, $"autonomous-turn cap ({maxAutonomousTurns}) reached");

        var mentioned = MentionRegex().Matches(last.Body)
            .Select(m => m.Groups[1].Value)
            .Select(v => memberNames.FirstOrDefault(nm => string.Equals(nm, v, StringComparison.OrdinalIgnoreCase)))
            .Where(nm => nm is not null).Select(nm => nm!).ToList();

        // An @mention hands the floor to the named members, so two others are not talking over the
        // answer. But it must not hand it over permanently: if the named members have already had
        // their turn on this message and said nothing, holding everyone else back deadlocks the room.
        // That is exactly how a three-agent room went silent with plenty still to say — the last
        // message named one agent, that agent passed, and nobody was ever allowed to speak again.
        if (mentioned.Count > 0 && !mentionedHaveHadTheirTurn)
            return mentioned.Contains(name)
                ? new(TurnKind.Speak, $"answer @mention from {last.From}")
                : new(TurnKind.Skip, "");

        if (mentioned.Count > 0 && mentioned.Contains(name))
            return new(TurnKind.Skip, "");   // already had its turn on this message

        return new(TurnKind.Speak, mentioned.Count > 0
            ? $"the agent(s) {last.From} addressed have passed; open to the room"
            : $"respond to {last.From}");
    }

    /// <summary>
    /// The room members a message @mentions. Exposed so a driver can tell whether the agents a message
    /// addressed have already had their chance to answer it.
    /// </summary>
    public static IReadOnlyList<string> MentionedMembers(string? body, IReadOnlyList<string> memberNames)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        return [.. MentionRegex().Matches(body)
            .Select(m => m.Groups[1].Value)
            .Select(v => memberNames.FirstOrDefault(nm => string.Equals(nm, v, StringComparison.OrdinalIgnoreCase)))
            .Where(nm => nm is not null)
            .Select(nm => nm!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Strip surrounding markdown emphasis / quotes / code ticks and trailing sentence punctuation
    /// so a lightly-formatted <c>**PASS**</c> or <c>"[DONE] …"</c> is still recognised.</summary>
    private static string Destyle(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var s = body.Trim().Trim('*', '_', '`', '"', '\'', '“', '”', ' ', '\t', '\r', '\n');
        return s.TrimEnd('.', '!', ' ', '\t').Trim();
    }

    [GeneratedRegex(@"@([\w.\-]+)")]
    private static partial Regex MentionRegex();
}
