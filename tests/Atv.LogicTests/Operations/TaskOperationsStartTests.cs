using Atv.Operations;
using Atv.Store;

namespace Atv.LogicTests.Operations;

/// <summary>Covers phase-05 acceptance criterion 5: `start` upsert -- new-handle create, live-handle adopt (preserve steps / re-apply fields), `--reset`, and the icon-token-change Remove+Create path.</summary>
[TestClass]
public sealed class TaskOperationsStartTests
{
    [TestMethod]
    public void Start_NewHandle_Creates_Running_WithGivenFields()
    {
        using var h = new OperationsHarness();

        var outcome = h.Ops.Start("h1", "My Title", "My Subtitle", OperationsHarness.IconUri, OperationsHarness.DeepLink, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.IsTrue(outcome.Success);
        Assert.IsNotNull(outcome.View);
        Assert.AreEqual("My Title", outcome.View!.Title);
        Assert.AreEqual("My Subtitle", outcome.View.Subtitle);
        Assert.AreEqual(AppTaskState.Running, outcome.View.State);
        Assert.IsEmpty(outcome.View.CompletedSteps);
        Assert.AreEqual("", outcome.View.ExecutingStep);

        var sidecarEntry = h.Sidecar.Read("h1");
        Assert.IsNotNull(sidecarEntry);
        Assert.AreEqual(outcome.View.Id, sidecarEntry!.Id);
    }

    [TestMethod]
    public void Start_IsValidInTheValidator_NeverRefused()
    {
        // ERGO-25/ERGO-10: start-on-live-handle is explicitly VALID, not an unsupported combination.
        using var h = new OperationsHarness();
        h.StartNew("h1");

        var outcome = h.Ops.Start("h1", "T2", "S2", OperationsHarness.IconUri, OperationsHarness.DeepLink, OperationsHarness.Now);

        Assert.AreNotEqual(OutcomeKind.RefusedUnsafeCombo, outcome.Kind);
        Assert.IsTrue(outcome.Success);
    }

    [TestMethod]
    public void Start_OnLiveHandle_SameIcon_PreservesStepHistory_AndReAppliesFields()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1", title: "Old Title", subtitle: "Old Subtitle");
        h.Ops.Step("h1", "step one", OperationsHarness.Now);
        h.Ops.Step("h1", "step two", OperationsHarness.Now);

        var beforeId = h.Store.FindAll().Single().Id;

        var outcome = h.Ops.Start("h1", "New Title", "New Subtitle", OperationsHarness.IconUri, OperationsHarness.DeepLink, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(beforeId, outcome.View!.Id, "same icon token -- adopted in place, no new Id");
        Assert.AreEqual("New Title", outcome.View.Title);
        Assert.AreEqual("New Subtitle", outcome.View.Subtitle);
        Assert.AreEqual(AppTaskState.Running, outcome.View.State);
        CollectionAssert.AreEqual(new[] { "step one" }, outcome.View.CompletedSteps.ToArray(), "step history preserved across re-start");
        Assert.AreEqual("step two", outcome.View.ExecutingStep);
    }

    [TestMethod]
    public void Start_OnLiveHandle_WithReset_ClearsStepHistory_ButKeepsSameId()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        h.Ops.Step("h1", "step one", OperationsHarness.Now);
        var beforeId = h.Store.FindAll().Single().Id;

        var outcome = h.Ops.Start("h1", "Title", "Subtitle", OperationsHarness.IconUri, OperationsHarness.DeepLink, OperationsHarness.Now, reset: true);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(beforeId, outcome.View!.Id, "--reset adopts in place (same icon) -- it does not mint a new Id");
        Assert.IsEmpty(outcome.View.CompletedSteps);
        Assert.AreEqual("", outcome.View.ExecutingStep);
    }

    [TestMethod]
    public void Start_OnLiveHandle_DifferentIconToken_ForcesRemoveCreate_NewId_StepsGone()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1", iconUri: OperationsHarness.IconUri);
        h.Ops.Step("h1", "step one", OperationsHarness.Now);
        h.Ops.Step("h1", "step two", OperationsHarness.Now);
        var oldId = h.Store.FindAll().Single().Id;

        var outcome = h.Ops.Start("h1", "Title", "Subtitle", OperationsHarness.OtherIconUri, OperationsHarness.DeepLink, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.IsTrue(outcome.IconChanged);
        Assert.AreNotEqual(oldId, outcome.View!.Id, "a different icon token forces platform Remove+Create -- new Id");
        Assert.IsEmpty(outcome.View.CompletedSteps, "step history is lost across a forced Remove+Create");
        Assert.AreEqual("", outcome.View.ExecutingStep);
        Assert.AreEqual(OperationsHarness.OtherIconUri, outcome.View.IconUri);

        // Old task is actually gone (Remove ran), not just orphaned.
        Assert.IsNull(h.Store.Find(oldId));
        Assert.HasCount(1, h.Store.FindAll(), "exactly one live task remains after the Remove+Create pair");

        // Sidecar now maps the handle to the NEW id.
        Assert.AreEqual(outcome.View.Id, h.Sidecar.Read("h1")!.Id);
    }

    [TestMethod]
    public void Start_TriggersFullReconcilePass_SweepsAnOtherHiddenTask()
    {
        // "full pass on start/remove" (phase-04 scoping): a start call should reconcile
        // the WHOLE sidecar, not just its own handle -- proven here via an unrelated
        // hidden task getting swept as a side effect of calling start.
        using var h = new OperationsHarness();
        h.StartNew("other");
        var otherId = h.Store.FindAll().Single().Id;
        h.Store.SetHiddenByUser(otherId, true);

        h.Ops.Start("h1", "Title", "Subtitle", OperationsHarness.IconUri, OperationsHarness.DeepLink, OperationsHarness.Now);

        Assert.IsNull(h.Store.Find(otherId), "the ERGO-2 hidden sweep should have Remove()'d the unrelated hidden task");
        Assert.IsNull(h.Sidecar.Read("other"));
    }
}
