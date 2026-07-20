using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Diagnostics;

namespace Codevoid.AgentTaskVoid.LogicTests.Cli;

/// <summary>
/// AC2's `clear` coverage: purges every task + sidecar entry + per-handle
/// icon; recycle bin untouched by default and emptied with
/// `--include-recycle-bin`; no confirmation prompt (it just runs); a second
/// `clear` is a clean no-op; the canonical icon render-once cache survives;
/// entryless tasks are purged too (identity-global, ERGO-16); it IS a
/// write-path verb (ensures a watchdog is live).
/// </summary>
[TestClass]
public sealed class ClearVerbTests
{
    [TestMethod]
    public void Clear_PurgesEveryTask_SidecarEntry_AndPerHandleIcon()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");
        h.Run(dispatcher, "working", "h2");
        string iconH1 = h.Store.Find(h.Sidecar.Read("h1")!.Id)!.IconUri.LocalPath;
        string iconH2 = h.Store.Find(h.Sidecar.Read("h2")!.Id)!.IconUri.LocalPath;

        int exit = h.Run(dispatcher, "clear");

        Assert.AreEqual(0, exit);
        Assert.IsEmpty(h.Store.FindAll());
        Assert.IsNull(h.Sidecar.Read("h1"));
        Assert.IsNull(h.Sidecar.Read("h2"));
        Assert.IsFalse(File.Exists(iconH1), "ERGO-23: clear must reap every per-handle icon copy.");
        Assert.IsFalse(File.Exists(iconH2));
    }

    [TestMethod]
    public void Clear_EntrylessTask_AlsoRemoved_IdentityGlobal()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");
        h.Store.SeedEntrylessTask("Orphan", "Sub");
        Assert.HasCount(2, h.Store.FindAll());

        h.Run(dispatcher, "clear");

        Assert.IsEmpty(h.Store.FindAll());
    }

    [TestMethod]
    public void Clear_DefaultScope_RecycleBinUntouched()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");
        h.Run(dispatcher, "remove", "h1"); // ordinary remove doesn't tombstone; use a real recycle write directly
        h.RecycleBin.Tombstone(new Codevoid.AgentTaskVoid.Persistence.RecycleRecord("recycled-h", "T", "S", null, new Uri("https://example.invalid"), DispatcherHarness.Now));

        h.Run(dispatcher, "clear");

        Assert.IsNotNull(h.RecycleBin.TryResurrect("recycled-h", DispatcherHarness.Now, TimeSpan.FromDays(365)), "default clear must NOT touch the recycle bin.");
    }

    [TestMethod]
    public void Clear_IncludeRecycleBin_WipesTombstonesAndTheirIcons()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.RecycleBin.Tombstone(new Codevoid.AgentTaskVoid.Persistence.RecycleRecord("recycled-h", "T", "S", null, new Uri("https://example.invalid"), DispatcherHarness.Now));

        h.Run(dispatcher, "clear", "--include-recycle-bin");

        Assert.IsNull(h.RecycleBin.TryResurrect("recycled-h", DispatcherHarness.Now, TimeSpan.FromDays(365)), "--include-recycle-bin must wipe every tombstone.");
    }

    [TestMethod]
    public void Clear_Twice_SecondIsACleanNoOp()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        h.Run(dispatcher, "clear");
        int secondExit = h.Run(dispatcher, "clear");

        Assert.AreEqual(0, secondExit);
        Assert.IsEmpty(h.Store.FindAll());
        Assert.IsEmpty(h.Sidecar.ReadAll());
    }

    [TestMethod]
    public void Clear_RenderCacheSurvives()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        // Force a cache render (default glyph) via a real start, then clear.
        h.Run(dispatcher, "working", "h1");
        string? cacheFile = Directory.Exists(Path.Combine(h.IconsCacheDir))
            ? Directory.GetFiles(h.IconsCacheDir).FirstOrDefault()
            : null;
        Assert.IsNotNull(cacheFile, "arrange: the default-glyph render must have populated the cache.");

        h.Run(dispatcher, "clear");

        Assert.IsTrue(File.Exists(cacheFile), "ERGO-23: the canonical render-once cache is a pure accelerator -- clear must never delete it.");
    }

    [TestMethod]
    public void Clear_NoConfirmationNeeded_RunsImmediately()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int exit = h.Run(dispatcher, "clear");

        Assert.AreEqual(0, exit, "ERGO-27 C4: clear executes immediately, no prompt/gate.");
        Assert.IsEmpty(h.Store.FindAll());
    }

    [TestMethod]
    public void Clear_EnsuresWatchdog_IsAWritePathVerb()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(watchdogMode: WatchdogMode.Spawn);

        h.Run(dispatcher, "clear");

        Assert.AreEqual(1, h.ProcessHost.StartCallCount, "clear mutates -- it must ensure a watchdog is live like every other write-path verb.");
    }

    [TestMethod]
    public void Clear_Json_Success_PrintsOkTrueShape()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(json: true);
        h.Run(dispatcher, "working", "h1");
        h.Stdout.GetStringBuilder().Clear();

        h.Run(dispatcher, "clear");

        StringAssert.Contains(h.Stdout.ToString(), "\"ok\":true");
    }

    [TestMethod]
    public void Clear_NoIdentity_NonStrict_ExitsZero_Silent_NoWrite()
    {
        using var h = new DispatcherHarness { HasIdentity = false };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "clear");

        Assert.AreEqual(0, exit);
        Assert.HasCount(1, h.LogEntriesExcludingTrace());
    }

    [TestMethod]
    public void Clear_NoIdentity_Strict_ReturnsIdentityNotRegisteredExitCode()
    {
        using var h = new DispatcherHarness { HasIdentity = false };
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "clear");

        Assert.AreEqual((int)FailureKind.IdentityNotRegistered, exit);
    }
}
