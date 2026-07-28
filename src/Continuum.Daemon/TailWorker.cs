using System.Security.Cryptography;
using System.Text;
using Continuum.Core.Contracts;
using Continuum.Core.Ingest;
using Microsoft.Extensions.Options;

namespace Continuum.Daemon;

/// <summary>
/// Polls the Claude Code project tree, tails each transcript from its saved byte offset,
/// parses new lines tolerantly, uploads them, and advances the cursor only after the
/// server acknowledges. First run naturally backfills existing files from offset 0.
/// </summary>
public sealed class TailWorker(
    ILogger<TailWorker> log,
    IOptions<DaemonOptions> options,
    CursorStore cursors,
    BackendClient backend) : BackgroundService
{
    private const int ReadWindow = 8 * 1024 * 1024; // cap bytes read per file per tick
    private readonly DaemonOptions _opt = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var projectsDir = Path.Combine(_opt.ClaudeDir, "projects");
        log.LogInformation("Continuum daemon watching {Dir} → {Backend} as '{Machine}'",
            projectsDir, _opt.BackendUrl, _opt.MachineName);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (Directory.Exists(projectsDir))
                    foreach (var file in Directory.EnumerateFiles(projectsDir, "*.jsonl", SearchOption.AllDirectories))
                    {
                        // Isolate per-file failures so one bad file never blocks the rest of the backfill.
                        try
                        {
                            await ProcessFileAsync(projectsDir, file, ct);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            log.LogWarning(ex, "Skipping file this cycle: {File}", Path.GetFileName(file));
                        }
                    }
                else
                    log.LogWarning("Projects directory not found: {Dir}", projectsDir);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Poll cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_opt.PollSeconds), ct);
        }
    }

    private async Task ProcessFileAsync(string projectsDir, string file, CancellationToken ct)
    {
        var (sessionId, projectKey) = Identify(projectsDir, file);
        var offset = cursors.GetOffset(file);

        FileStream fs;
        try
        {
            fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (IOException)
        {
            return; // momentarily locked; try again next tick
        }

        await using (fs)
        {
            var length = fs.Length;
            if (offset > length) offset = 0;         // truncated / rotated
            if (offset >= length) return;            // nothing new

            var window = (int)Math.Min(length - offset, ReadWindow);
            var buffer = new byte[window];
            fs.Seek(offset, SeekOrigin.Begin);
            fs.ReadExactly(buffer, 0, window);

            var lastNl = Array.LastIndexOf(buffer, (byte)'\n');
            if (lastNl < 0) return;                  // no complete line yet

            var events = new List<IngestEvent>();
            var start = 0;
            for (var i = 0; i <= lastNl; i++)
            {
                if (buffer[i] != (byte)'\n') continue;

                var len = i - start;
                if (len > 0 && buffer[i - 1] == (byte)'\r') len--; // strip CR
                var line = Encoding.UTF8.GetString(buffer, start, len);
                start = i + 1;

                var evt = JsonlParser.ParseLine(line, sessionId, projectKey);
                if (evt is not null) events.Add(evt);
            }

            var completeUpTo = offset + lastNl + 1;

            foreach (var chunk in Chunk(events, _opt.BatchSize))
            {
                var batch = new IngestBatch { MachineName = _opt.MachineName, Events = chunk };
                var result = await backend.UploadAsync(batch, ct);
                log.LogDebug("Sent {N} events from {File} ({Ins} new)",
                    chunk.Count, Path.GetFileName(file), result?.Inserted);
            }

            // Advance past all complete lines (covers blank/skipped lines too).
            cursors.SaveOffset(file, completeUpTo);
        }
    }

    private static (Guid SessionId, string ProjectKey) Identify(string projectsDir, string file)
    {
        var rel = Path.GetRelativePath(projectsDir, file);
        var projectKey = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

        var name = Path.GetFileNameWithoutExtension(file);
        var sessionId = Guid.TryParse(name, out var g) ? g : DeterministicGuid(file);
        return (sessionId, projectKey);
    }

    private static Guid DeterministicGuid(string s) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(s)).AsSpan(0, 16));

    private static IEnumerable<List<IngestEvent>> Chunk(List<IngestEvent> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.GetRange(i, Math.Min(size, items.Count - i));
    }
}
