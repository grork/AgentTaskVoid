using Codevoid.AgentTaskVoid.LogicTests.Store;
using Codevoid.AgentTaskVoid.Persistence;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Persistence;

/// <summary>
/// Covers phase-04 acceptance criterion 3: a data-driven test covers all
/// four reconciliation rules using the fake's drift hooks (vanish,
/// HiddenByUser, seed-unknown), asserting entryless tasks are LEFT ALONE.
/// Per-handle resolution (the update-class scope) performs no
/// <c>FindAll()</c> and never sweeps -- asserted structurally via
/// <see cref="CountingAppTaskStore"/>.
/// </summary>
[TestClass]
public sealed class ReconcilerTests
{
    private static readonly Uri DeepLink = new("https://example.invalid/deep-link");
    private static readonly Uri IconUri = new("ms-appx:///Assets/Square44x44Logo.png");

    // ---- Per-handle resolution (update-class scope): rules 1-2 only -----

    [TestMethod]
    public void ResolveHandle_UnknownHandle_ReturnsUnknown_NothingToTouch()
    {
        using var dir = new TempDirectory();
        var fake = new FakeAppTaskStore();
        var sidecar = new SidecarStore(dir.Path);

        var result = Reconciler.ResolveHandle(fake, sidecar, "never-seen");

        Assert.AreEqual(ResolveOutcome.Unknown, result.Outcome);
        Assert.IsNull(result.Id);
    }

