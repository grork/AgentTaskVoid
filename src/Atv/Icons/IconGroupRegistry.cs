using Atv.Persistence;

namespace Atv.Icons;

/// <summary>
/// ERGO-30's repo-grouping-intent owner registry: a tiny one-line text file
/// per group key recording WHICH handle currently "owns" (physically holds)
/// the shared per-repo icon file that other same-repo cards reuse
/// byte-for-byte. Deliberately NOT part of the sidecar/<c>EngineMemory</c>
/// schema -- a group has no per-CARD memory of its own (LIFE-24's "if it
/// needs memory... it is engine" is about a HANDLE's memory; this is a
/// cross-card routing fact), and keeping it out of <c>SidecarEntry</c> avoids
/// a schema-version bump for a purely-additive, self-healing mechanism: a
/// stale/dangling owner record is corrected the next time ANY card is
/// created in that repo (<see cref="Atv.Semantics.SemanticEngine"/> notices
/// the recorded owner handle is no longer live and simply becomes the new
/// owner itself, overwriting the record) -- see that type's repo-defaults
/// application for the full ownership-transfer algorithm. Same atomic
/// temp+rename write pattern as every other per-handle file this codebase
/// writes (<see cref="IconService"/>/<see cref="SidecarStore"/>/
/// <see cref="RecycleBin"/>).
/// </summary>
public sealed class IconGroupRegistry
{
    private const string FileSuffix = ".owner.txt";
    private readonly string _dir;

    public IconGroupRegistry(string groupsDir) => _dir = groupsDir;

    /// <summary>The handle currently recorded as <paramref name="groupKey"/>'s icon owner, or <see langword="null"/> if no group with this key has ever had an owner recorded (or the record is unreadable -- degrades to "no owner", never a throw, FAIL-1).</summary>
    public string? ReadOwnerHandle(string groupKey)
    {
        string path = PathFor(groupKey);
        if (!File.Exists(path)) return null;
        try
        {
            string s = File.ReadAllText(path).Trim();
            return s.Length > 0 ? s : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Records <paramref name="handle"/> as <paramref name="groupKey"/>'s current icon owner, atomically overwriting any prior record. Best-effort: this is a pure routing accelerator -- worst case on a write failure, the NEXT create in this repo just re-places its own icon rather than reusing one (never a correctness issue, only a missed glom).</summary>
    public void WriteOwnerHandle(string groupKey, string handle)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            string finalPath = PathFor(groupKey);
            string tempPath = Path.Combine(_dir, $".tmp-{Guid.NewGuid():N}.txt");
            File.WriteAllText(tempPath, handle);
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string PathFor(string groupKey) => Path.Combine(_dir, HandleEncoding.Encode(groupKey) + FileSuffix);
}
