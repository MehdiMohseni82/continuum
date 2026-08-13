using System.Linq.Expressions;
using Continuum.Core.Domain;

namespace Continuum.Core.Access;

/// <summary>
/// Who is asking. Deliberately smaller than the Host's <c>ICurrentUser</c>: deciding what a caller may
/// see needs an identity and whether they are an administrator, and nothing about HTTP. Keeping it
/// this narrow is what lets the visibility rules live beside the entities they filter, and be tested
/// without starting a web application.
/// </summary>
public interface IAccessPrincipal
{
    Guid? UserId { get; }
    bool IsAdmin { get; }

    /// <summary>
    /// The organization this request acts in. Every rule is confined to it, administrators included:
    /// operating the instance is not the same as being allowed to read what its tenants hold.
    /// Null means the caller belongs to no organization and therefore sees nothing.
    /// </summary>
    Guid? OrgId { get; }

    /// <summary>
    /// Teams the caller belongs to, resolved once per request. Held here rather than queried inside
    /// each rule so a visibility predicate stays a single SQL statement.
    /// </summary>
    IReadOnlyList<Guid> TeamIds { get; }
}

/// <summary>
/// Where the policy reads grants from. An <see cref="IQueryable{T}"/> rather than a loaded list, so
/// against a database it composes into one EXISTS subquery instead of a second round trip — and a
/// test can hand over an in-memory list and exercise the same rules with no database at all.
/// </summary>
public interface IGrantSource
{
    IQueryable<Grant> Grants { get; }
}

/// <summary>
/// The single source of truth for "who may see what". Before this existed the rule
/// <c>admin || OwnerId == uid || Shared</c> was written out at 45 separate call sites across eleven
/// files — two of them inside raw SQL strings, where no compiler or refactoring tool could see them.
/// That is survivable for one user and untenable for several: as the rule grows an organization
/// clause, then grants, then teams, a single site that misses a clause serves one person another
/// person's transcripts.
/// <para>
/// So every query asks this object instead of restating the rule. Adding tenancy later is then a
/// change to one file rather than forty-five.
/// </para>
/// <para>
/// Two distinct questions are deliberately kept apart. <em>Visible</em> is "may read", which includes
/// data shared with the caller. <em>Controlled</em> is "may change, delete or re-share", which does
/// not — being shown a session never confers the right to alter it.
/// </para>
/// </summary>
public interface IAccessPolicy
{
    // ---- visibility: may read ----

    Expression<Func<Session, bool>> VisibleSessions();

    /// <summary>Events are visible exactly when their parent session is.</summary>
    Expression<Func<Event, bool>> VisibleEvents();

    Expression<Func<MemoryItem, bool>> VisibleMemories();

    /// <summary>Rooms have no shared flag: the owner, administrators, and anyone granted access.</summary>
    Expression<Func<Room, bool>> VisibleRooms();

    /// <summary>
    /// Rooms the caller may post into. Watching a room and taking part in it are different things, so
    /// a read grant lets a colleague follow the conversation without being able to join it — only a
    /// Contribute grant (or owning the room) does that.
    /// </summary>
    Expression<Func<Room, bool>> ContributableRooms();

    /// <summary>
    /// Rooms the caller may administer: add or remove members, close it, share it. Grants never confer
    /// this, however generous — sharing a room does not hand over control of it.
    /// </summary>
    Expression<Func<Room, bool>> ControlledRooms();

    // ---- control: may change, delete, or re-share ----

    Expression<Func<Session, bool>> ControlledSessions();

    Expression<Func<MemoryItem, bool>> ControlledMemories();

    /// <summary>
    /// Who may rename a workspace. A workspace is shared across users — the same repository ingested
    /// from several accounts collapses into one — so unlike a session it isn't one person's to rename,
    /// and today that means administrators only.
    /// <para>
    /// The owner clause is present but currently inert: ingest never stamps
    /// <see cref="Workspace.OwnerId"/>, so every workspace carries the default owner. It becomes
    /// meaningful when workspaces gain proper scoping.
    /// </para>
    /// </summary>
    Expression<Func<Workspace, bool>> ControlledWorkspaces();

    /// <summary>Imperative form of <see cref="ControlledMemories"/>, for an entity already loaded.</summary>
    bool CanControl(MemoryItem memory);

