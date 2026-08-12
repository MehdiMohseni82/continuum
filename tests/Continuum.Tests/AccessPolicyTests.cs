using Continuum.Core.Access;
using Continuum.Core.Domain;
using Xunit;

namespace Continuum.Tests;

/// <summary>
/// The rules these assert were previously restated at 45 call sites. They are also the tenant
/// boundary, so a change that lets one organization see another's data should fail loudly here.
/// </summary>
public class AccessPolicyTests
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    private static readonly Guid OrgA = Guid.Parse("11111111-0000-4000-8000-00000000000a");
    private static readonly Guid OrgB = Guid.Parse("22222222-0000-4000-8000-00000000000b");

    private sealed class FakePrincipal(Guid? id, bool isAdmin, Guid? orgId) : IAccessPrincipal
    {
        public Guid? UserId => id;
        public bool IsAdmin => id is not null && isAdmin;
        public Guid? OrgId => orgId;
    }

    private static AccessPolicy For(Guid? id, bool admin = false, Guid? org = null) =>
        new(new FakePrincipal(id, admin, org ?? OrgA));

    private static Session Session(Guid owner, bool shared = false, Guid? org = null) =>
        new() { Id = Guid.NewGuid(), OrgId = org ?? OrgA, OwnerId = owner, Shared = shared };

    private static MemoryItem Memory(Guid owner, bool shared = false, Guid? org = null) =>
        new() { Id = Guid.NewGuid(), OrgId = org ?? OrgA, OwnerId = owner, Shared = shared, Content = "x" };

    private static Room Room(Guid owner, Guid? org = null) =>
        new() { Id = Guid.NewGuid(), OrgId = org ?? OrgA, OwnerId = owner, Name = "r", Topic = "t", ChannelName = "room:x" };

    private static Workspace Workspace(Guid owner, Guid? org = null) =>
        new() { Id = Guid.NewGuid(), OrgId = org ?? OrgA, OwnerId = owner, ProjectKey = "k", DisplayName = "d" };

    // ---- the tenant boundary ----

    [Fact]
    public void NothingIsVisibleAcrossOrganizations()
    {
        var alice = For(Alice, org: OrgA);

        Assert.False(alice.VisibleSessions().Compile()(Session(Alice, org: OrgB)));
        Assert.False(alice.VisibleMemories().Compile()(Memory(Alice, org: OrgB)));
        Assert.False(alice.VisibleRooms().Compile()(Room(Alice, org: OrgB)));
    }

    [Fact]
    public void SharingDoesNotReachAcrossOrganizations()
    {
        // Shared means "shared with my organization", never with the whole instance.
        Assert.False(For(Alice, org: OrgA).VisibleSessions().Compile()(Session(Bob, shared: true, org: OrgB)));
        Assert.False(For(Alice, org: OrgA).VisibleMemories().Compile()(Memory(Bob, shared: true, org: OrgB)));
    }

    [Fact]
    public void AnAdministratorIsUnrestrictedWithinTheirOrganizationOnly()
    {
        var admin = For(Bob, admin: true, org: OrgA);

        Assert.True(admin.VisibleSessions().Compile()(Session(Alice, org: OrgA)));
        Assert.False(admin.VisibleSessions().Compile()(Session(Alice, org: OrgB)));
    }

    [Fact]
    public void ControlNeverCrossesOrganizations()
    {
        var alice = For(Alice, org: OrgA);
        var elsewhere = Memory(Alice, org: OrgB);

        Assert.False(alice.ControlledMemories().Compile()(elsewhere));
        Assert.False(alice.CanControl(elsewhere));
        Assert.False(alice.ControlledSessions().Compile()(Session(Alice, org: OrgB)));
        Assert.False(alice.ControlledWorkspaces().Compile()(Workspace(Alice, org: OrgB)));
    }

    [Fact]
    public void ACallerWithNoOrganizationSeesNothing()
    {
        // Null must read as "no organization", not "every organization".
        var stranded = new AccessPolicy(new FakePrincipal(Alice, isAdmin: true, orgId: null));

        Assert.False(stranded.VisibleSessions().Compile()(Session(Alice, org: OrgA)));
        Assert.False(stranded.VisibleMemories().Compile()(Memory(Alice, shared: true, org: OrgA)));
        Assert.False(stranded.VisibleRooms().Compile()(Room(Alice, org: OrgA)));
    }

    [Fact]
    public void WritesLandInTheCallersOrganization()
    {
        Assert.Equal(OrgB, For(Alice, org: OrgB).WriteOrgId);
    }

    [Fact]
    public void AMemberlessCallerWritesIntoThePreTenancyOrganization()
    {
        // The upgrade fallback: reachable only before memberships exist, never a cross-tenant write.
        var stranded = new AccessPolicy(new FakePrincipal(Alice, isAdmin: false, orgId: null));
        Assert.Equal(Defaults.DefaultOrgId, stranded.WriteOrgId);
    }

    // ---- sessions: read ----

    [Fact]
    public void Owner_SeesTheirOwnSession()
        => Assert.True(For(Alice).VisibleSessions().Compile()(Session(Alice)));

    [Fact]
    public void Owner_DoesNotSeeAnotherUsersPrivateSession()
        => Assert.False(For(Alice).VisibleSessions().Compile()(Session(Bob)));

    [Fact]
    public void AnyMember_SeesASharedSession()
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
        // owner clause is inert — an ordinary user controls none of them.
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
    public void Sql_AlwaysScopesToAnOrganization_EvenForAdmins()
    {
        // The tenant clause is never optional. An administrator is unrestricted within their
        // organization, not across the instance — so this must not come back unrestricted.
        var p = For(Alice, admin: true, org: OrgB).VisibleSessionsSql("s");

        Assert.False(p.IsUnrestricted);
        Assert.Contains("s.\"OrgId\"", p.Sql);
        Assert.Equal(OrgB, Assert.Single(p.Args).Value);
    }

    [Fact]
    public void Sql_ScopesToTheCallerAndUsesTheGivenAlias()
    {
        var p = For(Alice, org: OrgA).VisibleSessionsSql("ss");

        Assert.False(p.IsUnrestricted);
        Assert.Contains("ss.\"OrgId\"", p.Sql);
        Assert.Contains("ss.\"OwnerId\"", p.Sql);
        Assert.Contains("ss.\"Shared\"", p.Sql);
        Assert.Contains(p.Args, a => Equals(a.Value, OrgA));
        Assert.Contains(p.Args, a => Equals(a.Value, Alice));
    }

    /// <summary>
    /// The regression this guards: the previous code built these clauses by pasting the caller's id
    /// straight into the SQL text. Ids must travel as bound arguments, never as literal text.
    /// </summary>
    [Fact]
    public void Sql_NeverInlinesIdentifiersIntoTheQueryText()
    {
        var p = For(Alice, org: OrgA).VisibleSessionsSql("s");

        Assert.DoesNotContain(Alice.ToString(), p.Sql);
        Assert.DoesNotContain(OrgA.ToString(), p.Sql);
        foreach (var a in p.Args) Assert.Contains("@" + a.Name, p.Sql);
    }

    [Fact]
    public void Sql_ScopesAnonymousCallersToNobody()
    {
        var p = For(null, org: OrgA).VisibleSessionsSql("s");
        Assert.False(p.IsUnrestricted);
        Assert.Contains(p.Args, a => Equals(a.Value, Guid.Empty));
    }
}
