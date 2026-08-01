using Microsoft.Extensions.DependencyInjection;

namespace Continuum.Host.Services;

/// <summary>
/// The autonomous ("push") side of the server-side room agent. Every <c>IntervalSeconds</c> it wakes
/// each configured agent to take at most one turn in the open rooms it belongs to — the backend
/// equivalent of the daemon's local-CLI room runner, but driven by the Claude API. Only started when
/// <see cref="ServerAgentOptions.Enabled"/> is true and a key is configured.
///
/// Pacing: one turn per room per cycle (avoids two server agents flooding a room), turns are awaited
/// sequentially so a scoped driver/DbContext never outlives its scope, and the loop swallows every
/// non-shutdown exception so a transient API/network error can't silently kill it.
/// </summary>
public sealed class ServerAgentWorker(
    IServiceScopeFactory scopes,
    ServerAgentOptions options,
    ILogger<ServerAgentWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Enabled || options.Agents.Count == 0 || !options.HasKey())
        {
            log.LogInformation("Server room agent disabled (Enabled={Enabled}, agents={Count}, key={HasKey}).",
                options.Enabled, options.Agents.Count, options.HasKey());
            return;
        }

        log.LogInformation("Server room agent started ({Count} agent(s), interval {Interval}s, model {Model}).",
            options.Agents.Count, options.IntervalSeconds, options.Model);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break;
                // Catch everything (incl. a stray TaskCanceledException from an HTTP timeout) so the
                // loop survives — the daemon's runner had a filter that let those kill the loop.
                log.LogError(ex, "Server room agent cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, options.IntervalSeconds)), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var driver = scope.ServiceProvider.GetRequiredService<ServerAgentDriver>();

        var rooms = await driver.LoadOpenContextsAsync(ct);
        foreach (var room in rooms)
        {
            // One server-agent turn per room per cycle: the first configured agent whose turn it is.
            foreach (var agent in options.Agents)
            {
                if (string.IsNullOrWhiteSpace(agent.Name) || !room.MemberNames.Contains(agent.Name)) continue;

                var decision = ServerAgentDriver.DecideTurn(agent.Name, room.MemberNames, room.Recent);
                if (!decision.IsTurn) continue;

                log.LogInformation("Waking '{Agent}' in '{Room}' -> {Why}", agent.Name, room.Name, decision.Why);
                var posted = await driver.TakeTurnAsync(room.RoomId, agent.Name, steer: null, ct);
                if (posted is null)
                    log.LogDebug("'{Agent}' produced no message in '{Room}' (refusal or empty).", agent.Name, room.Name);
                break; // move to the next room
            }
        }
    }
}
