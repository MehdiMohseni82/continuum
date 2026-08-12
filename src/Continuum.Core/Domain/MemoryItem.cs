using Pgvector;

namespace Continuum.Core.Domain;

/// <summary>
/// A durable fact Claude can save and recall — the anti-amnesia store. Content is redacted of
/// secrets before it is ever persisted or embedded. Salience + decay keep the store from rotting.
/// </summary>
public class MemoryItem
{
    public Guid Id { get; set; }

    /// <summary>The organization this belongs to. No query crosses organizations.</summary>
    public Guid OrgId { get; set; } = Defaults.DefaultOrgId;

    /// <summary>Team-ready seam.</summary>
    public Guid OwnerId { get; set; } = Defaults.DefaultOwnerId;

    /// <summary>Null = a global/user-level memory not tied to one project.</summary>
    public Guid? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public MemoryType Type { get; set; }

    /// <summary>The fact itself, already redacted.</summary>
    public required string Content { get; set; }

    /// <summary>Semantic embedding of <see cref="Content"/>. Dimension = <see cref="Embeddings.EmbeddingConfig.Dimensions"/>.</summary>
    public Vector? Embedding { get; set; }

    /// <summary>0..1 importance; boosted on recall, decayed over time.</summary>
    public float Salience { get; set; } = 0.5f;

    /// <summary>Never decays or gets pruned while true.</summary>
    public bool Pinned { get; set; }

    /// <summary>When true, visible to all users (opt-in share); otherwise only the owner + admins.</summary>
    public bool Shared { get; set; }

    /// <summary>Provenance: the session this was learned in, if any.</summary>
    public Guid? SourceSessionId { get; set; }

    public int TimesRecalled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastRecalledAt { get; set; }

    /// <summary>Soft expiry for decay; null = no expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
