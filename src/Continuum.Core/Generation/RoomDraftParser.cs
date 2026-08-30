using System.Text.Json;
using Continuum.Core.Contracts;
using Continuum.Core.Domain;

namespace Continuum.Core.Generation;

/// <summary>
/// Reads the drafting model's reply: a sentence or two for the chat, plus an optional room proposal.
///
/// <para>
/// Deliberately forgiving, because a 7B local model and Claude return visibly different shapes for
/// the same instruction. The one rule it enforces is that a proposal is all-or-nothing: a room with
/// a name but no topic is worse than no proposal at all, because it looks ready to create.
/// </para>
/// </summary>
public static class RoomDraftParser
{
    private const int MaxReply = 4000;
    private const int MaxName = 200;
    private const int MaxTopic = 2000;
    private const int MaxPrompt = 8000;
    private const int MaxAgents = 6;

    public static (string Reply, RoomProposal? Proposal) Parse(string? raw)
    {
        if (LlmJson.Root(raw) is not { ValueKind: JsonValueKind.Object } r)
        {
            // No JSON at all: the model just talked. That is a legitimate turn — it may be asking a
            // clarifying question — so keep the prose rather than discarding the response.
            var prose = LlmJson.Clean(LlmJson.Unfence(raw ?? ""), MaxReply);
            return (prose ?? "", null);
        }

        var reply = LlmJson.Clean(LlmJson.GetString(r, "reply"), MaxReply) ?? "";
        return (reply, ReadProposal(r));
    }

    private static RoomProposal? ReadProposal(JsonElement r)
    {
        if (!r.TryGetProperty("proposal", out var p) || p.ValueKind != JsonValueKind.Object)
            return null;

        // All-or-nothing: without both of these there is nothing to create.
        var name = LlmJson.Clean(LlmJson.GetString(p, "name"), MaxName);
        var topic = LlmJson.Clean(LlmJson.GetString(p, "topic"), MaxTopic);
        if (name is null || topic is null) return null;

        // Shorthand is the terse machine-to-machine mode; anything unrecognised stays Human, which
        // is the readable default and the one a person watching the room actually wants.
        var mode = LlmJson.GetString(p, "languageMode");
        var languageMode = string.Equals(mode, "Shorthand", StringComparison.OrdinalIgnoreCase)
            ? LanguageMode.Shorthand
            : LanguageMode.Human;

        return new RoomProposal(
            name,
            topic,
            LlmJson.Clean(LlmJson.GetString(p, "systemPrompt"), MaxPrompt) ?? "",
            LlmJson.Clean(LlmJson.GetString(p, "doneCriteria"), MaxTopic) ?? "",
            ReadAgents(p),
            languageMode,
            LlmJson.Clean(LlmJson.GetString(p, "language"), 60) ?? "English");
    }

    private static IReadOnlyList<ProposedAgent> ReadAgents(JsonElement p)
    {
        if (!p.TryGetProperty("agents", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var agents = new List<ProposedAgent>();
        foreach (var a in arr.EnumerateArray())
        {
            if (a.ValueKind != JsonValueKind.Object) continue;

            var name = LlmJson.Clean(LlmJson.GetString(a, "name"), MaxName);
            if (name is null) continue;

            // An implementer changes code and a consultant reviews it; anything else is a consultant,
            // which is the safe reading — it does not imply write access to a repo.
            var role = LlmJson.GetString(a, "role")?.Trim().ToLowerInvariant();
            var implementer = role == "implementer";

            agents.Add(new ProposedAgent(
                name,
                implementer ? "implementer" : "consultant",
                // Write is only meaningful for an implementer; never infer it for a reviewer.
                implementer && LlmJson.GetBool(a, "write", true),
                LlmJson.Clean(LlmJson.GetString(a, "responsibility"), MaxTopic) ?? ""));

            if (agents.Count == MaxAgents) break;
        }
        return agents;
    }
}
