using System.Text.RegularExpressions;

namespace Continuum.Core.Redaction;

public sealed record RedactionResult(string Text, int Count);

/// <summary>
/// Scrubs common secrets from text before it is stored as memory or sent to an embedding provider.
/// Pattern-based (not perfect) — a defense-in-depth layer, not a guarantee. Given hosted embeddings
/// were chosen, this runs on the memory path so credentials from IAM/DevOps sessions don't leak out.
/// </summary>
public static partial class SecretRedactor
{
    private static readonly (Regex Rx, string Label)[] Patterns =
    [
        (AwsAccessKey(), "AWS_KEY"),
        (PemBlock(), "PRIVATE_KEY"),
        (Jwt(), "JWT"),
        (BearerToken(), "BEARER"),
        (ConnStringPassword(), "PASSWORD"),
        (GitHubToken(), "GITHUB_TOKEN"),
        (SlackToken(), "SLACK_TOKEN"),
        (GenericAssignedSecret(), "SECRET"),
    ];

    /// <summary>Labels of any secrets detected in the text (without modifying it). Empty = clean.</summary>
    public static IReadOnlyList<string> Detect(string? input)
    {
        if (string.IsNullOrEmpty(input)) return [];
        var found = new List<string>();
        foreach (var (rx, label) in Patterns)
            if (rx.IsMatch(input)) found.Add(label);
        return found;
    }

    public static RedactionResult Redact(string input)
    {
        if (string.IsNullOrEmpty(input)) return new RedactionResult(input, 0);

        var count = 0;
        var text = input;
        foreach (var (rx, label) in Patterns)
        {
            text = rx.Replace(text, m =>
            {
                count++;
                // Preserve an assignment prefix (key=) so the text still reads naturally.
                var g = m.Groups["keep"];
                return (g.Success ? g.Value : "") + $"[REDACTED:{label}]";
            });
        }
        return new RedactionResult(text, count);
    }

    [GeneratedRegex(@"AKIA[0-9A-Z]{16}")]
    private static partial Regex AwsAccessKey();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PemBlock();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9._\-]{20,}")]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"(?i)(?<keep>(password|pwd)\s*=\s*)[^;\s""']{4,}")]
    private static partial Regex ConnStringPassword();

    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9]{20,}")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"xox[baprs]-[A-Za-z0-9-]{10,}")]
    private static partial Regex SlackToken();

    // key: "..." / api_key=... / token: ... with a long high-entropy-ish value
    [GeneratedRegex(@"(?i)(?<keep>(api[_-]?key|secret|token|access[_-]?key)\s*[:=]\s*)[""']?[A-Za-z0-9._\-]{16,}[""']?")]
    private static partial Regex GenericAssignedSecret();
}
