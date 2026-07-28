namespace Continuum.Core.Generation;

/// <summary>A local text-generation model (used for memory extraction and RAG answers).</summary>
public interface IChatCompleter
{
    string Model { get; }

    /// <summary>Complete a system+user prompt. When jsonMode is true, the model is constrained to valid JSON.</summary>
    Task<string> CompleteAsync(string system, string user, bool jsonMode, CancellationToken ct);
}