    /// <summary>
    /// The organization new rows belong to, and the one to resolve names in — an agent or channel is
    /// looked up within the caller's organization, never across the instance.
    /// <para>
    /// A caller with no membership falls back to the pre-tenancy organization. That is reachable only
    /// before memberships exist, since the migration enrols every existing user and account creation
    /// enrols every new one; it exists so an upgrade can't strand a request with nowhere to write.
    /// </para>
    /// </summary>
    Guid WriteOrgId { get; }

    // ---- raw SQL ----

    /// <summary>
    /// The visibility rule for sessions as a SQL fragment, for the two analytics queries that must run
    /// as raw SQL. <paramref name="sessionAlias"/> is the alias the caller gave the Sessions table.
    /// Values come back as named arguments so callers parameterise rather than interpolate.
    /// </summary>
    SqlPredicate VisibleSessionsSql(string sessionAlias);
}

/// <summary>A SQL boolean fragment plus the arguments it references.</summary>
/// <param name="Sql">The fragment, referencing each argument by <c>@name</c>.</param>
/// <param name="Args">Values to bind. Never interpolate these into the SQL text.</param>
public sealed record SqlPredicate(string Sql, IReadOnlyList<SqlArg> Args)
{
    /// <summary>The caller may see every row, so no clause is needed at all.</summary>
    public static SqlPredicate Unrestricted { get; } = new("TRUE", []);

    /// <summary>True when the predicate restricts nothing, letting callers omit the clause entirely.</summary>
    public bool IsUnrestricted => Args.Count == 0;
}

/// <param name="Name">Parameter name without the <c>@</c> prefix.</param>
public sealed record SqlArg(string Name, object Value);

/// <summary>
/// Today's rules, unchanged: administrators see everything, you see what you own, and everyone sees
/// what has been shared. Organizations, grants and teams enter here and nowhere else.
/// </summary>
public sealed class AccessPolicy(IAccessPrincipal caller, IGrantSource grantSource) : IAccessPolicy
{
    // Read once per call and capture as locals: EF must see constants in the expression tree, not
    // property accesses on a service it would try to translate.
    private bool SeesEverything => caller.IsAdmin;
    private Guid? CallerId => caller.UserId;

    // A caller with no organization matches nothing. Guid.Empty is never a real OrgId, so comparing
    // against it denies cleanly instead of leaving the tenant clause off altogether.
    private Guid OrgScope => caller.OrgId ?? Guid.Empty;

    public Guid WriteOrgId => caller.OrgId ?? Defaults.DefaultOrgId;

    public Expression<Func<Session, bool>> VisibleSessions()
    {
        var all = SeesEverything;
        var uid = CallerId;
        var org = OrgScope;
        var granted = GrantedIds(GrantResource.Session);
        return s => s.OrgId == org && (all || s.OwnerId == uid || s.Shared || granted.Contains(s.Id));
    }

    public Expression<Func<Event, bool>> VisibleEvents()
    {
        var all = SeesEverything;
        var uid = CallerId;
        var org = OrgScope;
        var granted = GrantedIds(GrantResource.Session);
        return e => e.Session!.OrgId == org
                 && (all || e.Session.OwnerId == uid || e.Session.Shared || granted.Contains(e.SessionId));
    }

    public Expression<Func<MemoryItem, bool>> VisibleMemories()
    {
        var all = SeesEverything;
        var uid = CallerId;
        var org = OrgScope;
        var granted = GrantedIds(GrantResource.Memory);
        return m => m.OrgId == org && (all || m.OwnerId == uid || m.Shared || granted.Contains(m.Id));
    }

    public Expression<Func<Room, bool>> VisibleRooms()
    {
        var all = SeesEverything;
        var uid = CallerId;
        var org = OrgScope;
        var granted = GrantedIds(GrantResource.Room);
        return r => r.OrgId == org && (all || r.OwnerId == uid || granted.Contains(r.Id));
    }

