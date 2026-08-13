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

    private static readonly Guid Carol = Guid.Parse("cccccccc-0000-4000-8000-000000000003");
    private static readonly Guid TeamX = Guid.Parse("dddddddd-0000-4000-8000-00000000000f");

    private static readonly Guid OrgA = Guid.Parse("11111111-0000-4000-8000-00000000000a");
    private static readonly Guid OrgB = Guid.Parse("22222222-0000-4000-8000-00000000000b");

    private sealed class FakePrincipal(Guid? id, bool isAdmin, Guid? orgId, IReadOnlyList<Guid>? teams = null) : IAccessPrincipal
    {
        public Guid? UserId => id;
        public bool IsAdmin => id is not null && isAdmin;
        public Guid? OrgId => orgId;
        public IReadOnlyList<Guid> TeamIds => teams ?? [];
    }

    /// <summary>Grants held in memory: the policy composes a query, so a list exercises the same rules.</summary>
    private sealed class FakeGrants(params Grant[] grants) : IGrantSource
    {
        public IQueryable<Grant> Grants => grants.AsQueryable();
    }

    private static AccessPolicy For(Guid? id, bool admin = false, Guid? org = null,
                                    IReadOnlyList<Guid>? teams = null, params Grant[] grants) =>
        new(new FakePrincipal(id, admin, org ?? OrgA, teams), new FakeGrants(grants));

    private static Grant GrantTo(GrantResource type, Guid resourceId, GrantPrincipal principalType, Guid principalId,
                                 Guid? org = null, DateTimeOffset? expiresAt = null) =>
        new()
        {
            Id = Guid.NewGuid(), OrgId = org ?? OrgA,
            ResourceType = type, ResourceId = resourceId,
            PrincipalType = principalType, PrincipalId = principalId,
            Access = GrantAccess.Read, CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = expiresAt,
        };

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
        var stranded = new AccessPolicy(new FakePrincipal(Alice, isAdmin: true, orgId: null), new FakeGrants());

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
        var stranded = new AccessPolicy(new FakePrincipal(Alice, isAdmin: false, orgId: null), new FakeGrants());
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

    // ---- grants: sharing with named people and teams ----

    [Fact]
    public void AGrantToMeMakesAPrivateSessionVisible()
    {
        var theirs = Session(Bob);
        var policy = For(Alice, grants: GrantTo(GrantResource.Session, theirs.Id, GrantPrincipal.User, Alice));

        Assert.True(policy.VisibleSessions().Compile()(theirs));
    }

    [Fact]
    public void AGrantToSomeoneElseDoesNotMakeItVisibleToMe()
    {
        var theirs = Session(Bob);
        var policy = For(Alice, grants: GrantTo(GrantResource.Session, theirs.Id, GrantPrincipal.User, Carol));

        Assert.False(policy.VisibleSessions().Compile()(theirs));
    }

    [Fact]
    public void AGrantToATeamIAmInMakesItVisible()
    {
        var theirs = Session(Bob);
        var policy = For(Alice, teams: [TeamX], grants: GrantTo(GrantResource.Session, theirs.Id, GrantPrincipal.Team, TeamX));

        Assert.True(policy.VisibleSessions().Compile()(theirs));
    }

    [Fact]
    public void AGrantToATeamIAmNotInDoesNothing()
    {
        var theirs = Session(Bob);
        var policy = For(Alice, teams: [], grants: GrantTo(GrantResource.Session, theirs.Id, GrantPrincipal.Team, TeamX));

        Assert.False(policy.VisibleSessions().Compile()(theirs));
    }

    [Fact]
    public void AnExpiredGrantConfersNothing()
    {
        var theirs = Session(Bob);
        var expired = GrantTo(GrantResource.Session, theirs.Id, GrantPrincipal.User, Alice,
                              expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.False(For(Alice, grants: expired).VisibleSessions().Compile()(theirs));
    }

    [Fact]
    public void AGrantFromAnotherOrganizationConfersNothing()
    {
        // Belt and braces: the resource is out of reach anyway, but a stray grant row must not help.
        var theirs = Session(Bob, org: OrgB);
        var policy = For(Alice, org: OrgA,
                         grants: GrantTo(GrantResource.Session, theirs.Id, GrantPrincipal.User, Alice, org: OrgB));

        Assert.False(policy.VisibleSessions().Compile()(theirs));
    }

    [Fact]
    public void AGrantOfTheWrongKindDoesNotLeakAcrossResourceTypes()
    {
        var theirs = Session(Bob);
        // A memory grant that happens to carry the same id must not unlock the session.
        var policy = For(Alice, grants: GrantTo(GrantResource.Memory, theirs.Id, GrantPrincipal.User, Alice));

        Assert.False(policy.VisibleSessions().Compile()(theirs));
    }

    [Fact]
    public void AGrantConfersReadingButNeverControl()
    {
        var theirs = Session(Bob);
        var policy = For(Alice, grants: GrantTo(GrantResource.Session, theirs.Id, GrantPrincipal.User, Alice));

        Assert.True(policy.VisibleSessions().Compile()(theirs));
        Assert.False(policy.ControlledSessions().Compile()(theirs));
    }

    [Fact]
    public void GrantsWorkForMemoriesAndRoomsToo()
    {
        var mem = Memory(Bob);
        var room = Room(Bob);

        Assert.True(For(Alice, grants: GrantTo(GrantResource.Memory, mem.Id, GrantPrincipal.User, Alice))
            .VisibleMemories().Compile()(mem));
        Assert.True(For(Alice, grants: GrantTo(GrantResource.Room, room.Id, GrantPrincipal.User, Alice))
            .VisibleRooms().Compile()(room));
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
