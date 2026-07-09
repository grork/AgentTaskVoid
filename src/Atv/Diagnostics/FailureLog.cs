using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atv.Diagnostics;

/// <summary>FAIL-3's durable log entry shape: <c>{timestamp, verb, handle, error}</c>. Written one per line (JSONL) -- cheap to append, and cheap to inspect just the first line for age-based rotation without parsing the whole file.</summary>
public sealed record LogEntry(DateTimeOffset Timestamp, string Verb, string? Handle, string Error);

/// <summary>
/// FAIL-1/FAIL-3's always-on durable failure log: lives in package app-data
/// (same container as tasks.json / the sidecar -- production callers use
/// <see cref="Atv.Persistence.AppPaths.LogPath"/>), NEVER a hand-rolled
/// global path. FAILURES are logged unconditionally (FAIL-1: "a durable
/// failure log entry is ALWAYS written on the silent path" is a hard
/// requirement, not optional); success entries are the caller's choice
/// (<see cref="Posture"/> wires them to <c>--verbose</c>).
///
/// <see cref="Append"/> NEVER throws -- a failing log write is itself
/// swallowed, because logging must not become a second way for this tool to
/// violate its own non-disruptive posture.
///
/// Size/age rotation keeps exactly one backup generation
/// (<c>&lt;path&gt;.1</c>) -- simple and bounded, no unbounded history. Age
/// is measured from the TIMESTAMP OF THE OLDEST ENTRY currently on disk
/// (read from line 1), not filesystem metadata -- deterministic and fully
/// testable via the caller-supplied wall-clock <c>now</c>, the same
/// convention used throughout this codebase (plan/README.md standing
/// invariant #6).
/// </summary>
public sealed class FailureLog
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly TimeSpan _maxAge;

    public FailureLog(string path, long maxBytes, TimeSpan maxAge)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _maxBytes = maxBytes;
        _maxAge = maxAge;
    }

    /// <summary>Appends one entry, rotating first if needed. Swallows every exception (FAIL-1) -- callers get no signal on a logging failure, by design.</summary>
    public void Append(string verb, string? handle, string error, DateTimeOffset now)
    {
        try
        {
            RotateIfNeeded(now);
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var entry = new LogEntry(now, verb, handle, error);
            string line = JsonSerializer.Serialize(entry, FailureLogJsonContext.Default.LogEntry);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch
        {
            // Swallow. Logging must never throw (FAIL-1/FAIL-3).
        }
    }

    /// <summary>Every entry currently in the active log file, oldest first. Test/inspection surface today -- a future `doctor -v` could reuse it.</summary>
    public IReadOnlyList<LogEntry> ReadAll()
    {
        if (!File.Exists(_path)) return [];

        var results = new List<LogEntry>();
        foreach (string line in File.ReadAllLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (JsonSerializer.Deserialize(line, FailureLogJsonContext.Default.LogEntry) is { } entry)
                    results.Add(entry);
            }
            catch (JsonException)
            {
                // Corrupt line -- skip it, degrade gracefully rather than fail the whole read.
            }
        }
        return results;
    }

    private void RotateIfNeeded(DateTimeOffset now)
    {
        if (!File.Exists(_path)) return;

        bool tooBig = new FileInfo(_path).Length >= _maxBytes;
        bool tooOld = !tooBig && IsTooOld(now);

        if (tooBig || tooOld)
            Rotate();
    }

    private bool IsTooOld(DateTimeOffset now)
    {
        string? firstLine = File.ReadLines(_path).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (firstLine is null) return false;

        try
        {
            var first = JsonSerializer.Deserialize(firstLine, FailureLogJsonContext.Default.LogEntry);
            return first is not null && now - first.Timestamp >= _maxAge;
        }
        catch (JsonException)
        {
            return false; // Corrupt first line -- the size check above still protects against unbounded growth.
        }
    }

    private void Rotate()
    {
        string backupPath = _path + ".1";
        try { File.Delete(backupPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        File.Move(_path, backupPath);
    }
}

/// <summary>Source-generated (AOT/trim-safe) JSON metadata for <see cref="LogEntry"/> -- the FAIL-3 log-line shape. camelCase property names match the documented <c>{timestamp, verb, handle, error}</c> shape literally.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LogEntry))]
internal partial class FailureLogJsonContext : JsonSerializerContext
{
}
