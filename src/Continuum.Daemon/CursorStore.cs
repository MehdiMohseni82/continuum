using Microsoft.Data.Sqlite;

namespace Continuum.Daemon;

/// <summary>
/// Persists how far each file has been read. This SQLite file plus the source transcripts
/// ARE the offline queue: the cursor only advances after the server acknowledges a batch,
/// so anything unsent simply stays in the file and is re-read next tick.
/// </summary>
public sealed class CursorStore
{
    private readonly string _connString;

    public CursorStore(DaemonOptions options)
    {
        _connString = new SqliteConnectionStringBuilder { DataSource = options.CursorDbPath }.ToString();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS cursors (
                path    TEXT PRIMARY KEY,
                offset  INTEGER NOT NULL,
                updated TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public long GetOffset(string path)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT offset FROM cursors WHERE path = $p";
        cmd.Parameters.AddWithValue("$p", path);
        var result = cmd.ExecuteScalar();
        return result is long l ? l : 0;
    }

    public void SaveOffset(string path, long offset)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cursors (path, offset, updated) VALUES ($p, $o, $u)
            ON CONFLICT(path) DO UPDATE SET offset = $o, updated = $u;
            """;
        cmd.Parameters.AddWithValue("$p", path);
        cmd.Parameters.AddWithValue("$o", offset);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connString);
        conn.Open();
        return conn;
    }
}
