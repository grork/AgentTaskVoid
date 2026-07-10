using Atv.Icons;
using Atv.Persistence;
using Atv.Store;
using Atv.Watchdog;

namespace Atv.LogicTests.Watchdog;

/// <summary>
/// AC4's automatable half: <see cref="BootRecovery.FlatClear"/>'s
/// unconditional-clear state machine (unit-testable end to end via fakes/temp
/// dirs). <see cref="BootRecovery.IsStartupTaskActivation"/> itself is a real
/// WinRT activation-boundary call, untestable here by construction (see its
/// own doc comment) -- the physical reboot verification is AC4's flagged
/// operator-manual check.
/// </summary>
[TestClass]
public sealed class BootRecoveryTests
{
    private static readonly Uri DeepLink = new("https://example.invalid/deep-link");
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void FlatClear_RemovesEverySidecarMappedTask_AndItsSidecarEntryAndIcon()
    {
        using var h = new WatchdogTestHarness();
        Uri icon = h.Icons.Place("h1", IconTokens.Default);
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Running, deepLink: DeepLink, iconUri: icon);
        h.Sidecar.Write("h1", view.Id, Now);

        int cleared = BootRecovery.FlatClear(h.Deps());

        Assert.AreEqual(1, cleared);
        Assert.IsNull(h.Store.Find(view.Id));
        Assert.IsNull(h.Sidecar.Read("h1"));
        Assert.IsFalse(File.Exists(icon.LocalPath));
    }

    [TestMethod]
    public void FlatClear_AlsoRemovesEntrylessTasks_UnconditionalNotSidecarScoped()
    {
        using var h = new WatchdogTestHarness();
        var orphan = h.Store.SeedEntrylessTask("Orphan", "no entry");

        int cleared = BootRecovery.FlatClear(h.Deps());

        Assert.AreEqual(1, cleared);
        Assert.IsNull(h.Store.Find(orphan.Id));
    }

    [TestMethod]
    public void FlatClear_MixOfMappedAndEntryless_ClearsBoth_CorrectCount()
    {
        using var h = new WatchdogTestHarness();
        var mapped = h.Store.SeedEntrylessTask("Mapped", "S", AppTaskState.Running, deepLink: DeepLink);
        h.Sidecar.Write("h1", mapped.Id, Now);
        var orphan = h.Store.SeedEntrylessTask("Orphan", "no entry");

        int cleared = BootRecovery.FlatClear(h.Deps());

        Assert.AreEqual(2, cleared);
        Assert.IsEmpty(h.Store.FindAll());
    }

    [TestMethod]
    public void FlatClear_WipesTheWholeRecycleBin_RecordsAndCoLocatedIcons()
    {
        using var h = new WatchdogTestHarness();
        h.Icons.Place("recycled-handle", IconTokens.Default);
        h.Icons.MoveToRecycle("recycled-handle");
        h.RecycleBin.Tombstone(new RecycleRecord("recycled-handle", "T", "S", null, DeepLink, Now));

        BootRecovery.FlatClear(h.Deps());

        Assert.IsNull(h.RecycleBin.TryResurrect("recycled-handle", Now, TimeSpan.FromDays(365)),
            "the whole recycle bin must be wiped -- the internal exception to clear's default recycle exclusion.");
        Assert.IsEmpty(Directory.Exists(h.RecycleDirPath) ? Directory.GetFiles(h.RecycleDirPath) : []);
    }

    [TestMethod]
    public void FlatClear_SweepsOrphanLiveIconFiles_EvenWithNoOwningHandle()
    {
        using var h = new WatchdogTestHarness();
        // A live icon file with no sidecar entry and no live task at all -- a pure filesystem leftover.
        h.Icons.Place("leftover", IconTokens.Default);

        BootRecovery.FlatClear(h.Deps());

        // SweepOrphans([], []) treats every handles/ file as unowned -- assert via
        // IconService's own reap contract rather than a raw path (avoids depending
        // on its private layout): if FlatClear already removed it, a second reap
        // attempt finds nothing left to do.
        Assert.IsFalse(h.Icons.ReapLiveIcon("leftover"), "the orphan sweep inside FlatClear must already have removed it.");
    }

    [TestMethod]
    public void FlatClear_EmptyState_NoOp_ReturnsZero_NoThrow()
    {
        using var h = new WatchdogTestHarness();
        int cleared = BootRecovery.FlatClear(h.Deps());
        Assert.AreEqual(0, cleared);
    }

    [TestMethod]
    public void FlatClear_WriteGateUnavailable_SkipsNonDisruptively_ReturnsZero()
    {
        using var h = new WatchdogTestHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Running);
        h.Sidecar.Write("h1", view.Id, Now);

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
            int cleared = BootRecovery.FlatClear(h.Deps(gate: busyGate));

            Assert.AreEqual(0, cleared);
            Assert.IsNotNull(h.Store.Find(view.Id), "a mutex-unavailable pass must change nothing.");
        }
        finally
        {
            releaseHolder.Set();
            holderThread.Join();
        }
    }
}
