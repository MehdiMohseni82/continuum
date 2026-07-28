namespace Continuum.Core.Domain;

/// <summary>A named topic room agents post to and read from.</summary>
public class Channel
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid OwnerId { get; set; } = Defaults.DefaultOwnerId;
    public DateTimeOffset CreatedAt { get; set; }
}
