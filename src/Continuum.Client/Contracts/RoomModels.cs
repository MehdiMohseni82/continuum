using Continuum.Core.Domain;

namespace Continuum.Core.Contracts;

public sealed record CreateRoomRequest(string Name, string Topic, LanguageMode LanguageMode, string? Language, string? SystemPrompt = null);
public sealed record AddMemberRequest(string Agent);
public sealed record RoomPostRequest(string FromAgent, string Body,
    int? InputTokens = null, int? OutputTokens = null, int? CacheReadTokens = null, int? CacheCreationTokens = null);

/// <summary>Ask a server-side (Claude API) agent to take a turn now. Optional steer directs the message;
/// optional agent picks which server agent speaks (defaults to the first configured one in the room).</summary>
public sealed record LeadRequest(string? Steer, string? Agent);

/// <param name="UserId">Whose agent this is — null for members enrolled before rooms crossed people.</param>
/// <param name="User">That person's display name, for showing who brought which agent.</param>
public sealed record RoomMemberDto(
    string Agent, string? MachineName, DateTimeOffset JoinedAt, Guid? UserId = null, string? User = null);

/// <summary>Token spend in a room attributed to one participant.</summary>
public sealed record RoomUserTokensDto(Guid? UserId, string User, int MessageCount, long TotalTokens);

public sealed record RoomDto(
    Guid Id,
    string Name,
    string Topic,
    LanguageMode LanguageMode,
    string? Language,
    string Status,
    string ChannelName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    int MemberCount,
    int MessageCount,
    DateTimeOffset? LastActivityAt,
    string? SystemPrompt = null,
    long TotalTokens = 0);

public sealed record RoomDetailDto(
    RoomDto Room,
    IReadOnlyList<RoomMemberDto> Members,
    IReadOnlyList<MessageDto> Messages,
    IReadOnlyList<RoomUserTokensDto>? TokensByUser = null);

// --- room drafting: turn a specification document into a room worth opening ---

/// <summary>One turn of the drafting conversation, as the browser has it.</summary>
public sealed record RoomDraftTurn(string Role, string Text);

/// <param name="Spec">The specification document, pasted or read from an attached file. Sent once;
/// afterwards it is carried in the conversation the client replays.</param>
/// <param name="WorkspaceId">Ground the draft in what Continuum already knows about this project.
/// Null draws on the document alone.</param>
/// <param name="RequireProposal">Set by "Propose the room now". A model that keeps asking questions
/// instead of committing leaves the developer with nothing to create, so they can overrule it.</param>
public sealed record RoomDraftRequest(
    string? Spec,
    IReadOnlyList<RoomDraftTurn> History,
    Guid? WorkspaceId = null,
    bool RequireProposal = false);

/// <summary>An agent the draft says the room needs, and the part it plays.</summary>
/// <param name="Role">implementer or consultant — an implementer changes code, a consultant reviews.</param>
/// <param name="Write">Whether this agent is expected to modify the repo.</param>
public sealed record ProposedAgent(string Name, string Role, bool Write, string Responsibility);

/// <summary>A room the assistant is proposing. Every field is editable before anything is created.</summary>
public sealed record RoomProposal(
    string Name,
    string Topic,
    string SystemPrompt,
    string DoneCriteria,
    IReadOnlyList<ProposedAgent> Agents,
    LanguageMode LanguageMode = LanguageMode.Human,
    string? Language = "English");

/// <param name="Reply">What the assistant says back — always present, so the chat never stalls.</param>
/// <param name="Proposal">Null until it has enough to propose something concrete.</param>
/// <param name="Sources">What it drew on from memory and history, so the draft is auditable.</param>
/// <param name="Model">Which model drafted this. Surfaced because a 7B local model and Claude
/// produce visibly different briefs, and you should know which one you are reading.</param>
public sealed record RoomDraftResponse(
    string Reply,
    RoomProposal? Proposal,
    IReadOnlyList<RagSource> Sources,
    string Model);
