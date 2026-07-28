namespace Continuum.Core.Contracts;

public sealed record AskRequest(string Question);

public sealed record RagSource(string Kind, Guid? SessionId, string? SessionTitle, string Snippet);

public sealed record AskResponse(string Answer, IReadOnlyList<RagSource> Sources);
