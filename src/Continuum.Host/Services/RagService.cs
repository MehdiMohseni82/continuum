using System.Text;
using Continuum.Core.Contracts;
using Continuum.Core.Generation;

namespace Continuum.Host.Services;

/// <summary>
/// "Ask my history": retrieves relevant memories (semantic) and transcript excerpts (full-text) for a
/// question, then has the local LLM answer using only that context, citing the sessions it came from.
/// </summary>
public sealed class RagService(
    MemoryService memory,
    HistoryService history,
    IChatCompleter chat)
{
    private const string System =
        "You answer questions about the user's own past Claude Code work, using ONLY the provided context " +
        "(remembered facts and transcript excerpts). Be concise and concrete. Cite the session titles you drew " +
        "from in parentheses. If the context does not contain the answer, say so plainly — do not invent details.";

    public async Task<AskResponse> AskAsync(string question, CancellationToken ct)
    {
        var memories = await memory.SearchAsync(question, null, 6, ct);
        var events = await history.SearchAsync(question, 8, ct);

        var sources = new List<RagSource>();
        var ctx = new StringBuilder();

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

        if (events.Count > 0)
        {
            ctx.AppendLine("Transcript excerpts:");
            foreach (var e in events)
            {
                var title = string.IsNullOrWhiteSpace(e.SessionTitle) ? "(untitled)" : e.SessionTitle;
                ctx.Append("- [").Append(title).Append("] ").AppendLine(e.Snippet ?? "");
                sources.Add(new RagSource("event", e.SessionId, e.SessionTitle, e.Snippet ?? ""));
            }
        }

        if (ctx.Length == 0)
            return new AskResponse("I don't have anything in your history that touches on that yet.", sources);

        var user = $"Question: {question}\n\nContext:\n{ctx}\n\nAnswer the question using only this context, citing session titles.";
        var answer = await chat.CompleteAsync(System, user, jsonMode: false, ct);

        return new AskResponse(answer.Trim(), sources);
    }
}
