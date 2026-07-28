using System.Text;
using Continuum.Core.Contracts;

namespace Continuum.Core.Export;

/// <summary>
/// Builds a portable, human-readable hand-off bundle for a session: what it was, where it
/// left off, and how to resume it on another machine. Pure (no DB) so it is unit-testable.
/// </summary>
public static class ResumeBundle
{
    /// <summary>Render a markdown hand-off for the given session and its (ordered) events.</summary>
    public static string ToMarkdown(SessionSummaryDto s, IReadOnlyList<EventDto> events, int maxMessages = 20)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(string.IsNullOrWhiteSpace(s.Title) ? "(untitled session)" : s.Title);
        sb.AppendLine();
        sb.Append("- **Project:** ").AppendLine(s.Workspace);
        sb.Append("- **Machine:** ").AppendLine(s.Machine);
        sb.Append("- **Status:** ").AppendLine(s.Status.ToString());
        sb.Append("- **Span:** ").Append(s.StartedAt.ToString("u")).Append(" → ").AppendLine(s.LastEventAt.ToString("u"));
        sb.Append("- **Events:** ").AppendLine(s.MessageCount.ToString());
        sb.Append("- **Resume:** `claude --resume ").Append(s.Id).AppendLine("`");
        sb.AppendLine();
        sb.AppendLine("## Recent conversation");
        sb.AppendLine();

        var recent = events
            .Where(e => !string.IsNullOrWhiteSpace(e.Text) && e.Role is "user" or "assistant")
            .TakeLast(maxMessages);

        foreach (var e in recent)
        {
            var who = e.Role == "user" ? "🧑 User" : "🤖 Assistant";
            sb.Append("**").Append(who).Append("** · ").AppendLine(e.Timestamp.ToString("HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine(Trim(e.Text!, 1200));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + " …";
}
