using Atv.Operations;

namespace Atv.LogicTests.Operations;

/// <summary>Covers `remove` (files-affected list: one of the seven verb cores): live-handle removal, clean no-op on an unknown handle, and the full-pass reconcile scoping it shares with `start`.</summary>
[TestClass]
public sealed class TaskOperationsRemoveTests
{
    [TestMethod]
    public void Remove_LiveHandle_RemovesTaskAndSidecarEntry()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        var id = h.Store.FindAll().Single().Id;

        var outcome = h.Ops.Remove("h1", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Removed, outcome.Kind);
        Assert.IsTrue(outcome.Success);
        Assert.IsNull(h.Store.Find(id));
        Assert.IsNull(h.Sidecar.Read("h1"));
    }

    [TestMethod]
    public void Remove_UnknownHandle_IsCleanNoOp()
    {
        using var h = new OperationsHarness();

        var outcome = h.Ops.Remove("never-seen", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.UnknownHandleNoOp, outcome.Kind);
        Assert.IsFalse(outcome.Success);
    }

    [TestMethod]
    public void Remove_DoesNotConsultTheRecycleBin_UnknownHandleIgnoresAStrayTombstone()
    {
        // Remove is not one of LIFE-15/21's update-class verbs -- there is nothing
        // sensible to "resurrect and then remove", so an absent handle is always a
        // plain no-op even if a tombstone happens to exist for it.
        using var h = new OperationsHarness();
        h.RecycleBin.Tombstone(new Atv.Persistence.RecycleRecord("h1", "T", "S", null, OperationsHarness.DeepLink, OperationsHarness.Now));

        var outcome = h.Ops.Remove("h1", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.UnknownHandleNoOp, outcome.Kind);
        Assert.IsEmpty(h.Store.FindAll());
    }

    [TestMethod]
    public void Remove_TriggersFullReconcilePass_SweepsAnOtherHiddenTask()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        h.StartNew("other");
        var otherId = h.Sidecar.Read("other")!.Id;
        h.Store.SetHiddenByUser(otherId, true);

        h.Ops.Remove("h1", OperationsHarness.Now);

        Assert.IsNull(h.Store.Find(otherId), "the ERGO-2 hidden sweep should have Remove()'d the unrelated hidden task as part of remove's full reconcile pass");
        Assert.IsNull(h.Sidecar.Read("other"));
    }
}
