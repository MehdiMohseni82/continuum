using Continuum.Core.Access;
using Continuum.Core.Contracts;
using Continuum.Core.Data;
using Continuum.Core.Domain;
using Continuum.Host.Auth;
using Microsoft.EntityFrameworkCore;

namespace Continuum.Host.Services;

/// <summary>
/// Sharing with named people and teams. The <c>Shared</c> flag already covers "everyone in my
/// organization"; this covers what a flag can't express, which is naming who.
/// <para>
/// Granting is a form of control, not of reading: you may share what you own (or, for an
/// administrator, anything in the organization), and being shown something shared with you never
/// lets you pass it on.
/// </para>
/// </summary>
public sealed class SharingService(ContinuumDbContext db, ICurrentUser current, IAccessPolicy policy)
{
    private Guid Org => policy.WriteOrgId;

    // ---- teams ----

    public async Task<TeamDto> CreateTeamAsync(string name, CancellationToken ct)
    {
        var team = new Team { Id = Guid.NewGuid(), OrgId = Org, Name = name.Trim(), CreatedAt = DateTimeOffset.UtcNow };
        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);
        return new TeamDto(team.Id, team.Name, 0);
    }

    public async Task<IReadOnlyList<TeamDto>> ListTeamsAsync(CancellationToken ct) =>
        await db.Teams
            .Where(t => t.OrgId == Org)
            .OrderBy(t => t.Name)
            .Select(t => new TeamDto(t.Id, t.Name, t.Members.Count))
            .ToListAsync(ct);

    /// <summary>Add someone to a team. Both must belong to this organization. False if either doesn't.</summary>
    public async Task<bool> AddTeamMemberAsync(Guid teamId, Guid userId, CancellationToken ct)
    {
        if (!await db.Teams.AnyAsync(t => t.Id == teamId && t.OrgId == Org, ct)) return false;
        if (!await db.OrgMemberships.AnyAsync(m => m.UserId == userId && m.OrgId == Org, ct)) return false;
        if (await db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == userId, ct)) return true;

        db.TeamMembers.Add(new TeamMember { Id = Guid.NewGuid(), TeamId = teamId, UserId = userId, JoinedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RemoveTeamMemberAsync(Guid teamId, Guid userId, CancellationToken ct)
    {
        var scoped = await db.Teams.AnyAsync(t => t.Id == teamId && t.OrgId == Org, ct);
        if (!scoped) return false;
        var removed = await db.TeamMembers.Where(m => m.TeamId == teamId && m.UserId == userId).ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    // ---- grants ----

    /// <summary>
    /// Share a resource with a user or team. Returns null when the caller may not share this resource,
    /// or when the recipient isn't in the organization — sharing must not reach across tenants.
    /// Re-granting the same pair updates the access level rather than duplicating.
    /// </summary>
    public async Task<GrantDto?> GrantAsync(GrantRequest req, CancellationToken ct)
    {
        if (!await CanShareAsync(req.ResourceType, req.ResourceId, ct)) return null;
        if (!await RecipientIsInOrgAsync(req.PrincipalType, req.PrincipalId, ct)) return null;

        var existing = await db.Grants.FirstOrDefaultAsync(
            g => g.ResourceType == req.ResourceType && g.ResourceId == req.ResourceId
              && g.PrincipalType == req.PrincipalType && g.PrincipalId == req.PrincipalId, ct);

        if (existing is not null)
        {
            existing.Access = req.Access;
            existing.ExpiresAt = req.ExpiresAt;
            await db.SaveChangesAsync(ct);
            return ToDto(existing);
        }

        var grant = new Grant
        {
            Id = Guid.NewGuid(),
            OrgId = Org,
            ResourceType = req.ResourceType,
            ResourceId = req.ResourceId,
            PrincipalType = req.PrincipalType,
            PrincipalId = req.PrincipalId,
            Access = req.Access,
            GrantedByUserId = current.UserId ?? Defaults.DefaultOwnerId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = req.ExpiresAt,
        };
        db.Grants.Add(grant);
        await db.SaveChangesAsync(ct);
        return ToDto(grant);
    }

    /// <summary>Withdraw a grant. False when it doesn't exist or isn't the caller's to withdraw.</summary>
    public async Task<bool> RevokeAsync(Guid grantId, CancellationToken ct)
    {
        var grant = await db.Grants.FirstOrDefaultAsync(g => g.Id == grantId && g.OrgId == Org, ct);
        if (grant is null) return false;
        if (!await CanShareAsync(grant.ResourceType, grant.ResourceId, ct)) return false;

        db.Grants.Remove(grant);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Who a resource is shared with. Only answerable by someone who may share it.</summary>
    public async Task<IReadOnlyList<GrantDto>?> ListForResourceAsync(GrantResource type, Guid id, CancellationToken ct)
    {
        if (!await CanShareAsync(type, id, ct)) return null;
        return await db.Grants
            .Where(g => g.OrgId == Org && g.ResourceType == type && g.ResourceId == id)
            .OrderBy(g => g.CreatedAt)
            .Select(g => new GrantDto(g.Id, g.ResourceType, g.ResourceId, g.PrincipalType, g.PrincipalId, g.Access, g.CreatedAt, g.ExpiresAt))
            .ToListAsync(ct);
    }

    /// <summary>What has been shared with me — directly or through a team I'm in, and not expired.</summary>
    public async Task<IReadOnlyList<GrantDto>> SharedWithMeAsync(CancellationToken ct)
    {
        var uid = current.UserId;
        var teams = current.TeamIds;
        var now = DateTimeOffset.UtcNow;

        return await db.Grants
            .Where(g => g.OrgId == Org
                     && (g.ExpiresAt == null || g.ExpiresAt > now)
                     && ((g.PrincipalType == GrantPrincipal.User && g.PrincipalId == uid)
                      || (g.PrincipalType == GrantPrincipal.Team && teams.Contains(g.PrincipalId))))
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => new GrantDto(g.Id, g.ResourceType, g.ResourceId, g.PrincipalType, g.PrincipalId, g.Access, g.CreatedAt, g.ExpiresAt))
            .ToListAsync(ct);
    }

    // ---- helpers ----

    /// <summary>
    /// Sharing is controlling, so it follows the control rules rather than the visibility ones: what
    /// you own, or anything in the organization if you administer it.
    /// </summary>
    private Task<bool> CanShareAsync(GrantResource type, Guid id, CancellationToken ct) => type switch
    {
        GrantResource.Session => db.Sessions.Where(policy.ControlledSessions()).AnyAsync(s => s.Id == id, ct),
        GrantResource.Memory => db.Memories.Where(policy.ControlledMemories()).AnyAsync(m => m.Id == id, ct),
        // Rooms have no separate control rule: seeing one means owning it or administering the org.
        GrantResource.Room => db.Rooms.Where(policy.VisibleRooms()).AnyAsync(r => r.Id == id, ct),
        _ => Task.FromResult(false),
    };

    private Task<bool> RecipientIsInOrgAsync(GrantPrincipal type, Guid id, CancellationToken ct) => type switch
    {
        GrantPrincipal.User => db.OrgMemberships.AnyAsync(m => m.UserId == id && m.OrgId == Org, ct),
        GrantPrincipal.Team => db.Teams.AnyAsync(t => t.Id == id && t.OrgId == Org, ct),
        _ => Task.FromResult(false),
    };

    private static GrantDto ToDto(Grant g) =>
        new(g.Id, g.ResourceType, g.ResourceId, g.PrincipalType, g.PrincipalId, g.Access, g.CreatedAt, g.ExpiresAt);
}
