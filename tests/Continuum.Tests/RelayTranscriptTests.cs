using System.Text.Json;
using Continuum.Cli;
using Xunit;

namespace Continuum.Tests;

/// <summary>
/// The relay's read of a session transcript. Everything here is a way the relay can silently do
/// nothing: it exits 0 whether it relayed a turn or misparsed the input, so a wrong answer looks
/// exactly like a session that simply isn't in a room.
/// </summary>
public class RelayTranscriptTests
{
    private const string Room = "7aa33481-e6f0-4029-a6c6-76c5d2de1731";
    private static string Marker(string agent = "alice", string room = Room) =>
        $"<<CONTINUUM-ROOM room={room} agent={agent} channel=room:947df5bd5823>>";

    // ---- hook payload ----

    [Fact]
    public void HookInput_BindsClaudeCodesSnakeCase()
    {
        // The shipped payload verbatim. JsonSerializerDefaults.Web relaxes case but not word
        // separators, so without explicit names every field is null and the relay never relays.
        var input = HookInput.Parse(
            """{"session_id":"abc-123","transcript_path":"/tmp/t.jsonl","cwd":"/repo","hook_event_name":"Stop"}""");

        Assert.NotNull(input);
        Assert.Equal("abc-123", input!.SessionId);
        Assert.Equal("/tmp/t.jsonl", input.TranscriptPath);
        Assert.Equal("/repo", input.Cwd);
    }

    [Fact]
    public void HookInput_ReturnsNullOnGarbage() => Assert.Null(HookInput.Parse("not json"));

    // ---- bind marker ----

    [Fact]
    public void CurrentBind_FindsRoomAgentAndChannel()
    {
        var bind = Transcript.CurrentBind($"chatter\n{Marker()}\nmore chatter");

        Assert.NotNull(bind);
        Assert.Equal(Guid.Parse(Room), bind!.RoomId);
        Assert.Equal("alice", bind.Agent);
        Assert.Equal("room:947df5bd5823", bind.Channel);
    }

    [Fact]
    public void CurrentBind_NullWhenNeverJoined() =>
        Assert.Null(Transcript.CurrentBind("an ordinary session with no room in it"));

    [Fact]
    public void CurrentBind_NullAfterLeaving() =>
        Assert.Null(Transcript.CurrentBind($"{Marker()}\nsome turns\n<<CONTINUUM-ROOM-LEAVE>>"));

    [Fact]
    public void CurrentBind_RejoiningAfterLeaveBindsAgain()
    {
        // A leave only ends the bind that preceded it — order in the transcript is what decides.
        var bind = Transcript.CurrentBind($"{Marker()}\n<<CONTINUUM-ROOM-LEAVE>>\n{Marker("bob")}");

        Assert.NotNull(bind);
        Assert.Equal("bob", bind!.Agent);
    }

    [Fact]
    public void CurrentBind_LastJoinWins()
    {
        var other = Guid.NewGuid().ToString();
        var bind = Transcript.CurrentBind($"{Marker()}\n{Marker("bob", other)}");

        Assert.Equal(Guid.Parse(other), bind!.RoomId);
        Assert.Equal("bob", bind.Agent);
    }

    [Fact]
    public void CurrentBind_NullWhenRoomIdIsNotAGuid() =>
        Assert.Null(Transcript.CurrentBind("<<CONTINUUM-ROOM room=not-a-guid agent=alice channel=c>>"));

    // ---- last assistant message ----

    [Fact]
    public void LastAssistantMessage_TakesTheLatestTextAndItsUsage()
    {
        var path = WriteTranscript(
            Assistant("first turn"),
            User("a human interjects"),
            Assistant("second turn", input: 1200, output: 42, cacheRead: 8000, cacheCreation: 100));

        var (text, usage) = Transcript.LastAssistantMessage(path);

        Assert.Equal("second turn", text);
        Assert.Equal(1200, usage!.InputTokens);
        Assert.Equal(42, usage.OutputTokens);
        Assert.Equal(8000, usage.CacheReadInputTokens);
        Assert.Equal(100, usage.CacheCreationInputTokens);
    }

    [Fact]
    public void LastAssistantMessage_ConcatenatesTextBlocksAndIgnoresToolUse()
    {
        // A turn that ends in a tool call still has prose worth posting; the tool block itself is
        // internal detail the peer must not receive.
        var line = JsonSerializer.Serialize(new
        {
            type = "assistant",
            message = new
            {
                role = "assistant",
                content = new object[]
                {
                    new { type = "text", text = "Running the test. " },
                    new { type = "tool_use", id = "t1", name = "Bash", input = new { command = "dotnet test" } },
                    new { type = "text", text = "Standing by." },
                },
            },
        });

        var (text, _) = Transcript.LastAssistantMessage(WriteTranscript(line));

        Assert.Equal("Running the test. Standing by.", text);
    }

    [Fact]
    public void LastAssistantMessage_SkipsTurnsWithNoProse()
    {
        // A tool-only turn must not blank out what the session actually last said — otherwise the
        // relay posts nothing and both agents sit waiting for each other.
        var toolOnly = JsonSerializer.Serialize(new
        {
            type = "assistant",
            message = new
            {
                role = "assistant",
                content = new object[] { new { type = "tool_use", id = "t1", name = "Read", input = new { } } },
            },
        });

        var (text, _) = Transcript.LastAssistantMessage(WriteTranscript(Assistant("the real message"), toolOnly));

        Assert.Equal("the real message", text);
    }

    [Fact]
    public void LastAssistantMessage_ToleratesMalformedLines()
    {
        var path = WriteTranscript("{ this is not json", "", Assistant("still readable"));

        var (text, _) = Transcript.LastAssistantMessage(path);

        Assert.Equal("still readable", text);
    }

    [Fact]
    public void LastAssistantMessage_NullWhenTheSessionNeverSpoke()
    {
        var (text, usage) = Transcript.LastAssistantMessage(WriteTranscript(User("hello?")));

        Assert.Null(text);
        Assert.Null(usage);
    }

    // ---- handshake ----

    [Theory]
    [InlineData("ready")]
    [InlineData("Ready.")]
    [InlineData("**ready**")]
    [InlineData("  ready  ")]
    public void IsReadyHandshake_TrueForTheBareWord(string body) =>
        Assert.True(Transcript.IsReadyHandshake(body));

    [Theory]
    [InlineData("ready when you are — what's the goal?")]
    [InlineData("The build is ready")]
    [InlineData("")]
    public void IsReadyHandshake_FalseForRealContent(string body) =>
        Assert.False(Transcript.IsReadyHandshake(body));

    // ---- fixtures ----

    private static string Assistant(string text, int? input = null, int? output = null,
                                    int? cacheRead = null, int? cacheCreation = null)
    {
        var message = new Dictionary<string, object?>
        {
            ["role"] = "assistant",
            ["content"] = new object[] { new { type = "text", text } },
        };
        if (input is not null)
            message["usage"] = new Dictionary<string, object?>
            {
                ["input_tokens"] = input,
                ["output_tokens"] = output,
                ["cache_read_input_tokens"] = cacheRead,
                ["cache_creation_input_tokens"] = cacheCreation,
            };
        return JsonSerializer.Serialize(new { type = "assistant", message });
    }

    private static string User(string text) => JsonSerializer.Serialize(new
    {
        type = "user",
        message = new { role = "user", content = new object[] { new { type = "text", text } } },
    });

    private static string WriteTranscript(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"continuum-relay-test-{Guid.NewGuid():N}.jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }
}
