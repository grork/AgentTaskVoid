using System.Text.Json;

namespace Atv.Persistence;

/// <summary>
/// LIFE-21 ("What expiry does"): the record captured when a task is
/// tombstoned. Nothing mutable -- steps/state restart fresh on resurrection.
/// Captured by reading these fields off the live API card at expiry time
/// (ERGO-21's "no cached content" is preserved: the LIVE sidecar never grows
/// this data, only the recycle bin does, and only at tombstone time).
/// <see cref="IconRef"/> is an opaque reference this phase does not
/// interpret or move -- ERGO-23's icon co-location/move mechanics land in
/// phase 07; this phase only defines the record shape and folder contract.
/// </summary>
public sealed record RecycleRecord(
    string Handle,
    string Title,
    string Subtitle,
    string? IconRef,
    Uri DeepLink,
    DateTimeOffset WhenTombstoned);

/// <summary>Result of a <see cref="RecycleBin.Scavenge"/> pass -- handles, not full records.</summary>
public sealed record ScavengeResult(IReadOnlyList<string> Removed, IReadOnlyList<string> Kept);

/// <summary>
/// LIFE-21's cold recycle-bin folder: a directory of per-handle tombstone
/// records (same reversible <see cref="HandleEncoding"/> as the sidecar, so
/// a record's filename recovers its handle too). NEVER enumerated on the hot
/// path -- <see cref="TryResurrect"/> and <see cref="Remove"/> are direct
/// single-file lookups by construction (no folder enumeration in either);
/// only <see cref="Scavenge"/> enumerates, and it is an explicit,
/// separately-invoked opportunistic sweep (phases 08/09 call it) -- never
/// wired into the miss-path lookup itself.
///
/// Callers are responsible for invoking every member of this type from
/// inside a <see cref="WriteGate"/> critical section; this type has no
/// mutex awareness of its own.
/// </summary>
public sealed class RecycleBin
{
    private const string FileExtension = ".json";

    private readonly string _directory;

    public RecycleBin(string directory) => _directory = directory ?? throw new ArgumentNullException(nameof(directory));

    /// <summary>
    /// Writes (or overwrites) the tombstone record for
    /// <paramref name="record"/>'s handle -- atomic temp-file + rename, same
    /// pattern as <see cref="SidecarStore.Write"/>.
    /// </summary>
    public void Tombstone(RecycleRecord record)
    {
        Directory.CreateDirectory(_directory);
        string finalPath = PathFor(record.Handle);
        string tempPath = Path.Combine(_directory, $".tmp-{Guid.NewGuid():N}{FileExtension}");

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record, PersistenceJsonContext.Default.RecycleRecord);
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, finalPath, overwrite: true);
    }

    /// <summary>
    /// Miss-path lookup ONLY (LIFE-21: "consulted only on the miss path --
    /// an update whose handle is absent from the live sidecar"). A direct
    /// single-file check by construction: never enumerates the folder.
    /// Returns a hit only if a record exists AND is still within
    /// <paramref name="ttl"/> of <paramref name="now"/>; a past-TTL record
    /// is reported as a miss here but is NOT deleted by this call -- that is
    /// <see cref="Scavenge"/>'s job, invoked separately/opportunistically.
    /// </summary>
    public RecycleRecord? TryResurrect(string handle, DateTimeOffset now, TimeSpan ttl)
    {
        string path = PathFor(handle);
        if (!File.Exists(path)) return null;

        RecycleRecord? record = ReadFile(path);
        if (record is null) return null;

        return now - record.WhenTombstoned <= ttl ? record : null;
    }

    /// <summary>Removes a record after successful resurrection (phase 05 calls this once the card is re-created live). A direct single-file delete: never enumerates.</summary>
    public bool Remove(string handle)
    {
        string path = PathFor(handle);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>
    /// Opportunistic sweep: drops every record older than
    /// <paramref name="ttl"/> as of <paramref name="now"/>. The ONLY member
    /// of this type that enumerates the folder -- never called from the
    /// miss-path hot path, only from phases 08/09's existing sweeps.
    /// Co-located icon-asset deletion is phase 07's move-mechanics territory
    /// (ERGO-23); this phase ships the record half of scavenge only.
    /// </summary>
    public ScavengeResult Scavenge(DateTimeOffset now, TimeSpan ttl)
    {
        if (!Directory.Exists(_directory)) return new ScavengeResult([], []);

        var removed = new List<string>();
        var kept = new List<string>();

        foreach (string file in Directory.EnumerateFiles(_directory, "*" + FileExtension))
        {
            string handle = HandleEncoding.Decode(Path.GetFileNameWithoutExtension(file));
            RecycleRecord? record = ReadFile(file);

            bool expired = record is null || now - record.WhenTombstoned > ttl;
            if (expired)
            {
                File.Delete(file);
                removed.Add(handle);
            }
            else
            {
                kept.Add(handle);
            }
        }

        return new ScavengeResult(removed, kept);
    }

    /// <summary>
    /// LIFE-20's boot-recovery unconditional wipe: deletes EVERY file in the
    /// directory -- both tombstone <c>.json</c> records and their co-located
    /// <c>Atv.Icons.IconService</c> recycle-side <c>.png</c> copies (same
    /// directory, same <see cref="HandleEncoding"/> filename convention, by
    /// deliberate convention rather than a dependency between the two types
    /// -- see <c>Atv.Icons.IconService</c>'s own remarks). Ignores TTL
    /// entirely -- never called from any hot path, boot-recovery only.
    /// Returns the count of files removed.
    /// </summary>
    public int WipeAll()
    {
        if (!Directory.Exists(_directory)) return 0;

        int removed = 0;
        foreach (string file in Directory.EnumerateFiles(_directory))
        {
            try { File.Delete(file); removed++; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }

    private static RecycleRecord? ReadFile(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllBytes(path), PersistenceJsonContext.Default.RecycleRecord);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private string PathFor(string handle) => Path.Combine(_directory, HandleEncoding.Encode(handle) + FileExtension);
}
