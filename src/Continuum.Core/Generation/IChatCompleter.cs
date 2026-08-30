namespace Continuum.Core.Generation;

/// <summary>One turn of a conversation. <c>FromUser</c> false means the assistant said it.</summary>
public readonly record struct ChatTurn(bool FromUser, string Text);

/// <summary>A text-generation model (used for memory extraction, RAG answers, and room drafting).</summary>
public interface IChatCompleter
{
    string Model { get; }

    /// <summary>Complete a system+user prompt. When jsonMode is true, the model is constrained to valid JSON.</summary>
    Task<string> CompleteAsync(string system, string user, bool jsonMode, CancellationToken ct);

    /// <summary>
    /// Complete a multi-turn conversation. Extraction and RAG are single-shot, but drafting is a
    /// back-and-forth: the model has to remember what it already proposed and what you rejected.
    /// </summary>
    Task<string> CompleteChatAsync(
        string system, IReadOnlyList<ChatTurn> turns, bool jsonMode, CancellationToken ct);
}
