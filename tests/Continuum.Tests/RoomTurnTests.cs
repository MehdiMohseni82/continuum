using Continuum.Core.Rooms;
using Xunit;

namespace Continuum.Tests;

public class RoomTurnTests
{
    private static IReadOnlyList<(string From, string Body)> Recent(params (string, string)[] msgs) => msgs;

    // ---- silence sentinel (PASS) ----

    [Theory]
    [InlineData("PASS")]
    [InlineData("pass")]
    [InlineData("  PASS  ")]
    [InlineData("**PASS**")]
    [InlineData("\"PASS.\"")]
    [InlineData("`PASS`")]
    public void IsPass_TrueForBareSentinel(string body) => Assert.True(RoomTurn.IsPass(body));

    [Theory]
    [InlineData("")]
    [InlineData("PASS the config to the next stage")]
    [InlineData("I'll pass on that")]
    [InlineData("passing tests are green")]
    public void IsPass_FalseForRealContent(string body) => Assert.False(RoomTurn.IsPass(body));

    // ---- done marker ----

    [Theory]
    [InlineData("[DONE] shipped the parser")]
    [InlineData("[done]")]
    [InlineData("**[DONE]** summary here")]
    [InlineData("\"[DONE] wrapped up\"")]
    public void IsDone_TrueWhenPrefixed(string body) => Assert.True(RoomTurn.IsDone(body));

    [Theory]
    [InlineData("")]
    [InlineData("we are done here")]
    [InlineData("almost [DONE] but not quite")]
    public void IsDone_FalseOtherwise(string body) => Assert.False(RoomTurn.IsDone(body));

    // ---- trailing agent streak ----

    [Fact]
    public void Streak_CountsTrailingMembersUntilAHuman()
    {
        var members = new HashSet<string> { "alice", "bob" };
        var senders = new List<string> { "alice", "human", "bob", "alice", "bob" };
        Assert.Equal(3, RoomTurn.TrailingAgentStreak(senders, members)); // bob, alice, bob (stops at human)
    }

    [Fact]
    public void Streak_IsZeroWhenHumanSpokeLast()
    {
        var members = new HashSet<string> { "alice", "bob" };
        var senders = new List<string> { "alice", "bob", "human" };
        Assert.Equal(0, RoomTurn.TrailingAgentStreak(senders, members));
    }

    [Fact]
    public void Streak_CountsAllWhenNoHumanEverSpoke()
    {
        var members = new HashSet<string> { "alice", "bob" };
        var senders = new List<string> { "alice", "bob", "alice", "bob" };
        Assert.Equal(4, RoomTurn.TrailingAgentStreak(senders, members));
    }

    // ---- mention deadlock ----

    private static readonly string[] Three = ["alice", "bob", "carol"];

    [Fact]
    public void AnMentionHandsTheFloorToTheNamedMemberOnly()
    {
        var recent = Recent(("alice", "@bob what about the contract?"));

        Assert.Equal(RoomTurn.TurnKind.Speak, RoomTurn.Decide("bob", Three, recent, 1, 200).Kind);
        Assert.Equal(RoomTurn.TurnKind.Skip, RoomTurn.Decide("carol", Three, recent, 1, 200).Kind);
    }

    [Fact]
    public void OnceTheNamedMemberHasPassedTheFloorReopens()
    {
        // The deadlock this fixes: a three-agent room went silent because the last message named one
        // agent, that agent had nothing to add, and nobody else was ever allowed to speak again.
        var recent = Recent(("alice", "@bob what about the contract?"));

        var carol = RoomTurn.Decide("carol", Three, recent, 1, 200, mentionedHaveHadTheirTurn: true);
        Assert.Equal(RoomTurn.TurnKind.Speak, carol.Kind);
        Assert.Contains("passed", carol.Why);
    }

    [Fact]
    public void AnAgentThatAlreadyPassedDoesNotGetASecondTurn()
    {
        var recent = Recent(("alice", "@bob what about the contract?"));

        Assert.Equal(RoomTurn.TurnKind.Skip,
            RoomTurn.Decide("bob", Three, recent, 1, 200, mentionedHaveHadTheirTurn: true).Kind);
    }

    [Fact]
    public void TheAuthorNeverAnswersItself()
    {
        var recent = Recent(("alice", "@bob what about the contract?"));

        Assert.Equal(RoomTurn.TurnKind.Skip,
            RoomTurn.Decide("alice", Three, recent, 1, 200, mentionedHaveHadTheirTurn: true).Kind);
    }

