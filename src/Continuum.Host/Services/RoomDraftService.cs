using System.Text;
using Continuum.Core.Contracts;
using Continuum.Core.Generation;

namespace Continuum.Host.Services;

/// <summary>
/// Turns a specification document and a conversation about it into a room worth opening.
///
/// <para>
/// The valuable output is not the name — it is the system prompt and the definition of done. A room
/// whose agents were told "discuss the payments design" produced the 48-hour failure this codebase
/// already carries scar tissue for (see RoomTurn): two agents talking about root cause forever and
/// never changing a line. So the drafting prompt below spends most of its instruction budget forcing
/// a brief that names an artifact, a first move, and a finish line.
/// </para>
/// <para>
/// Grounded in what Continuum already knows: the same retrieval that backs "Ask my history" runs over
/// the workspace, so the room reflects decisions already taken rather than only what the document says.
/// </para>
/// </summary>
public sealed class RoomDraftService(
    MemoryService memory,
    HistoryService history,
    IChatCompleter chat,
    RoomDraftCompleter drafting)
{
    // A local 7B spends real time just reading its input, and drafting already sits near every proxy's
    // patience. A specification long enough to hit this cap has said what it needs to in the first
    // quarter; the truncation is announced to the model so it does not invent what it cannot see.
    private const int MaxSpecChars = 24_000;
    private const int MaxHistoryTurns = 24;

    private const string System = """
        You help a developer turn a project specification into a Continuum "room": a working session in
        which two or more coding agents collaborate on one goal, each in its own repository checkout.

        ALWAYS include a proposal when you have a specification document to work from — in your very first
        reply, not after a round of questions. You may ask questions alongside it, never instead of it.
        The developer edits every field before anything is created, so a proposal they correct beats a
        question they must answer, and a reply with no proposal leaves them nothing to act on.

        Ask only about what is genuinely ambiguous — the scope boundary, which repos are involved, what
        "done" means — at most two questions, and never about anything the document already answers.
        Where the document is silent, choose something defensible and say what you assumed in "reply".

        Never invent specifics the document does not support. If you do not know the language, framework
        or directory layout, describe the artifact without naming a path: "a failing test covering the
        settlement contract", not "a failing test in src/main/java/com/example/payments".

        What makes a room work, and what makes one fail:
        - Rooms fail when agents discuss instead of act. A room has run for 48 hours producing nothing
          but conversation about root cause. The systemPrompt is the fix.
        - The systemPrompt is read by every agent when it joins. Write it as a standing instruction to
          them, in the second person. It must name the concrete artifact they are producing (a file, an
          API contract, a passing test), state what each agent does first, and forbid discussing the
          problem without changing something. Demand that they show work — a diff, a test result — every
          turn.
        - doneCriteria must be checkable by looking at the repository, not by asking someone's opinion.
          "Both agree the design is good" is not a finish line. "A failing test in payments/ encodes the
          contract, and the adapter makes it pass" is.
        - Two agents is the common shape: an implementer that changes code, and a consultant that
          reviews and pushes back. Give each a name matching the repository or service it works in.

        Reply with a single JSON object and nothing else:

        {
          "reply": "what you say to the developer — plain prose, no markdown headings",
          "proposal": {
            "name": "short room name",
            "topic": "one or two sentences on what this room settles",
            "systemPrompt": "the standing instruction every agent reads on joining",
            "doneCriteria": "the checkable finish line",
            "languageMode": "Human",
            "language": "English",
            "agents": [
              {"name": "service-or-repo-name", "role": "implementer", "write": true,
               "responsibility": "what this agent owns"}
            ]
          }
        }

        Omit "proposal" only when you have nothing at all to work from — no document and no description.
        Never return a proposal with only some fields filled in.
        """;

    /// <summary>
    /// Draft, or continue drafting. Never throws for a model failure: the chat has to keep working
    /// even when generation is misconfigured, or the panel looks broken with nothing to explain it.
    /// </summary>
    public async Task<RoomDraftResponse> DraftAsync(RoomDraftRequest req, CancellationToken ct)
    {
        var completer = drafting.Resolve(chat);
        var sources = new List<RagSource>();
        var grounding = await GroundAsync(req, sources, ct);

        var turns = new List<ChatTurn>();

        // The spec and the retrieved context lead as the first user turn, so the model sees them
        // before anything the developer said about them.
        var opening = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(req.Spec))
        {
            opening.AppendLine("Specification document:");
            opening.AppendLine(Truncate(req.Spec!, MaxSpecChars));
            opening.AppendLine();
        }
        if (grounding.Length > 0)
        {
            opening.AppendLine("What Continuum already knows about this project:");
            opening.Append(grounding);
            opening.AppendLine();
        }
        if (opening.Length > 0)
            turns.Add(new ChatTurn(FromUser: true, opening.ToString().TrimEnd()));

        foreach (var t in req.History.TakeLast(MaxHistoryTurns))
        {
            var text = t.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            turns.Add(new ChatTurn(
                FromUser: !string.Equals(t.Role, "assistant", StringComparison.OrdinalIgnoreCase),
                text));
        }

        // A conversation that opens with no spec and no message has nothing to work from.
        if (turns.Count == 0)
            return new RoomDraftResponse(
                "Paste your specification, or attach the document, and I'll draft a room from it.",
                null, sources, completer.Model);

        // The API wants the last turn to be the developer's. If the client replays a trailing
        // assistant message, asking again would be answering ourselves.
        if (!turns[^1].FromUser)
            turns.Add(new ChatTurn(FromUser: true, "Continue — propose the room."));

        // "Propose the room now": the model stalled and the developer is overruling it. A small local
        // model in particular will keep asking questions well past the point of being useful, and until
        // it commits to something there is nothing on screen to create.
        var system = req.RequireProposal
            ? System + "\n\nThe developer has asked you to propose the room NOW. Your reply MUST contain "
                     + "a complete proposal. Ask nothing further — state your assumptions in \"reply\" instead."
            : System;

        string raw;
        try
        {
            raw = await completer.CompleteChatAsync(system, turns, jsonMode: true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new RoomDraftResponse(
                $"The drafting model ({completer.Model}) could not be reached: {ex.Message}",
                null, sources, completer.Model);
        }

        var (reply, proposal) = RoomDraftParser.Parse(raw);
        if (string.IsNullOrWhiteSpace(reply) && proposal is null)
            reply = "I couldn't draft anything from that. Try giving me more of the specification.";

        return new RoomDraftResponse(reply, proposal, sources, completer.Model);
    }

    /// <summary>
    /// Retrieval over the workspace's memories and transcripts, using the specification as the query.
    /// Skipped when no workspace is named — grounding is opt-in, since a new project has no history
    /// worth pulling and an old one's decisions could steer it wrongly.
    /// </summary>
    private async Task<StringBuilder> GroundAsync(
        RoomDraftRequest req, List<RagSource> sources, CancellationToken ct)
    {
        var ctx = new StringBuilder();
        if (req.WorkspaceId is not { } workspaceId) return ctx;

        // The most recent thing said is a better query than the whole document, which is long enough
        // to wash out any single topic in an embedding.
        var query = req.History.LastOrDefault(t =>
                        !string.Equals(t.Role, "assistant", StringComparison.OrdinalIgnoreCase))?.Text
                    ?? Truncate(req.Spec ?? "", 2000);
        if (string.IsNullOrWhiteSpace(query)) return ctx;

        var memories = await memory.SearchAsync(query, workspaceId, 6, ct);
        if (memories.Count > 0)
        {
            ctx.AppendLine("Remembered facts:");
            foreach (var m in memories)
            {
                ctx.Append("- [").Append(m.Type).Append("] ").AppendLine(m.Content);
                sources.Add(new RagSource("memory", null, null, m.Content));
            }
            ctx.AppendLine();
        }

        var events = await history.SearchAsync(query, 6, ct, workspaceId);
        if (events.Count > 0)
        {
            ctx.AppendLine("Transcript excerpts:");
            foreach (var e in events)
            {
                var title = string.IsNullOrWhiteSpace(e.SessionTitle) ? "(untitled)" : e.SessionTitle;
                ctx.Append("- [").Append(title).Append("] ").AppendLine(e.Snippet ?? "");
                sources.Add(new RagSource("session", e.SessionId, e.SessionTitle, e.Snippet ?? ""));
            }
        }

        return ctx;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\n…(truncated)";
}

/// <summary>
/// Which model drafts rooms. Prefers the Claude API when a key is configured, because a brief that has
/// to be demanding and specific is exactly where a small local model reads thin — and falls back to the
/// self-hosted completer so the feature works on a deployment that has no key and sends nothing out.
/// </summary>
public sealed class RoomDraftCompleter(AnthropicChatCompleter? anthropic = null)
{
    public IChatCompleter Resolve(IChatCompleter fallback) => anthropic ?? fallback;
}
