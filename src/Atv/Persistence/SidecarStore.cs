using System.Text.Json;

namespace Atv.Persistence;

/// <summary>
/// ERGO-21 ("The sidecar store design"): a directory of PER-HANDLE files,
/// each holding a <see cref="SidecarEntry"/>. An INDEX, never authoritative
/// for task existence -- the API (<see cref="Atv.Store.IAppTaskStore"/>)
/// stays source of truth; a wiped/corrupt sidecar degrades to
/// "un-addressable by handle", never mass-deletes anything.
///
/// No interface (INFRA-8-style testing seam): construct directly with an
/// injected directory (prod = <see cref="AppPaths.SidecarDir"/>, test = a
/// temp dir).
///
/// Every write is atomic (temp-file + rename, same directory/volume so the
/// rename is a single filesystem operation -- readers never observe a torn
/// file) and stamps <see cref="SidecarEntry.LastUpdate"/> itself from the
/// caller-supplied wall-clock <c>now</c> on EVERY call -- never left to the
/// caller to remember (plan/README.md standing invariant #6: wall-clock,
/// never a monotonic timer or pre-sleep cached deadline; this is the
/// liveness heartbeat the watchdog polls, LIFE-5).
///
/// Callers are responsible for invoking every member of this type from
/// INSIDE a <see cref="WriteGate"/> critical section (INFRA-6) -- this type
/// has no mutex awareness of its own, matching WriteGate's role as the sole
/// synchronization primitive above this seam.
/// </summary>
public sealed class SidecarStore
{
    private const string FileExtension = ".json";

    private readonly string _directory;

    public SidecarStore(string directory) => _directory = directory;

    /// <summary>Reads the entry for <paramref name="handle"/>, or <see langword="null"/> if absent or corrupt (graceful degradation, ERGO-21).</summary>
    public SidecarEntry? Read(string handle)
    {
        string path = PathFor(handle);
        return File.Exists(path) ? ReadFile(path) : null;
    }

    /// <summary>
    /// Every entry currently on disk, decoding each filename back to its
    /// caller handle. Used ONLY by the full reconciliation pass
    /// (<see cref="Reconciler.ReconcileAll"/>) -- never the per-handle
    /// update-class hot path (ERGO-21 "scoped 2026-07-07").
    /// </summary>
    public IReadOnlyList<(string Handle, SidecarEntry Entry)> ReadAll()
    {
        if (!Directory.Exists(_directory)) return [];

        var results = new List<(string, SidecarEntry)>();
        foreach (string file in Directory.EnumerateFiles(_directory, "*" + FileExtension))
        {
            string handle = HandleEncoding.Decode(Path.GetFileNameWithoutExtension(file));
            if (ReadFile(file) is { } entry)
                results.Add((handle, entry));
        }
        return results;
    }

    /// <summary>
    /// Atomic create-or-replace: writes to a uniquely-named temp file in the
    /// same directory, then renames it over the final path -- a single
    /// filesystem operation, so <see cref="Read"/> never observes a
    /// partially-written file. <paramref name="now"/> is stamped as
    /// <see cref="SidecarEntry.LastUpdate"/> unconditionally, on every call.
    ///
    /// PRESERVES whatever <see cref="EngineMemory"/> already existed for this
    /// handle (a fresh read-before-write) -- every phase-05-era caller of
    /// this overload (<c>ReplaceSteps</c>/<c>TouchKeepAlive</c>/the recycle-bin
    /// resurrection paths) only ever meant to touch the identity/liveness
    /// stamp, never the v2 engine's own memory; <see cref="WriteWithMemory"/>
    /// is the explicit overload for a caller that DOES mean to set it.
    /// </summary>
    public void Write(string handle, string id, DateTimeOffset now, int schemaVersion = SidecarEntry.CurrentSchemaVersion)
        => WriteEntry(handle, new SidecarEntry(id, now, schemaVersion, Read(handle)?.EngineMemory));

    /// <summary>
    /// Same atomic create-or-replace as <see cref="Write"/>, but sets
    /// <see cref="SidecarEntry.EngineMemory"/> to <paramref name="memory"/>
    /// explicitly rather than preserving whatever was already there -- the
    /// v2 <c>Atv.Semantics.SemanticEngine</c>'s own write path.
    /// </summary>
    public void WriteWithMemory(string handle, string id, DateTimeOffset now, EngineMemory memory, int schemaVersion = SidecarEntry.CurrentSchemaVersion)
        => WriteEntry(handle, new SidecarEntry(id, now, schemaVersion, memory));

    private void WriteEntry(string handle, SidecarEntry entry)
    {
        Directory.CreateDirectory(_directory);
        string finalPath = PathFor(handle);
        string tempPath = Path.Combine(_directory, $".tmp-{Guid.NewGuid():N}{FileExtension}");

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(entry, PersistenceJsonContext.Default.SidecarEntry);
        File.WriteAllBytes(tempPath, bytes);
        ReplaceAtomically(tempPath, finalPath);
    }

    /// <summary>
    /// Renames <paramref name="tempPath"/> over <paramref name="finalPath"/> --
    /// atomic on success (a single <c>MoveFileEx(REPLACE_EXISTING)</c>), so a
    /// reader only ever observes the whole old or whole new file. On Windows
    /// that call throws a transient sharing violation
    /// (<see cref="UnauthorizedAccessException"/>/<see cref="IOException"/>) if a
    /// concurrent reader holds the destination open without FILE_SHARE_DELETE --
    /// e.g. <c>list</c>/the watchdog reading the sidecar, which run lock-free
    /// OUTSIDE the WriteGate. The move itself never leaves a partial file (on
    /// failure the temp survives and the destination is untouched), so retrying
    /// the whole rename is safe and preserves atomicity.
    /// </summary>
    private static void ReplaceAtomically(string tempPath, string finalPath)
    {
        const int maxAttempts = 100;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, finalPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException && attempt < maxAttempts)
            {
                System.Threading.Thread.Sleep(2);
            }
        }
    }

    /// <summary>Drops the entry for <paramref name="handle"/> (reconciliation rules 2/3). Returns <see langword="false"/> if there was nothing to drop.</summary>
    public bool Delete(string handle)
    {
        string path = PathFor(handle);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private static SidecarEntry? ReadFile(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllBytes(path), PersistenceJsonContext.Default.SidecarEntry);
        }
        catch (JsonException)
        {
            // Corrupt/wiped entry -- degrade gracefully (ERGO-21: the
            // sidecar is authoritative for nothing, so a bad entry just
            // means this handle is un-addressable, never a mass-delete).
            return null;
        }
        catch (IOException)
        {
            // Lost a race with a concurrent writer's rename. Callers run
            // under WriteGate in production so this should be rare, but
            // degrade gracefully here too rather than throw.
            return null;
        }
    }

    private string PathFor(string handle) => Path.Combine(_directory, HandleEncoding.Encode(handle) + FileExtension);
}
