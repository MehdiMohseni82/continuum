using Continuum.Core.Contracts;
using Continuum.Core.Domain;
using Continuum.Core.Export;
using Xunit;

namespace Continuum.Tests;

public class ResumeBundleTests
{
    private static SessionSummaryDto Sample() => new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
        "IAM token refresh fix",
        "D--demo", "desktop", SessionStatus.Interrupted,
        DateTimeOffset.Parse("2026-01-05T10:00:00Z"),
        DateTimeOffset.Parse("2026-01-05T10:05:00Z"),
        3);

    [Fact]
    public void Bundle_IncludesResumeCommandAndMetadata()
    {
        var md = ResumeBundle.ToMarkdown(Sample(), []);

        Assert.Contains("claude --resume aaaaaaaa-0000-0000-0000-000000000001", md);
        Assert.Contains("IAM token refresh fix", md);
        Assert.Contains("D--demo", md);
        Assert.Contains("desktop", md);
    }

    [Fact]
    public void Bundle_RendersRecentUserAndAssistantText()
    {
        var events = new List<EventDto>
        {
            new(1, Guid.NewGuid(), "user", "user", DateTimeOffset.Parse("2026-01-05T10:00:00Z"), "fix the bug"),
            new(2, Guid.NewGuid(), "assistant", "assistant", DateTimeOffset.Parse("2026-01-05T10:00:05Z"), "on it"),
            new(3, Guid.NewGuid(), "ai-title", null, DateTimeOffset.Parse("2026-01-05T10:00:06Z"), null),
        };

        var md = ResumeBundle.ToMarkdown(Sample(), events);

        Assert.Contains("fix the bug", md);
        Assert.Contains("on it", md);
    }

    [Fact]
    public void Bundle_CapsToRecentMessages()
    {
        var events = Enumerable.Range(0, 50)
            .Select(i => new EventDto(i, Guid.NewGuid(), "user", "user",
                DateTimeOffset.Parse("2026-01-05T10:00:00Z").AddSeconds(i), $"message-{i}"))
            .ToList();

        var md = ResumeBundle.ToMarkdown(Sample(), events, maxMessages: 5);

        Assert.Contains("message-49", md);   // most recent kept
        Assert.DoesNotContain("message-10", md); // older dropped
    }
}