    [TestMethod]
    public void ResolveHandle_Rule1_LiveEntry_Keeps()
    {
        using var dir = new TempDirectory();
        var fake = new FakeAppTaskStore();
        var sidecar = new SidecarStore(dir.Path);
        var task = fake.Create("T", "S", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        sidecar.Write("h", task.Id, DateTimeOffset.Now);

        var result = Reconciler.ResolveHandle(fake, sidecar, "h");

        Assert.AreEqual(ResolveOutcome.Kept, result.Outcome);
        Assert.AreEqual(task.Id, result.Id);
        Assert.IsNotNull(sidecar.Read("h"), "a kept entry must not be touched");
    }

    [TestMethod]
    public void ResolveHandle_Rule2_VanishedTask_DropsEntry()
    {
        using var dir = new TempDirectory();
        var fake = new FakeAppTaskStore();
        var sidecar = new SidecarStore(dir.Path);
        var task = fake.Create("T", "S", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        sidecar.Write("h", task.Id, DateTimeOffset.Now);
        fake.SimulateVanish(task.Id);

        var result = Reconciler.ResolveHandle(fake, sidecar, "h");

        Assert.AreEqual(ResolveOutcome.Dropped, result.Outcome);
        Assert.IsNull(result.Id);
        Assert.IsNull(sidecar.Read("h"), "the stale entry must be dropped");
    }

    [TestMethod]
    public void ResolveHandle_HiddenTask_StillKept_NoSweep_MatchingErgo19()
    {
        // ERGO-19: update never sweeps. Per-handle resolution deliberately
        // ignores HiddenByUser -- rule 3's ACTION is full-pass-only, and this
        // path performs no sweep either way, so a hidden task's update-class
        // ops still resolve normally; cleanup is left to the next full pass.
        using var dir = new TempDirectory();
        var fake = new FakeAppTaskStore();
        var sidecar = new SidecarStore(dir.Path);
        var task = fake.Create("T", "S", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        sidecar.Write("h", task.Id, DateTimeOffset.Now);
        fake.SetHiddenByUser(task.Id, true);

        var result = Reconciler.ResolveHandle(fake, sidecar, "h");

        Assert.AreEqual(ResolveOutcome.Kept, result.Outcome);
        Assert.AreEqual(task.Id, result.Id);
        Assert.IsTrue(fake.Find(task.Id)!.HiddenByUser, "sanity: task is still hidden -- resolution did not clear the flag");
        Assert.IsNotNull(sidecar.Read("h"), "no sweep on the per-handle path -- entry left untouched");
        Assert.IsNotNull(fake.Find(task.Id), "no sweep on the per-handle path -- API task left untouched");
    }

    [TestMethod]
    public void ResolveHandle_NeverCallsFindAll_NeverCallsRemove()
    {
        using var dir = new TempDirectory();
        var fake = new FakeAppTaskStore();
        var counting = new CountingAppTaskStore(fake);
        var sidecar = new SidecarStore(dir.Path);
        var task = fake.Create("T", "S", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        sidecar.Write("live", task.Id, DateTimeOffset.Now);
        sidecar.Write("stale", "some-vanished-id", DateTimeOffset.Now);

        Reconciler.ResolveHandle(counting, sidecar, "live");
        Reconciler.ResolveHandle(counting, sidecar, "stale");
        Reconciler.ResolveHandle(counting, sidecar, "never-seen");

        Assert.AreEqual(0, counting.FindAllCallCount, "update-class resolution must never call FindAll()");
        Assert.AreEqual(0, counting.RemoveCallCount, "update-class resolution must never sweep (never call Remove())");
        Assert.IsGreaterThanOrEqualTo(2, counting.FindCallCount, "resolution should use single-Id Find(), not FindAll()");
    }

    // ---- Full four-rule pass: data-driven ---------------------------------

    [TestMethod]
    public void ReconcileAll_Rule1_LiveEntry_Kept()
    {
        using var dir = new TempDirectory();
        var fake = new FakeAppTaskStore();
        var sidecar = new SidecarStore(dir.Path);
        var live = fake.Create("Live", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        sidecar.Write("live-handle", live.Id, DateTimeOffset.Now);

        var summary = Reconciler.ReconcileAll(fake, sidecar);

        Assert.HasCount(1, summary.Kept);
        Assert.AreEqual("live-handle", summary.Kept[0].Handle);
        Assert.AreEqual(live.Id, summary.Kept[0].Id);
        Assert.IsEmpty(summary.DroppedStaleEntries);
        Assert.IsEmpty(summary.SweptHidden);
        Assert.IsNotNull(sidecar.Read("live-handle"));
        Assert.IsNotNull(fake.Find(live.Id));
    }

    [TestMethod]
    public void ReconcileAll_Rule2_VanishedTask_EntryDropped()
    {
        using var dir = new TempDirectory();
        var fake = new FakeAppTaskStore();
        var sidecar = new SidecarStore(dir.Path);
        var task = fake.Create("Gone", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        sidecar.Write("vanished-handle", task.Id, DateTimeOffset.Now);
        fake.SimulateVanish(task.Id);

        var summary = Reconciler.ReconcileAll(fake, sidecar);

        Assert.HasCount(1, summary.DroppedStaleEntries);
        Assert.AreEqual("vanished-handle", summary.DroppedStaleEntries[0].Handle);
        Assert.AreEqual(task.Id, summary.DroppedStaleEntries[0].Id);
        Assert.IsEmpty(summary.Kept);
        Assert.IsEmpty(summary.SweptHidden);
        Assert.IsNull(sidecar.Read("vanished-handle"));
    }

    [TestMethod]
    public void ReconcileAll_Rule3_HiddenByUser_RemovedAndEntryDropped()
    {
        using var dir = new TempDirectory();
        var fake = new FakeAppTaskStore();
        var sidecar = new SidecarStore(dir.Path);
        var task = fake.Create("Hidden", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        sidecar.Write("hidden-handle", task.Id, DateTimeOffset.Now);
        fake.SetHiddenByUser(task.Id, true);

        var summary = Reconciler.ReconcileAll(fake, sidecar);

        Assert.HasCount(1, summary.SweptHidden);
        Assert.AreEqual("hidden-handle", summary.SweptHidden[0].Handle);
        Assert.AreEqual(task.Id, summary.SweptHidden[0].Id);
        Assert.IsEmpty(summary.Kept);
        Assert.IsEmpty(summary.DroppedStaleEntries);
        Assert.IsNull(sidecar.Read("hidden-handle"), "entry must be dropped as part of the sweep");
        Assert.IsNull(fake.Find(task.Id), "the ERGO-2 sweep must Remove() the API task too");
    }

    [TestMethod]
    public void ReconcileAll_Rule4_EntrylessTask_LeftAlone_NeverTouched()
    {
        using var dir = new TempDirectory();
        var fake = new FakeAppTaskStore();
        var sidecar = new SidecarStore(dir.Path);
        var seeded = fake.SeedEntrylessTask("Orphan", "no entry ever knew this id");

        var summary = Reconciler.ReconcileAll(fake, sidecar);

        CollectionAssert.Contains(summary.EntrylessIds.ToArray(), seeded.Id);
        Assert.IsEmpty(summary.Kept);
        Assert.IsEmpty(summary.DroppedStaleEntries);
        Assert.IsEmpty(summary.SweptHidden);
        // NON-DESTRUCTIVE: the entryless task itself must still be live afterward.
        Assert.IsNotNull(fake.Find(seeded.Id));
    }

    [TestMethod]
    public void ReconcileAll_AllFourRulesTogether_DataDriven()
    {
        using var dir = new TempDirectory();
        var fake = new FakeAppTaskStore();
        var sidecar = new SidecarStore(dir.Path);

        var kept = fake.Create("Kept", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        sidecar.Write("kept-handle", kept.Id, DateTimeOffset.Now);

        var vanished = fake.Create("Vanished", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        sidecar.Write("vanished-handle", vanished.Id, DateTimeOffset.Now);
        fake.SimulateVanish(vanished.Id);

        var hidden = fake.Create("Hidden", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        sidecar.Write("hidden-handle", hidden.Id, DateTimeOffset.Now);
        fake.SetHiddenByUser(hidden.Id, true);

        var entryless = fake.SeedEntrylessTask("Entryless", "no entry");

        var summary = Reconciler.ReconcileAll(fake, sidecar);

        Assert.HasCount(1, summary.Kept);
        Assert.AreEqual(kept.Id, summary.Kept[0].Id);
        Assert.HasCount(1, summary.DroppedStaleEntries);
        Assert.AreEqual(vanished.Id, summary.DroppedStaleEntries[0].Id);
        Assert.HasCount(1, summary.SweptHidden);
        Assert.AreEqual(hidden.Id, summary.SweptHidden[0].Id);
        CollectionAssert.Contains(summary.EntrylessIds.ToArray(), entryless.Id);

        Assert.IsNotNull(fake.Find(kept.Id));
        Assert.IsNotNull(fake.Find(entryless.Id), "entryless must survive untouched");
        Assert.IsNull(fake.Find(hidden.Id), "hidden must be Remove()'d");
        Assert.IsNotNull(sidecar.Read("kept-handle"));
        Assert.IsNull(sidecar.Read("vanished-handle"));
        Assert.IsNull(sidecar.Read("hidden-handle"));
    }

    [TestMethod]
    public void ReconcileAll_UsesExactlyOneFindAll()
    {
        using var dir = new TempDirectory();
        var fake = new FakeAppTaskStore();
        var counting = new CountingAppTaskStore(fake);
        var sidecar = new SidecarStore(dir.Path);
        fake.Create("A", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));

        Reconciler.ReconcileAll(counting, sidecar);

        Assert.AreEqual(1, counting.FindAllCallCount, "the full pass must use exactly one FindAll(), not a per-entry Find() loop");
    }
}
