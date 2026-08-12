using Continuum.Core.Access;
using Continuum.Core.Domain;
using Xunit;

namespace Continuum.Tests;

/// <summary>
/// The rules these assert are the ones that were previously restated at 45 call sites. They are the
/// behaviour multi-tenancy will extend, so they're pinned here first — a change to the policy that
/// alters what one user may see about another should fail loudly.
/// </summary>
public class AccessPolicyTests
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    private sealed class FakePrincipal(Guid? id, bool isAdmin) : IAccessPrincipal
    {
        public Guid? UserId => id;
        public bool IsAdmin => id is not null && isAdmin;
    }

    private static AccessPolicy For(Guid? id, bool admin = false) => new(new FakePrincipal(id, admin));

    private static Session Session(Guid owner, bool shared = false) =>
        new() { Id = Guid.NewGuid(), OwnerId = owner, Shared = shared };

    private static MemoryItem Memory(Guid owner, bool shared = false) =>
        new() { Id = Guid.NewGuid(), OwnerId = owner, Shared = shared, Content = "x" };

    private static Room Room(Guid owner) =>
        new() { Id = Guid.NewGuid(), OwnerId = owner, Name = "r", Topic = "t", ChannelName = "room:x" };

    private static Workspace Workspace(Guid owner) =>
        new() { Id = Guid.NewGuid(), OwnerId = owner, ProjectKey = "k", DisplayName = "d" };

    // ---- sessions: read ----

    [Fact]
    public void Owner_SeesTheirOwnSession()
        => Assert.True(For(Alice).VisibleSessions().Compile()(Session(Alice)));

    [Fact]
    public void Owner_DoesNotSeeAnotherUsersPrivateSession()
        => Assert.False(For(Alice).VisibleSessions().Compile()(Session(Bob)));

    [Fact]
    public void AnyUser_SeesASharedSession()
        => Assert.True(For(Alice).VisibleSessions().Compile()(Session(Bob, shared: true)));

    [Fact]
    public void Admin_SeesEveryoneElsesPrivateSessions()
        => Assert.True(For(Bob, admin: true).VisibleSessions().Compile()(Session(Alice)));

    [Fact]
    public void Anonymous_SeesOnlySharedSessions()
    {
        var visible = For(null).VisibleSessions().Compile();
        Assert.False(visible(Session(Alice)));
        Assert.True(visible(Session(Alice, shared: true)));
    }

    // ---- the distinction that matters: seeing is not controlling ----

    [Fact]
    public void BeingShownASharedSession_DoesNotAllowChangingIt()
    {
        var shared = Session(Bob, shared: true);
        var policy = For(Alice);

        Assert.True(policy.VisibleSessions().Compile()(shared));
        Assert.False(policy.ControlledSessions().Compile()(shared));
    }

    [Fact]
    public void Owner_ControlsTheirOwnSession()
        => Assert.True(For(Alice).ControlledSessions().Compile()(Session(Alice)));

    [Fact]
    public void BeingShownASharedMemory_DoesNotAllowChangingIt()
    {
        var shared = Memory(Bob, shared: true);
        var policy = For(Alice);

        Assert.True(policy.VisibleMemories().Compile()(shared));
        Assert.False(policy.ControlledMemories().Compile()(shared));
        Assert.False(policy.CanControl(shared));
    }

    [Fact]
    public void CanControl_AgreesWithTheQueryForm()
    {
        var mine = Memory(Alice);
        var policy = For(Alice);
        Assert.Equal(policy.ControlledMemories().Compile()(mine), policy.CanControl(mine));
    }

    // ---- workspaces are shared infrastructure, not one person's property ----

    [Fact]
    public void Workspaces_AreRenameableByAdminsOnly()
    {
        // Ingest never stamps Workspace.OwnerId, so every workspace carries the default owner and the
        // owner clause is inert — an ordinary user controls none of them. Pinned so that when phase 2
        // gives workspaces real scoping, the change in behaviour is visible here rather than silent.
        var ordinary = Workspace(Defaults.DefaultOwnerId);

        Assert.False(For(Alice).ControlledWorkspaces().Compile()(ordinary));
        Assert.True(For(Alice, admin: true).ControlledWorkspaces().Compile()(ordinary));
    }

    [Fact]
    public void Workspaces_AreRenameableByTheirOwnerOnceOwnershipIsReal()
    {
        Assert.True(For(Alice).ControlledWorkspaces().Compile()(Workspace(Alice)));
        Assert.False(For(Alice).ControlledWorkspaces().Compile()(Workspace(Bob)));
    }

    // ---- rooms have no shared path ----

    [Fact]
    public void Rooms_AreVisibleOnlyToTheirOwnerAndAdmins()
    {
        Assert.True(For(Alice).VisibleRooms().Compile()(Room(Alice)));
        Assert.False(For(Alice).VisibleRooms().Compile()(Room(Bob)));
        Assert.True(For(Bob, admin: true).VisibleRooms().Compile()(Room(Alice)));
    }

    // ---- raw SQL ----

    [Fact]
    public void Sql_IsUnrestrictedForAdmins()
    {
        var p = For(Alice, admin: true).VisibleSessionsSql("s");
        Assert.True(p.IsUnrestricted);
        Assert.Empty(p.Args);
    }

    [Fact]
    public void Sql_ScopesToTheCallerAndUsesTheGivenAlias()
    {
        var p = For(Alice).VisibleSessionsSql("ss");

        Assert.False(p.IsUnrestricted);
        Assert.Contains("ss.\"OwnerId\"", p.Sql);
        Assert.Contains("ss.\"Shared\"", p.Sql);
        Assert.Equal(Alice, Assert.Single(p.Args).Value);
    }

    /// <summary>
    /// The regression this guards: the previous code built these clauses by pasting the caller's id
    /// straight into the SQL text. The id must travel as a bound argument, never as literal text.
    /// </summary>
    [Fact]
    public void Sql_NeverInlinesTheCallerIdIntoTheQueryText()
    {
        var p = For(Alice).VisibleSessionsSql("s");

        Assert.DoesNotContain(Alice.ToString(), p.Sql);
        Assert.Contains("@" + p.Args[0].Name, p.Sql);
    }

    [Fact]
    public void Sql_ScopesAnonymousCallersToNobody()
    {
        var p = For(null).VisibleSessionsSql("s");
        Assert.False(p.IsUnrestricted);
        Assert.Equal(Guid.Empty, Assert.Single(p.Args).Value);
    }
}