    /// <summary>
    /// Ids of one kind of resource currently granted to this caller, as a composable subquery: either
    /// named directly or through a team they belong to, and not expired. Composed into the visibility
    /// predicates rather than fetched, so the database answers it as one EXISTS.
    /// </summary>
    /// <param name="atLeast">
    /// Minimum access the grant must confer. Contribute is the higher level, so asking for it excludes
    /// read-only grants; asking for Read accepts either.
    /// </param>
    private IQueryable<Guid> GrantedIds(GrantResource resource, GrantAccess atLeast = GrantAccess.Read)
    {
        var uid = CallerId;
        var org = OrgScope;
        var teams = caller.TeamIds;
        var now = DateTimeOffset.UtcNow;

        return grantSource.Grants
            .Where(g => g.OrgId == org
                     && g.ResourceType == resource
                     && g.Access >= atLeast
                     && (g.ExpiresAt == null || g.ExpiresAt > now)
                     && ((g.PrincipalType == GrantPrincipal.User && g.PrincipalId == uid)
                      || (g.PrincipalType == GrantPrincipal.Team && teams.Contains(g.PrincipalId))))
            .Select(g => g.ResourceId);
    }

    public Expression<Func<Room, bool>> ContributableRooms()
    {
        var all = SeesEverything;
        var uid = CallerId;
        var org = OrgScope;
        var granted = GrantedIds(GrantResource.Room, GrantAccess.Contribute);
        return r => r.OrgId == org && (all || r.OwnerId == uid || granted.Contains(r.Id));
    }

    public Expression<Func<Room, bool>> ControlledRooms()
    {
        var all = SeesEverything;
        var uid = CallerId;
        var org = OrgScope;
        return r => r.OrgId == org && (all || r.OwnerId == uid);
    }

    public Expression<Func<Session, bool>> ControlledSessions()
    {
        var all = SeesEverything;
        var uid = CallerId;
        var org = OrgScope;
        return s => s.OrgId == org && (all || s.OwnerId == uid);
    }

    public Expression<Func<MemoryItem, bool>> ControlledMemories()
    {
        var all = SeesEverything;
        var uid = CallerId;
        var org = OrgScope;
        return m => m.OrgId == org && (all || m.OwnerId == uid);
    }

    public Expression<Func<Workspace, bool>> ControlledWorkspaces()
    {
        var all = SeesEverything;
        var uid = CallerId;
        var org = OrgScope;
        return w => w.OrgId == org && (all || w.OwnerId == uid);
    }

    public bool CanControl(MemoryItem memory) =>
        memory.OrgId == OrgScope && (SeesEverything || memory.OwnerId == CallerId);

    public SqlPredicate VisibleSessionsSql(string sessionAlias)
    {
        var org = OrgScope;

        // The organization clause is never optional — an administrator is unrestricted *within* their
        // organization, not across the instance.
        if (SeesEverything)
            return new SqlPredicate(
                $"({sessionAlias}.\"OrgId\" = @scopeOrgId)",
                [new SqlArg("scopeOrgId", org)]);

        // Guid.Empty matches nothing, which is the correct reading of a caller with no identity.
        var uid = CallerId ?? Guid.Empty;

        // The grant clause mirrors GrantedIds. It has to: analytics runs as raw SQL, and if the two
        // spellings of "may read" disagree, a shared session silently vanishes from the totals.
        var sql =
            $"({sessionAlias}.\"OrgId\" = @scopeOrgId AND (" +
            $"{sessionAlias}.\"OwnerId\" = @scopeOwnerId OR {sessionAlias}.\"Shared\" OR EXISTS (" +
            "SELECT 1 FROM \"Grants\" g WHERE g.\"OrgId\" = @scopeOrgId " +
            "AND g.\"ResourceType\" = @scopeResSession " +
            $"AND g.\"ResourceId\" = {sessionAlias}.\"Id\" " +
            "AND (g.\"ExpiresAt\" IS NULL OR g.\"ExpiresAt\" > now()) " +
            "AND ((g.\"PrincipalType\" = @scopePrinUser AND g.\"PrincipalId\" = @scopeOwnerId) " +
            "OR (g.\"PrincipalType\" = @scopePrinTeam AND g.\"PrincipalId\" = ANY(@scopeTeamIds))))))";

        return new SqlPredicate(sql, [
            new SqlArg("scopeOrgId", org),
            new SqlArg("scopeOwnerId", uid),
            new SqlArg("scopeResSession", (int)GrantResource.Session),
            new SqlArg("scopePrinUser", (int)GrantPrincipal.User),
            new SqlArg("scopePrinTeam", (int)GrantPrincipal.Team),
            new SqlArg("scopeTeamIds", caller.TeamIds.ToArray()),
        ]);
    }
}
