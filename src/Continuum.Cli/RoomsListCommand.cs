using Continuum.Core.Contracts;

namespace Continuum.Cli;

/// <summary>
/// <c>continuum rooms</c> — rooms you can see, with their ids.
///
/// The id is what `join` needs and nothing else surfaces it: today people copy the GUID out of the
/// browser's address bar.
/// </summary>
public static class RoomsListCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var cfg = Config.Load();
        if (cfg is null)
        {
            Console.Error.WriteLine("Continuum is not configured. Run `continuum doctor`.");
            return 1;
        }

        using var api = new Api(cfg);
        List<RoomDto>? rooms;
        try { rooms = await api.GetAsync<List<RoomDto>>("/api/rooms", ct); }
        catch (Exception ex) { Console.Error.WriteLine($"Could not reach {cfg.Backend}: {ex.Message}"); return 1; }

        if (rooms is null || rooms.Count == 0)
        {
            Console.WriteLine("No rooms are visible to this token. Create one in the web UI.");
            return 0;
        }

        // Open rooms first — they're the only ones you can join.
        foreach (var r in rooms.OrderBy(r => r.Status == "open" ? 0 : 1).ThenByDescending(r => r.LastActivityAt))
        {
            Console.WriteLine($"{r.Id}  {r.Status,-6}  {r.Name}");
            if (!string.IsNullOrWhiteSpace(r.Topic)) Console.WriteLine($"{"",38}{Truncate(r.Topic, 90)}");
            Console.WriteLine($"{"",38}{r.MemberCount} member(s), {r.MessageCount} message(s), channel #{r.ChannelName}");
        }

        Console.WriteLine();
        Console.WriteLine("Join one with:  /continuum-joinroom <room-id> <your-agent-name>");
        return 0;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max - 1), "…");
}
