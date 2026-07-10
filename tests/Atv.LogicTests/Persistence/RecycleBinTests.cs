using Atv.Persistence;

namespace Atv.LogicTests.Persistence;

/// <summary>
/// Covers phase-04 acceptance criterion 4: record round-trip; miss-path
/// lookup finds within TTL and misses past it; scavenge deletes only expired
/// records; hot-path code never touches the folder (asserted by
/// construction).
/// </summary>
[TestClass]
public sealed class RecycleBinTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(1);
    private static readonly Uri DeepLink = new("https://example.invalid/deep");

    [TestMethod]
    public void Tombstone_Then_TryResurrect_RoundTrips_WithinTtl()
    {
        using var dir = new TempDirectory();
        var bin = new RecycleBin(dir.Path);
        var tombstonedAt = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var record = new RecycleRecord("h", "Title", "Subtitle", "icon-ref.png", DeepLink, tombstonedAt);

        bin.Tombstone(record);
        var now = tombstonedAt.AddHours(1);
        var found = bin.TryResurrect("h", now, Ttl);

        Assert.IsNotNull(found);
        Assert.AreEqual(record, found);
    }

    [TestMethod]
    public void TryResurrect_UnknownHandle_Misses_NoThrow()
    {
        using var dir = new TempDirectory();
        var bin = new RecycleBin(dir.Path);
        Assert.IsNull(bin.TryResurrect("never-tombstoned", DateTimeOffset.Now, Ttl));
    }

    [TestMethod]
    public void TryResurrect_PastTtl_Misses_ButDoesNotDeleteTheRecord()
    {
        using var dir = new TempDirectory();
        var bin = new RecycleBin(dir.Path);
        var tombstonedAt = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        bin.Tombstone(new RecycleRecord("h", "T", "S", null, DeepLink, tombstonedAt));

        var justPastTtl = tombstonedAt.Add(Ttl).AddSeconds(1);
        Assert.IsNull(bin.TryResurrect("h", justPastTtl, Ttl), "past-TTL record must miss");

        // The lookup itself must not have deleted anything -- scavenge is a separate step.
        Assert.HasCount(1, Directory.GetFiles(dir.Path));
    }

    [TestMethod]
    public void TryResurrect_ExactlyAtTtlBoundary_Hits()
    {
        using var dir = new TempDirectory();
        var bin = new RecycleBin(dir.Path);
        var tombstonedAt = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        bin.Tombstone(new RecycleRecord("h", "T", "S", null, DeepLink, tombstonedAt));

        var exactlyAtTtl = tombstonedAt.Add(Ttl);
        Assert.IsNotNull(bin.TryResurrect("h", exactlyAtTtl, Ttl), "TTL boundary is inclusive");
    }

    [TestMethod]
    public void Remove_DeletesRecord_AndIsFalseOnSecondCall()
    {
        using var dir = new TempDirectory();
        var bin = new RecycleBin(dir.Path);
        bin.Tombstone(new RecycleRecord("h", "T", "S", null, DeepLink, DateTimeOffset.Now));

        Assert.IsTrue(bin.Remove("h"));
        Assert.IsNull(bin.TryResurrect("h", DateTimeOffset.Now, Ttl));
        Assert.IsFalse(bin.Remove("h"));
    }

    [TestMethod]
    public void Scavenge_DropsOnlyExpiredRecords_AndDeletesTheirFiles()
    {
        using var dir = new TempDirectory();
        var bin = new RecycleBin(dir.Path);
        var now = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

        bin.Tombstone(new RecycleRecord("fresh", "T", "S", null, DeepLink, now.AddHours(-1)));
        bin.Tombstone(new RecycleRecord("stale", "T", "S", null, DeepLink, now.Subtract(Ttl).AddMinutes(-5)));

        var result = bin.Scavenge(now, Ttl);

        CollectionAssert.Contains(result.Removed.ToArray(), "stale");
        CollectionAssert.Contains(result.Kept.ToArray(), "fresh");
        Assert.IsNotNull(bin.TryResurrect("fresh", now, Ttl));
        Assert.IsNull(bin.TryResurrect("stale", now, Ttl));
        Assert.HasCount(1, Directory.GetFiles(dir.Path), "the expired record's file must actually be deleted from disk");
    }

    [TestMethod]
    public void Scavenge_OnMissingDirectory_ReturnsEmpty_NoThrow()
    {
        using var dir = new TempDirectory();
        var bin = new RecycleBin(dir.Path);
        var result = bin.Scavenge(DateTimeOffset.Now, Ttl);
        Assert.IsEmpty(result.Removed);
        Assert.IsEmpty(result.Kept);
    }

    [TestMethod]
    public void HostileHandle_RoundTrips_ThroughTombstoneAndResurrect()
    {
        using var dir = new TempDirectory();
        var bin = new RecycleBin(dir.Path);
        const string handle = @"agent/session:2026-07-07\weird?name";
        var now = DateTimeOffset.Now;
        bin.Tombstone(new RecycleRecord(handle, "T", "S", null, DeepLink, now));

        var found = bin.TryResurrect(handle, now, Ttl);
        Assert.IsNotNull(found);
        Assert.AreEqual(handle, found!.Handle);
    }

    // ---- LIFE-20 boot recovery: unconditional wipe ------------------------

    [TestMethod]
    public void WipeAll_DeletesEveryFile_RecordsAndAnyCoLocatedNonRecordFiles()
    {
        using var dir = new TempDirectory();
        var bin = new RecycleBin(dir.Path);
        bin.Tombstone(new RecycleRecord("a", "T", "S", null, DeepLink, DateTimeOffset.Now));
        bin.Tombstone(new RecycleRecord("b", "T", "S", null, DeepLink, DateTimeOffset.Now));
        // Simulate a co-located icon file (Atv.Icons.IconService's own convention -- same directory, different extension).
        File.WriteAllBytes(Path.Combine(dir.Path, "a.png"), [1, 2, 3]);

        int removed = bin.WipeAll();

        Assert.AreEqual(3, removed);
        Assert.IsEmpty(Directory.GetFiles(dir.Path));
    }

    [TestMethod]
    public void WipeAll_OnMissingDirectory_ReturnsZero_NoThrow()
    {
        using var dir = new TempDirectory();
        var bin = new RecycleBin(dir.Path);
        Assert.AreEqual(0, bin.WipeAll());
    }

    // ---- AC4: hot-path code never touches the folder (by construction) ----

    [TestMethod]
    public void HotPathMembers_NeverEnumerateTheFolder_ByConstruction()
    {
        string source = File.ReadAllText(FindRecycleBinSourceFile());

        string tryResurrectBody = ExtractMethodBody(source, "public RecycleRecord? TryResurrect(");
        string removeBody = ExtractMethodBody(source, "public bool Remove(");
        string scavengeBody = ExtractMethodBody(source, "public ScavengeResult Scavenge(");

        Assert.IsFalse(ContainsEnumeration(tryResurrectBody),
            "TryResurrect (the miss-path hot-path lookup) must never enumerate the recycle-bin folder");
        Assert.IsFalse(ContainsEnumeration(removeBody),
            "Remove must never enumerate the recycle-bin folder");
        Assert.IsTrue(ContainsEnumeration(scavengeBody),
            "sanity check that this test isn't vacuous: Scavenge IS expected to enumerate the folder");
    }

    private static bool ContainsEnumeration(string methodBody)
        => methodBody.Contains("EnumerateFiles", StringComparison.Ordinal) || methodBody.Contains("GetFiles", StringComparison.Ordinal);

    private static string ExtractMethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, $"could not locate method signature '{signature}' in RecycleBin.cs -- test needs updating alongside the source");

        int braceOpen = source.IndexOf('{', start);
        int depth = 0;
        int i = braceOpen;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) break;
            }
        }
        return source[braceOpen..(i + 1)];
    }

    private static string FindRecycleBinSourceFile([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AppTaskInfoCli.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException($"Could not locate the repo root (AppTaskInfoCli.slnx) above {here}");
        return Path.Combine(dir.FullName, "src", "Atv", "Persistence", "RecycleBin.cs");
    }
}
