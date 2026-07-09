using Atv.Icons;
using Atv.LogicTests.Persistence;
using Atv.Persistence;

namespace Atv.LogicTests.Icons;

/// <summary>
/// Phase-07 acceptance criteria 2-4: cache (render once, per-handle copies
/// distinct), ownership (reap/move/purge), and the fallback chain. Real
/// rendering throughout (no fake -- <c>Atv.IconRendering</c> has no
/// filesystem/handle/policy surface to fake against; its own test suite
/// covers the mechanism in isolation). Temp-dir injected, same pattern as
/// <see cref="RecycleBinTests"/>/<see cref="SidecarStoreTests"/> -- no
/// package identity required.
/// </summary>
[TestClass]
public sealed class IconServiceTests
{
    private static readonly IconToken Robot = IconTokens.Default;
    private static readonly IconToken Bug = IconToken.Segoe(IconTokens.CuratedSegoe["Bug"]);

    [TestMethod]
    public void Place_SameGlyph_DifferentHandles_ProducesDistinctFiles_ButIdenticalBytes()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);

        Uri uriA = service.Place("session-a", Robot);
        Uri uriB = service.Place("session-b", Robot);

        Assert.AreNotEqual(uriA, uriB, "each handle must be its own file/path (the ERGO-15 grouping mechanism), even for an identical glyph.");
        CollectionAssert.AreEqual(File.ReadAllBytes(uriA.LocalPath), File.ReadAllBytes(uriB.LocalPath));
    }

    [TestMethod]
    public void Place_SameGlyphAndSizeTwice_SecondCallServedFromCache_NoReRender()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);

        service.Place("session-a", Bug);
        string cachePath = Path.Combine(icons.Path, "cache", $"segoe-{Bug.Codepoint:X}-{IconService.DefaultSizePx}.png");
        Assert.IsTrue(File.Exists(cachePath), "expected a canonical cache entry to be written on first render.");
        DateTime firstWrite = File.GetLastWriteTimeUtc(cachePath);

        service.Place("session-b", Bug);

        Assert.AreEqual(firstWrite, File.GetLastWriteTimeUtc(cachePath), "the cache file must not be rewritten -- the second Place should be served from cache, not re-rendered.");
    }

    [TestMethod]
    public void Place_AfterWipingCache_StillSucceeds_ReRenders()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);

        Uri first = service.Place("session-a", Bug);
        byte[] firstBytes = File.ReadAllBytes(first.LocalPath);

        Directory.Delete(Path.Combine(icons.Path, "cache"), recursive: true);

        Uri second = service.Place("session-b", Bug);
        byte[] secondBytes = File.ReadAllBytes(second.LocalPath);

        CollectionAssert.AreEqual(firstBytes, secondBytes, "wiping the canonical cache must break nothing -- deterministic rendering re-produces byte-identical output.");
    }

    [TestMethod]
    public void ReapLiveIcon_DeletesThePerHandleCopy()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);
        Uri uri = service.Place("session-a", Robot);
        Assert.IsTrue(File.Exists(uri.LocalPath));

        bool reaped = service.ReapLiveIcon("session-a");

        Assert.IsTrue(reaped);
        Assert.IsFalse(File.Exists(uri.LocalPath));
    }

    [TestMethod]
    public void ReapLiveIcon_NoLiveCopy_ReturnsFalse_NoThrow()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);

        Assert.IsFalse(service.ReapLiveIcon("never-placed"));
    }

    [TestMethod]
    public void MoveToRecycle_MovesFileFromLiveToRecycleFolder()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);
        Uri liveUri = service.Place("session-a", Robot);
        byte[] originalBytes = File.ReadAllBytes(liveUri.LocalPath);

        bool moved = service.MoveToRecycle("session-a");

        Assert.IsTrue(moved);
        Assert.IsFalse(File.Exists(liveUri.LocalPath), "the live copy must be GONE -- moved, not copied (single-owner MOVE model).");
        string recyclePath = Path.Combine(recycle.Path, HandleEncoding.Encode("session-a") + ".png");
        Assert.IsTrue(File.Exists(recyclePath));
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(recyclePath));
    }

    [TestMethod]
    public void MoveBackFromRecycle_MovesFileBackToLivePath_ReturnsLiveUri()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);
        Uri originalLiveUri = service.Place("session-a", Robot);
        byte[] originalBytes = File.ReadAllBytes(originalLiveUri.LocalPath);
        service.MoveToRecycle("session-a");

        Uri restoredUri = service.MoveBackFromRecycle("session-a");

        Assert.AreEqual(originalLiveUri, restoredUri);
        Assert.IsTrue(File.Exists(restoredUri.LocalPath));
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(restoredUri.LocalPath));
        string recyclePath = Path.Combine(recycle.Path, HandleEncoding.Encode("session-a") + ".png");
        Assert.IsFalse(File.Exists(recyclePath), "each asset lives in exactly ONE place at a time -- the recycle copy must be gone after moving back.");
    }

    [TestMethod]
    public void MoveBackFromRecycle_NothingRecycled_DegradesToFreshDefaultRender_NoThrow()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);

        Uri uri = service.MoveBackFromRecycle("never-tombstoned");

        Assert.IsTrue(File.Exists(uri.LocalPath), "structurally this should never happen, but must degrade gracefully rather than throw.");
    }

    [TestMethod]
    public void ReapRecycledIcon_DeletesTheRecycleSideCopy()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);
        service.Place("session-a", Robot);
        service.MoveToRecycle("session-a");
        string recyclePath = Path.Combine(recycle.Path, HandleEncoding.Encode("session-a") + ".png");
        Assert.IsTrue(File.Exists(recyclePath));

        bool reaped = service.ReapRecycledIcon("session-a");

        Assert.IsTrue(reaped);
        Assert.IsFalse(File.Exists(recyclePath));
    }

    [TestMethod]
    public void TtlPurge_RecycleBinScavengePairedWithIconReap_RemovesRecordAndIconTogether()
    {
        using var iconsDir = new TempDirectory();
        using var recycleDir = new TempDirectory();
        var service = new IconService(iconsDir.Path, recycleDir.Path);
        var recycleBin = new RecycleBin(recycleDir.Path);
        var now = new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

        service.Place("session-a", Robot);
        service.MoveToRecycle("session-a");
        recycleBin.Tombstone(new RecycleRecord("session-a", "Title", "Subtitle", null, new Uri("https://example.invalid"), now));

        var scavenge = recycleBin.Scavenge(now + TimeSpan.FromDays(2), TimeSpan.FromDays(1));
        foreach (string handle in scavenge.Removed)
            service.ReapRecycledIcon(handle);

        Assert.IsTrue(scavenge.Removed.Contains("session-a"));
        Assert.IsFalse(File.Exists(Path.Combine(recycleDir.Path, HandleEncoding.Encode("session-a") + ".json")));
        Assert.IsFalse(File.Exists(Path.Combine(recycleDir.Path, HandleEncoding.Encode("session-a") + ".png")));
    }

    [TestMethod]
    public void Place_ChosenUnavailableRawPath_FallsBackToDefault_AndLogs()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var logs = new List<string>();
        var service = new IconService(icons.Path, recycle.Path, log: logs.Add);
        var missing = IconToken.RawPath(Path.Combine(icons.Path, "does-not-exist.png"));

        Uri uri = service.Place("session-a", missing);
        Uri defaultUri = service.Place("session-b", IconTokens.Default);

        CollectionAssert.AreEqual(File.ReadAllBytes(defaultUri.LocalPath), File.ReadAllBytes(uri.LocalPath), "an unavailable chosen icon must fall back to the ERGO-12 default glyph's actual pixels.");
        Assert.IsTrue(logs.Any(l => l.Contains("falling back to the default glyph", StringComparison.Ordinal)), "the fallback must be logged (FAIL-3).");
    }

    [TestMethod]
    public void SweepOrphans_ReapsUnownedIcons_KeepsLiveAndRecycleOwnedOnes()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);
        Uri liveUri = service.Place("live-handle", Robot);
        Uri orphanUri = service.Place("orphan-handle", Robot);
        // Simulate a recycle-owned handle: its live copy still needs to be
        // recognized as "owned" even though it's not in liveHandles, because
        // it's passed via recycleHandles.
        Uri recycleOwnedUri = service.Place("recycle-owned-handle", Robot);

        int reaped = service.SweepOrphans(
            liveHandles: ["live-handle"],
            recycleHandles: ["recycle-owned-handle"]);

        Assert.AreEqual(1, reaped);
        Assert.IsTrue(File.Exists(liveUri.LocalPath));
        Assert.IsTrue(File.Exists(recycleOwnedUri.LocalPath));
        Assert.IsFalse(File.Exists(orphanUri.LocalPath));
    }

    [TestMethod]
    public void PruneCache_RemovesEntriesOlderThanMaxAge_KeepsRecent()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);
        service.Place("session-a", Robot);
        string cachePath = Path.Combine(icons.Path, "cache", $"segoe-{Robot.Codepoint:X}-{IconService.DefaultSizePx}.png");
        File.SetLastWriteTimeUtc(cachePath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        int pruned = service.PruneCache(new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(30));

        Assert.AreEqual(1, pruned);
        Assert.IsFalse(File.Exists(cachePath));
    }

    [TestMethod]
    public void PruneCache_RecentEntry_IsKept()
    {
        using var icons = new TempDirectory();
        using var recycle = new TempDirectory();
        var service = new IconService(icons.Path, recycle.Path);
        service.Place("session-a", Robot);
        string cachePath = Path.Combine(icons.Path, "cache", $"segoe-{Robot.Codepoint:X}-{IconService.DefaultSizePx}.png");

        int pruned = service.PruneCache(DateTimeOffset.UtcNow, TimeSpan.FromDays(30));

        Assert.AreEqual(0, pruned);
        Assert.IsTrue(File.Exists(cachePath));
    }
}
