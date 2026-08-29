using System.Text.Json;

namespace Continuum.Cli;

/// <summary>
/// Where the CLI gets its backend and identity.
///
/// The PowerShell relay read the *daemon's* appsettings.json, which meant rooms required the daemon
/// to be installed even though the relay never talks to it. Config lives in its own file now, so a
/// machine that only wants to join rooms needs nothing else.
/// </summary>
public sealed record Config(string Backend, string Token, string? Machine, string? Agent)
{
    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".continuum");

    public static string Path_ => System.IO.Path.Combine(Dir, "config.json");

    /// <summary>
    /// Env wins over the file, so a single session can point somewhere else without editing config.
    /// Returns null when there is genuinely nothing configured — callers report that rather than
    /// guessing at localhost, which is how the old hooks failed silently.
    /// </summary>
    public static Config? Load()
    {
        var backend = Environment.GetEnvironmentVariable("CONTINUUM_BACKEND");
        var token = Environment.GetEnvironmentVariable("CONTINUUM_TOKEN");
        var machine = Environment.GetEnvironmentVariable("CONTINUUM_MACHINE");
        var agent = Environment.GetEnvironmentVariable("CONTINUUM_AGENT");

        if (File.Exists(Path_))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path_));
                var root = doc.RootElement;
                backend ??= Str(root, "backend");
                token ??= Str(root, "token");
                machine ??= Str(root, "machine");
                agent ??= Str(root, "agent");
            }
            catch (JsonException)
            {
                // A corrupt config is worth saying out loud rather than silently ignoring.
                Console.Error.WriteLine($"continuum: {Path_} is not valid JSON — ignoring it.");
            }
        }

        if (string.IsNullOrWhiteSpace(backend) || string.IsNullOrWhiteSpace(token)) return null;
        return new Config(backend.TrimEnd('/'), token, machine, agent);

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>Per-session relay state, kept beside the config.</summary>
    public static string StateDir => System.IO.Path.Combine(Dir, "relay", "state");
    public static string LogDir => System.IO.Path.Combine(Dir, "relay", "log");
}
