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

    /// <summary>Rooms have no shared flag today — only the owner (and admins) see them.</summary>
    Expression<Func<Room, bool>> VisibleRooms();

    // ---- control: may change, delete, or re-share ----

    Expression<Func<Session, bool>> ControlledSessions();

    Expression<Func<MemoryItem, bool>> ControlledMemories();

    /// <summary>Imperative form of <see cref="ControlledMemories"/>, for an entity already loaded.</summary>
    bool CanControl(MemoryItem memory);

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
public sealed class AccessPolicy(IAccessPrincipal caller) : IAccessPolicy
{
    // Read once per call and capture as locals: EF must see constants in the expression tree, not
    // property accesses on a service it would try to translate.
    private bool SeesEverything => caller.IsAdmin;
    private Guid? CallerId => caller.UserId;

    public Expression<Func<Session, bool>> VisibleSessions()
    {
        var all = SeesEverything;
        var uid = CallerId;
        return s => all || s.OwnerId == uid || s.Shared;
    }

    public Expression<Func<Event, bool>> VisibleEvents()
    {
        var all = SeesEverything;
        var uid = CallerId;
        return e => all || e.Session!.OwnerId == uid || e.Session.Shared;
    }

    public Expression<Func<MemoryItem, bool>> VisibleMemories()
    {
        var all = SeesEverything;
        var uid = CallerId;
        return m => all || m.OwnerId == uid || m.Shared;
    }

    public Expression<Func<Room, bool>> VisibleRooms()
    {
        var all = SeesEverything;
        var uid = CallerId;
        return r => all || r.OwnerId == uid;
    }

    public Expression<Func<Session, bool>> ControlledSessions()
    {
        var all = SeesEverything;
        var uid = CallerId;
        return s => all || s.OwnerId == uid;
    }

    public Expression<Func<MemoryItem, bool>> ControlledMemories()
    {
        var all = SeesEverything;
        var uid = CallerId;
        return m => all || m.OwnerId == uid;
    }

    public bool CanControl(MemoryItem memory) => SeesEverything || memory.OwnerId == CallerId;

    public SqlPredicate VisibleSessionsSql(string sessionAlias)
    {
        if (SeesEverything) return SqlPredicate.Unrestricted;

        // Guid.Empty matches nothing, which is the correct reading of an unauthenticated caller.
        var uid = CallerId ?? Guid.Empty;
        return new SqlPredicate(
            $"({sessionAlias}.\"OwnerId\" = @scopeOwnerId OR {sessionAlias}.\"Shared\")",
            [new SqlArg("scopeOwnerId", uid)]);
    }
}
