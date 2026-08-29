using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Continuum.Core.Contracts;
using Continuum.Core.Domain;
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
    private const int CwdScanLines = 200;           // how far into a transcript to look for its cwd

    private readonly DaemonOptions _opt = options.Value;

    /// <summary>
    /// Project directory name → the working directory its sessions ran in, recovered from the
    /// transcripts themselves. The mangled directory name can't be reversed (every non-alphanumeric
    /// character became the same dash), but each transcript records its own <c>cwd</c>.
    /// A directory's cwd never changes, so this is cached for the life of the process.
    /// </summary>
    private readonly ConcurrentDictionary<string, string?> _cwdByProjectDir = new();

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

            // Resolved only once there is something to send: an idle tree is the common case, and
            // this reaches the filesystem for the repo's .continuum-project marker.
            var (sessionId, projectKey) = Identify(projectsDir, file);

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

    private (Guid SessionId, string ProjectKey) Identify(string projectsDir, string file)
    {
        var rel = Path.GetRelativePath(projectsDir, file);
        var projectDir = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

        // A repo may declare its own key so the same checkout on another machine lands in the same
        // workspace. The cwd is cached (it can't change), but the marker is read afresh every time:
        // adding the file should take effect on the next tick, not on the next daemon restart.
        // A null cwd is not cached — a transcript that named none says nothing about the next one.
        if (!_cwdByProjectDir.TryGetValue(projectDir, out var cwd) || cwd is null)
        {
            cwd = ReadCwd(file);
            if (cwd is not null) _cwdByProjectDir[projectDir] = cwd;
        }

        var projectKey = ProjectKey.Resolve(cwd, projectDir);

        var name = Path.GetFileNameWithoutExtension(file);
        var sessionId = Guid.TryParse(name, out var g) ? g : DeterministicGuid(file);
        return (sessionId, projectKey);
    }

    /// <summary>
    /// The working directory a transcript ran in, taken from the first line that records one, or null
    /// if the head of the file names none. Only the head is scanned: <c>cwd</c> appears within the
    /// first handful of entries, and a transcript can be hundreds of megabytes.
    /// </summary>
    private string? ReadCwd(string file)
    {
        try
        {
            var seen = 0;
            foreach (var line in File.ReadLines(file))
            {
                if (++seen > CwdScanLines) break;
                if (line.Length == 0 || !line.Contains("\"cwd\"", StringComparison.Ordinal)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("cwd", out var cwd)
                        && cwd.ValueKind == JsonValueKind.String
                        && cwd.GetString() is { Length: > 0 } value)
                        return value;
                }
                catch (JsonException)
                {
                    // Half-written or malformed line; the next one will do.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogDebug("Could not read cwd from {File}", Path.GetFileName(file));
        }

        return null;
    }

    private static Guid DeterministicGuid(string s) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(s)).AsSpan(0, 16));

    private static IEnumerable<List<IngestEvent>> Chunk(List<IngestEvent> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.GetRange(i, Math.Min(size, items.Count - i));
    }
}
