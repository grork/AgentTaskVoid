using Atv.Icons;
using Atv.Persistence;
using Atv.Store;
using Atv.Watchdog;

namespace Atv.LogicTests.Watchdog;

/// <summary>
/// AC1's per-tick behavior coverage: per-state expiry at exactly the
/// configured thresholds, freshness ordering (a write interleaved before the
/// expiry compare rescues the task), expiry writing a complete recycle
/// record read from the card, the HiddenByUser sweep, entryless reap with a
/// logged count, and the recycle-bin TTL scavenge folded in.
/// </summary>
[TestClass]
public sealed class WatchdogLoopTickTests
{
    // ---- per-state expiry thresholds, boundary-inclusive -------------------

    [TestMethod]
    public void RunTick_RunningTask_ExactlyAtThreshold_Expires()
    {
        using var h = new WatchdogTestHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Running);
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdleRunning);

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(1, result.ExpiredCount);
        Assert.IsNull(h.Store.Find(view.Id));
        Assert.IsNull(h.Sidecar.Read("h1"));
    }

    [TestMethod]
    public void RunTick_RunningTask_JustUnderThreshold_NotExpired()
    {
        using var h = new WatchdogTestHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Running);
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdleRunning + TimeSpan.FromSeconds(1));

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(0, result.ExpiredCount);
        Assert.IsNotNull(h.Store.Find(view.Id));
    }

    [TestMethod]
    public void RunTick_PausedTask_UsesIdlePausedThreshold_NotIdleRunning()
    {
        using var h = new WatchdogTestHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Paused);
        // Past Running's threshold but well under Paused's -- must NOT expire.
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdleRunning - TimeSpan.FromMinutes(5));

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(0, result.ExpiredCount, "Paused must use its own (longer) idle threshold.");
        Assert.IsNotNull(h.Store.Find(view.Id));
    }

    [TestMethod]
    public void RunTick_PausedTask_AtItsOwnThreshold_Expires()
    {
        using var h = new WatchdogTestHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Paused);
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdlePaused);

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(1, result.ExpiredCount);
    }

    [TestMethod]
    public void RunTick_NeedsAttentionTask_UsesItsOwnThreshold()
    {
        using var h = new WatchdogTestHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.NeedsAttention);
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdleRunning - TimeSpan.FromMinutes(5));

        var notYet = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);
        Assert.AreEqual(0, notYet.ExpiredCount, "NeedsAttention must not expire on Running's shorter threshold.");

        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdleNeedsAttention);
        var atThreshold = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);
        Assert.AreEqual(1, atThreshold.ExpiredCount, "NeedsAttention DOES expire at its own (longer) threshold -- nothing resurrects it.");
    }

    [TestMethod]
    public void RunTick_CompletedTask_UsesShortLingerThreshold()
    {
        using var h = new WatchdogTestHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Completed);
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdleCompleted);

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(1, result.ExpiredCount);
    }

    [TestMethod]
    public void RunTick_FailedTask_UsesShortLingerThreshold_SameAsCompleted()
    {
        using var h = new WatchdogTestHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Error);
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdleCompleted);

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(1, result.ExpiredCount, "LIFE-22: Completed/done/fail share the same short linger.");
    }

    // ---- freshness ordering (invariant #6) ----------------------------------

    [TestMethod]
    public void RunTick_FreshnessOrdering_InterleavedWriteRescuesTask()
    {
        using var h = new WatchdogTestHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Running);
        DateTimeOffset staleTime = WatchdogTestHarness.Now - h.Settings.IdleRunning - TimeSpan.FromMinutes(5);
        h.Sidecar.Write("h1", view.Id, staleTime);

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now,
            onAfterReconcile: () => h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now));

        Assert.AreEqual(0, result.ExpiredCount, "a write landing before the expiry compare must rescue the task.");
        Assert.IsNotNull(h.Store.Find(view.Id));
        Assert.IsNotNull(h.Sidecar.Read("h1"));
    }

    [TestMethod]
    public void RunTick_FreshnessOrdering_Control_WithoutInterleavedWrite_TaskExpires()
    {
        // Same setup as the rescue test, minus the hook -- proves the rescue test isn't vacuous.
        using var h = new WatchdogTestHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Running);
        DateTimeOffset staleTime = WatchdogTestHarness.Now - h.Settings.IdleRunning - TimeSpan.FromMinutes(5);
        h.Sidecar.Write("h1", view.Id, staleTime);

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(1, result.ExpiredCount);
        Assert.IsNull(h.Store.Find(view.Id));
    }

    // ---- what expiry does: complete recycle record + icon move -------------

    [TestMethod]
    public void RunTick_Expiry_WritesCompleteRecycleRecord_ReadFromTheLiveCard()
    {
        using var h = new WatchdogTestHarness();
        Uri deepLink = new("https://example.invalid/task-deep-link");
        Uri iconUri = new("ms-appx:///Assets/OtherLogo.png");
        var view = h.Store.SeedEntrylessTask("My Title", "My Subtitle", AppTaskState.Running, deepLink: deepLink, iconUri: iconUri);
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdleRunning);

        WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        var record = h.RecycleBin.TryResurrect("h1", WatchdogTestHarness.Now, h.Settings.RecycleBinTtl);
        Assert.IsNotNull(record);
        Assert.AreEqual("h1", record!.Handle);
        Assert.AreEqual("My Title", record.Title);
        Assert.AreEqual("My Subtitle", record.Subtitle);
        Assert.AreEqual(deepLink, record.DeepLink);
        Assert.AreEqual(iconUri.ToString(), record.IconRef);
        Assert.AreEqual(WatchdogTestHarness.Now, record.WhenTombstoned);
    }

    [TestMethod]
    public void RunTick_Expiry_MovesTheLiveIconIntoTheRecycleFolder()
    {
        using var h = new WatchdogTestHarness();
        Uri liveIconUri = h.Icons.Place("h1", IconTokens.Default);
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Running, deepLink: WatchdogTestHarness.DeepLink, iconUri: liveIconUri);
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdleRunning);

        WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.IsFalse(File.Exists(liveIconUri.LocalPath), "the live per-handle icon must be moved away, not left behind.");
        string recycledIconPath = Path.Combine(h.RecycleDirPath, HandleEncoding.Encode("h1") + ".png");
        Assert.IsTrue(File.Exists(recycledIconPath), "the icon must land beside the recycle-bin's own tombstone record.");
    }

    // ---- HiddenByUser sweep --------------------------------------------------

    [TestMethod]
    public void RunTick_HiddenByUser_SweptAndRemoved_ReportedInResult()
    {
        using var h = new WatchdogTestHarness();
        var view = h.Store.Create("T", "S", WatchdogTestHarness.DeepLink, WatchdogTestHarness.IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now);
        h.Store.SetHiddenByUser(view.Id, true);

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(1, result.HiddenSweptCount);
        Assert.IsNull(h.Store.Find(view.Id));
        Assert.IsNull(h.Sidecar.Read("h1"));
    }

    // ---- entryless-orphan reap (LIFE-23) -------------------------------------

    [TestMethod]
    public void RunTick_EntrylessTask_ReapedUnconditionally_LoggedWithCount()
    {
        using var h = new WatchdogTestHarness();
        var seeded = h.Store.SeedEntrylessTask("Orphan", "no entry ever knew this id");

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(1, result.EntrylessReapedCount);
        Assert.IsNull(h.Store.Find(seeded.Id));
        Assert.IsTrue(h.Logs.Any(l => l.Contains("reaped 1 entryless", StringComparison.Ordinal)),
            "the entryless reap must leave an audible FAIL-3 breadcrumb with a count.");
    }

    [TestMethod]
    public void RunTick_NoEntrylessTasks_NoBreadcrumbLogged()
    {
        using var h = new WatchdogTestHarness();
        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(0, result.EntrylessReapedCount);
        Assert.IsFalse(h.Logs.Any(l => l.Contains("entryless", StringComparison.Ordinal)));
    }

    // ---- recycle-bin TTL scavenge folded in ----------------------------------

    [TestMethod]
    public void RunTick_RecycleBinScavenge_DropsExpiredRecords_AndTheirCoLocatedIcon()
    {
        using var h = new WatchdogTestHarness();
        h.Icons.Place("stale-handle", IconTokens.Default);
        h.Icons.MoveToRecycle("stale-handle");
        h.RecycleBin.Tombstone(new RecycleRecord("stale-handle", "T", "S", null, WatchdogTestHarness.DeepLink,
            WatchdogTestHarness.Now - h.Settings.RecycleBinTtl - TimeSpan.FromMinutes(1)));

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(1, result.RecycleScavengedCount);
        Assert.IsNull(h.RecycleBin.TryResurrect("stale-handle", WatchdogTestHarness.Now, h.Settings.RecycleBinTtl));
        string recycledIconPath = Path.Combine(h.RecycleDirPath, HandleEncoding.Encode("stale-handle") + ".png");
        Assert.IsFalse(File.Exists(recycledIconPath), "the scavenged record's co-located icon must be reaped too.");
    }

    [TestMethod]
    public void RunTick_RecycleBinScavenge_KeepsFreshRecords()
    {
        using var h = new WatchdogTestHarness();
        h.RecycleBin.Tombstone(new RecycleRecord("fresh-handle", "T", "S", null, WatchdogTestHarness.DeepLink, WatchdogTestHarness.Now.AddHours(-1)));

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(0, result.RecycleScavengedCount);
        Assert.IsNotNull(h.RecycleBin.TryResurrect("fresh-handle", WatchdogTestHarness.Now, h.Settings.RecycleBinTtl));
    }

    // ---- write-gate unavailable: an inconclusive, non-disruptive tick --------

    [TestMethod]
    public void RunTick_WriteGateUnavailable_ReturnsNullSupervisedCount_NoChangesMade()
    {
        using var h = new WatchdogTestHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Running);
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdleRunning);

        // Hold the mutex from a SEPARATE thread -- a same-thread WaitOne on an
        // already-owned Mutex is reentrant and would succeed instantly, which
        // would not exercise the busy-gate path at all.
        using var mutex = new Mutex(initiallyOwned: false);
        using var holderReady = new ManualResetEventSlim(false);
        using var releaseHolder = new ManualResetEventSlim(false);
        var holderThread = new Thread(() =>
        {
            mutex.WaitOne();
            holderReady.Set();
            releaseHolder.Wait();
            mutex.ReleaseMutex();
        })
        { IsBackground = true };
        holderThread.Start();
        holderReady.Wait();

        try
        {
            var busyGate = new WriteGate(mutex, timeout: TimeSpan.FromMilliseconds(50), log: h.Logs.Add);
            var result = WatchdogLoop.RunTick(h.Deps(gate: busyGate), WatchdogTestHarness.Now);

            Assert.IsNull(result.SupervisedCount);
            Assert.AreEqual(0, result.ExpiredCount);
            Assert.IsNotNull(h.Store.Find(view.Id), "an inconclusive tick must not have expired anything.");
        }
        finally
        {
            releaseHolder.Set();
            holderThread.Join();
        }
    }

    // ---- supervised count reflects post-expiry state -------------------------

    [TestMethod]
    public void RunTick_SupervisedCount_ReflectsLiveTasksAfterExpiryAndReaps()
    {
        using var h = new WatchdogTestHarness();
        var survivor = h.Store.SeedEntrylessTask("Survivor", "S", AppTaskState.Running);
        h.Sidecar.Write("keep", survivor.Id, WatchdogTestHarness.Now);

        var expiring = h.Store.SeedEntrylessTask("Expiring", "S", AppTaskState.Running);
        h.Sidecar.Write("expire", expiring.Id, WatchdogTestHarness.Now - h.Settings.IdleRunning);

        h.Store.SeedEntrylessTask("Entryless", "S"); // no sidecar entry -- reaped

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(1, result.SupervisedCount, "only the survivor should remain live.");
    }
}
