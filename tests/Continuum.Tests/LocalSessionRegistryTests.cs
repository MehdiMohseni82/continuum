using Continuum.Core.Sessions;
using Xunit;

namespace Continuum.Tests;

public class LocalSessionRegistryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "continuum-sessions-" + Guid.NewGuid().ToString("N"));

    public LocalSessionRegistryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private void WriteRaw(string name, string json) => File.WriteAllText(Path.Combine(_dir, name), json);

    /// <summary>A registry file in the exact shape Claude Code 2.1.224 writes.</summary>
    private void WriteSession(int pid, string sessionId, string name, string status = "idle", long startedAt = 1786101635857)
        => WriteRaw($"{pid}.json", $$"""
        {"pid":{{pid}},"sessionId":"{{sessionId}}","cwd":"/Users/me/dev/{{name}}",
         "startedAt":{{startedAt}},"procStart":"Fri Aug  7 11:20:35 2026","version":"2.1.224",
         "peerProtocol":1,"kind":"interactive","entrypoint":"cli",
         "messagingSocketPath":"/tmp/cc-socks/{{pid}}.sock","name":"{{name}}","nameSource":"derived",
         "status":"{{status}}","updatedAt":1786120773639,"statusUpdatedAt":1786120773639}
        """);

    private LocalSessionRegistry Registry(Func<int, bool>? alive = null) => new(_dir, alive ?? (_ => true));

    // ---- parsing the real shape ----

    [Fact]
    public void Read_ParsesTheRegistryShapeClaudeCodeWrites()
    {
        WriteSession(5362, "0c1dd9ae-593b-44f3-924c-6b5c73acf0a6", "jarvis-08");

        var s = Assert.Single(Registry().Read());

        Assert.Equal(5362, s.Pid);
        Assert.Equal("jarvis-08", s.Name);
        Assert.Equal("/Users/me/dev/jarvis-08", s.Cwd);
        Assert.Equal("interactive", s.Kind);
        Assert.Equal("cli", s.Entrypoint);
        Assert.Equal("2.1.224", s.Version);
        Assert.Equal("/tmp/cc-socks/5362.sock", s.MessagingSocketPath);
        Assert.Equal(Guid.Parse("0c1dd9ae-593b-44f3-924c-6b5c73acf0a6"), s.SessionGuid);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1786101635857), s.StartedAt);
        Assert.False(s.IsBusy);
    }

    [Fact]
    public void Read_ReadsBusyStatus()
    {
        WriteSession(1, "3f1c1d2e-0000-4000-8000-000000000001", "geonoai-73", status: "busy");
        Assert.True(Assert.Single(Registry().Read()).IsBusy);
    }

    [Fact]
    public void Read_OrdersNewestFirst()
    {
        WriteSession(1, "3f1c1d2e-0000-4000-8000-000000000001", "older", startedAt: 1_000_000);
        WriteSession(2, "3f1c1d2e-0000-4000-8000-000000000002", "newer", startedAt: 9_000_000);

        Assert.Equal(["newer", "older"], Registry().Read().Select(s => s.Name));
    }

    // ---- tolerance: a newer Claude Code must never stop the daemon ----

    [Fact]
    public void Read_SkipsMalformedFilesButKeepsTheRest()
    {
        WriteSession(1, "3f1c1d2e-0000-4000-8000-000000000001", "good");
        WriteRaw("2.json", "{ this is not json");
        WriteRaw("3.json", "");

        Assert.Equal(["good"], Registry().Read().Select(s => s.Name));
    }

    [Fact]
    public void Read_IgnoresUnknownFieldsFromANewerVersion()
    {
        WriteRaw("7.json", """
        {"pid":7,"sessionId":"3f1c1d2e-0000-4000-8000-000000000007","cwd":"/w","name":"future",
         "status":"idle","kind":"interactive","entrypoint":"cli","version":"9.9.9",
         "messagingSocketPath":"/tmp/x.sock","startedAt":1786101635857,"updatedAt":1786120773639,
         "somethingBrandNew":{"nested":[1,2,3]},"anotherNewFlag":true}
        """);

        var s = Assert.Single(Registry().Read());
        Assert.Equal("future", s.Name);
        Assert.Equal("9.9.9", s.Version);
    }

    [Fact]
    public void Read_SkipsEntriesWithoutAUsableIdentity()
    {
        WriteRaw("1.json", """{"pid":0,"sessionId":"3f1c1d2e-0000-4000-8000-000000000001"}""");
        WriteRaw("2.json", """{"pid":2,"sessionId":""}""");
        WriteRaw("3.json", """{"cwd":"/w","name":"no-pid-no-id"}""");

        Assert.Empty(Registry().Read());
    }

    [Fact]
    public void Read_KeepsAnUnparsableSessionIdRatherThanDroppingTheSession()
    {
        WriteRaw("4.json", """
        {"pid":4,"sessionId":"not-a-guid","cwd":"/w","name":"odd","status":"idle","startedAt":1786101635857}
        """);

        var s = Assert.Single(Registry().Read());
        Assert.Equal("not-a-guid", s.SessionId);
        Assert.Null(s.SessionGuid);
    }

    [Fact]
    public void Read_TreatsMissingTimestampsAsUnknown()
    {
        WriteRaw("5.json", """{"pid":5,"sessionId":"3f1c1d2e-0000-4000-8000-000000000005","name":"n"}""");

        var s = Assert.Single(Registry().Read());
        Assert.Null(s.StartedAt);
        Assert.Null(s.UpdatedAt);
    }

    [Fact]
    public void Read_ReturnsEmptyWhenTheDirectoryIsAbsent()
        => Assert.Empty(new LocalSessionRegistry(Path.Combine(_dir, "nope")).Read());

    // ---- liveness ----

    [Fact]
    public void ReadLive_DropsSessionsWhoseProcessHasExited()
    {
        WriteSession(100, "3f1c1d2e-0000-4000-8000-000000000100", "alive");
        WriteSession(200, "3f1c1d2e-0000-4000-8000-000000000200", "exited");

        var registry = Registry(alive: pid => pid == 100);

        Assert.Equal(["alive", "exited"], registry.Read().Select(s => s.Name).Order());
        Assert.Equal(["alive"], registry.ReadLive().Select(s => s.Name));
    }

    [Fact]
    public void HasInbox_FalseWhenTheSocketIsGone()
    {
        WriteSession(300, "3f1c1d2e-0000-4000-8000-000000000300", "no-socket");
        Assert.False(Assert.Single(Registry().Read()).HasInbox);
    }
}