    [Fact]
    public void ReopeningNeverOverridesATerminalRoom()
    {
        // Done and the cap must still win, or this would turn a backstop into a suggestion.
        var done = Recent(("alice", "[DONE] shipped"));
        Assert.Equal(RoomTurn.TurnKind.Done,
            RoomTurn.Decide("carol", Three, done, 1, 200, mentionedHaveHadTheirTurn: true).Kind);

        var capped = Recent(("alice", "@bob ping"));
        Assert.Equal(RoomTurn.TurnKind.Exhausted,
            RoomTurn.Decide("carol", Three, capped, 200, 200, mentionedHaveHadTheirTurn: true).Kind);
    }

    [Fact]
    public void MentionedMembersResolvesOnlyRealMembers()
    {
        var found = RoomTurn.MentionedMembers("@bob and @nobody and @CAROL", Three);
        Assert.Equal(new[] { "bob", "carol" }, found.OrderBy(x => x).ToArray());
    }

    // ---- decision ----

    private static readonly string[] Members = ["alice", "bob"];

    [Fact]
    public void Decide_FirstMemberGreetsEmptyRoom()
    {
        var d = RoomTurn.Decide("alice", Members, Recent(), 0, 16);
        Assert.Equal(RoomTurn.TurnKind.Speak, d.Kind);
    }

    [Fact]
    public void Decide_NonFirstMemberStaysQuietInEmptyRoom()
    {
        var d = RoomTurn.Decide("bob", Members, Recent(), 0, 16);
        Assert.Equal(RoomTurn.TurnKind.Skip, d.Kind);
    }

    [Fact]
    public void Decide_DoesNotReplyToOwnLastMessage()
    {
        var d = RoomTurn.Decide("bob", Members, Recent(("alice", "hi"), ("bob", "hey")), 2, 16);
        Assert.Equal(RoomTurn.TurnKind.Skip, d.Kind);
    }

    [Fact]
    public void Decide_RespondsToPeer()
    {
        var d = RoomTurn.Decide("bob", Members, Recent(("alice", "what do you think?")), 1, 16);
        Assert.True(d.IsTurn);
    }

    [Fact]
    public void Decide_DoneMarkerTerminatesRoom()
    {
        var d = RoomTurn.Decide("bob", Members, Recent(("alice", "[DONE] we shipped it")), 5, 16);
        Assert.Equal(RoomTurn.TurnKind.Done, d.Kind);
        Assert.True(d.IsTerminal);
    }

    [Fact]
    public void Decide_DoneWinsEvenBelowCapAndFromAnyone()
    {
        var d = RoomTurn.Decide("alice", Members, Recent(("bob", "[DONE] agreed")), 1, 16);
        Assert.Equal(RoomTurn.TurnKind.Done, d.Kind);
    }

    [Fact]
    public void Decide_ExhaustedAtCap()
    {
        var d = RoomTurn.Decide("bob", Members, Recent(("alice", "still going")), 16, 16);
        Assert.Equal(RoomTurn.TurnKind.Exhausted, d.Kind);
        Assert.True(d.IsTerminal);
    }

    [Fact]
    public void Decide_SpeaksJustBelowCap()
    {
        var d = RoomTurn.Decide("bob", Members, Recent(("alice", "still going")), 15, 16);
        Assert.True(d.IsTurn);
    }

    [Fact]
    public void Decide_CapOfZeroDisablesTermination()
    {
        var d = RoomTurn.Decide("bob", Members, Recent(("alice", "still going")), 999, 0);
        Assert.True(d.IsTurn);
    }

    [Fact]
    public void Decide_MentionGatesOthersOut()
    {
        var toAlice = Recent(("bob", "@alice can you take this?"));
        Assert.True(RoomTurn.Decide("alice", Members, toAlice, 1, 16).IsTurn);
        Assert.Equal(RoomTurn.TurnKind.Skip, RoomTurn.Decide("carol", ["alice", "bob", "carol"], toAlice, 1, 16).Kind);
    }

    [Fact]
    public void Decide_FreshHumanMessageIsAnsweredEvenAfterLongRun()
    {
        // Caller passes streak 0 because a human just spoke; the cap must not fire.
        var d = RoomTurn.Decide("alice", Members, Recent(("human", "please summarize")), 0, 16);
        Assert.True(d.IsTurn);
    }
}
