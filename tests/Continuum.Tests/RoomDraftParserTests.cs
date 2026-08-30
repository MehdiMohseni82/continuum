using Continuum.Core.Domain;
using Continuum.Core.Generation;
using Xunit;

namespace Continuum.Tests;

public class RoomDraftParserTests
{
    private const string Full = """
        {
          "reply": "Here's the room I'd open.",
          "proposal": {
            "name": "Payments v2 — settlement contract",
            "topic": "Settle the ledger/adapter API before either side writes code.",
            "systemPrompt": "You are one of two agents. Produce the contract, not a discussion of it.",
            "doneCriteria": "A failing test in payments/ encodes the contract and the adapter makes it pass.",
            "languageMode": "Human",
            "language": "English",
            "agents": [
              {"name": "payments-api", "role": "implementer", "write": true, "responsibility": "Owns the adapter."},
              {"name": "ledger-svc", "role": "consultant", "write": false, "responsibility": "Reviews the contract."}
            ]
          }
        }
        """;

    [Fact]
    public void ReadsAFullProposal()
    {
        var (reply, p) = RoomDraftParser.Parse(Full);

        Assert.Equal("Here's the room I'd open.", reply);
        Assert.NotNull(p);
        Assert.Equal("Payments v2 — settlement contract", p!.Name);
        Assert.Equal(LanguageMode.Human, p.LanguageMode);
        Assert.Contains("failing test", p.DoneCriteria);
        Assert.Equal(2, p.Agents.Count);
        Assert.Equal("implementer", p.Agents[0].Role);
        Assert.True(p.Agents[0].Write);
    }

    [Fact]
    public void SurvivesCodeFencesAndPreamble()
    {
        // Every local model does this at least some of the time.
        var (reply, p) = RoomDraftParser.Parse("Sure! Here you go:\n```json\n" + Full + "\n```");
        Assert.NotNull(p);
        Assert.Equal("Here's the room I'd open.", reply);
    }

    [Fact]
    public void PlainProseIsKeptAsTheReply()
    {
        // Asking a clarifying question is a legitimate turn — don't discard it for lacking JSON.
        var (reply, p) = RoomDraftParser.Parse("Which repo owns the webhook receiver?");
        Assert.Equal("Which repo owns the webhook receiver?", reply);
        Assert.Null(p);
    }

    [Fact]
    public void NoProposalWhileStillAsking()
    {
        var (reply, p) = RoomDraftParser.Parse("""{"reply":"Two questions first."}""");
        Assert.Equal("Two questions first.", reply);
        Assert.Null(p);
    }

    [Theory]
    [InlineData("""{"reply":"x","proposal":{"topic":"only a topic"}}""")]
    [InlineData("""{"reply":"x","proposal":{"name":"only a name"}}""")]
    [InlineData("""{"reply":"x","proposal":{"name":"  ","topic":"  "}}""")]
    public void AHalfFilledProposalIsNoProposal(string raw)
    {
        // A room with a name and no topic looks ready to create. Worse than nothing.
        var (reply, p) = RoomDraftParser.Parse(raw);
        Assert.Equal("x", reply);
        Assert.Null(p);
    }

    [Fact]
    public void AConsultantNeverGetsWriteAccess()
    {
        // The model claiming write:true for a reviewer must not grant it — that is a repo it can edit.
        var (_, p) = RoomDraftParser.Parse("""
            {"reply":"r","proposal":{"name":"n","topic":"t","agents":[
              {"name":"reviewer","role":"consultant","write":true}]}}
            """);
        Assert.False(p!.Agents[0].Write);
        Assert.Equal("consultant", p.Agents[0].Role);
    }

    [Fact]
    public void AnUnknownRoleFallsBackToConsultant()
    {
        var (_, p) = RoomDraftParser.Parse("""
            {"reply":"r","proposal":{"name":"n","topic":"t","agents":[
              {"name":"a","role":"architect","write":true}]}}
            """);
        Assert.Equal("consultant", p!.Agents[0].Role);
        Assert.False(p.Agents[0].Write);
    }

    [Fact]
    public void MalformedAgentEntriesAreSkippedNotFatal()
    {
        var (_, p) = RoomDraftParser.Parse("""
            {"reply":"r","proposal":{"name":"n","topic":"t","agents":[
              "just a string", {"role":"implementer"}, {"name":"good","role":"implementer"}]}}
            """);
        Assert.Single(p!.Agents);
        Assert.Equal("good", p.Agents[0].Name);
    }

    [Fact]
    public void ShorthandModeIsHonouredAndAnythingElseStaysHuman()
    {
        var (_, shorthand) = RoomDraftParser.Parse(
            """{"reply":"r","proposal":{"name":"n","topic":"t","languageMode":"shorthand"}}""");
        Assert.Equal(LanguageMode.Shorthand, shorthand!.LanguageMode);

        var (_, odd) = RoomDraftParser.Parse(
            """{"reply":"r","proposal":{"name":"n","topic":"t","languageMode":"Klingon"}}""");
        Assert.Equal(LanguageMode.Human, odd!.LanguageMode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInputIsNotACrash(string? raw)
    {
        var (reply, p) = RoomDraftParser.Parse(raw);
        Assert.Equal("", reply);
        Assert.Null(p);
    }
}
