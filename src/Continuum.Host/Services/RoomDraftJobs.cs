using System.Collections.Concurrent;
using Continuum.Core.Contracts;

namespace Continuum.Host.Services;

/// <summary>
/// Drafting runs as a background job that the client polls, rather than as one long request.
///
/// <para>
/// It has to. Cloudflare cuts any request off at 100 seconds (error 524), nginx has its own read
/// timeout, and a 7B model working through a real specification document routinely takes longer than
/// either. Streaming heartbeats would also keep the connection alive, but only if proxy buffering is
/// off all the way down — and the nginx snippet in front of this lives on the server, outside this
/// repo. Polling needs nothing from the proxies: every request returns in milliseconds.
/// </para>
/// </summary>
public sealed class RoomDraftJobs(IServiceScopeFactory scopes, ILogger<RoomDraftJobs> log)
{
    /// <summary>A model that has wedged must not hold a slot forever.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(6);

    /// <summary>Long enough for a slow client to collect a result, short enough not to accumulate.</summary>
    private static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(30);

    private const int MaxJobs = 200;

    private sealed class Job
    {
        public string Status = "running";
        public RoomDraftResponse? Result;
        public string? Error;
        public DateTimeOffset Finished;
    }

    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();

    /// <summary>Queue a draft and return its id. Never blocks on the model.</summary>
    public Guid Start(RoomDraftRequest req)
    {
        Prune();

        var id = Guid.NewGuid();
        var job = new Job();
        _jobs[id] = job;

        // Deliberately not awaited: the HTTP request that started this is about to end. That also
        // means the request's DI scope is about to be disposed, so the work gets a scope of its own —
        // resolving the service from the request scope would use a disposed DbContext.
        _ = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(Budget);
            try
            {
                using var scope = scopes.CreateScope();
                var draft = scope.ServiceProvider.GetRequiredService<RoomDraftService>();
                job.Result = await draft.DraftAsync(req, cts.Token);
                job.Status = "done";
            }
            catch (OperationCanceledException)
            {
                job.Error = $"The drafting model did not answer within {Budget.TotalMinutes:0} minutes.";
                job.Status = "failed";
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Room draft job {Id} failed", id);
                job.Error = ex.Message;
                job.Status = "failed";
            }
            finally
            {
                job.Finished = DateTimeOffset.UtcNow;
            }
        });

        return id;
    }

    /// <summary>The job's state, or null if it never existed or has already been pruned.</summary>
    public RoomDraftJobDto? Get(Guid id) =>
        _jobs.TryGetValue(id, out var j)
            ? new RoomDraftJobDto(id, j.Status, j.Result, j.Error)
            : null;

    /// <summary>
    /// Drop finished jobs past their keep window, and — if something has gone badly wrong and the
    /// dictionary is full of stuck ones — the oldest finished entries regardless.
    /// </summary>
    private void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow - KeepFinished;
        foreach (var (id, job) in _jobs)
            if (job.Status != "running" && job.Finished < cutoff)
                _jobs.TryRemove(id, out _);

        if (_jobs.Count < MaxJobs) return;

        foreach (var (id, _) in _jobs
                     .Where(kv => kv.Value.Status != "running")
                     .OrderBy(kv => kv.Value.Finished)
                     .Take(_jobs.Count - MaxJobs + 1))
            _jobs.TryRemove(id, out _);
    }
}
