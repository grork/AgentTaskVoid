using Atv.Operations;
using Atv.Store;

namespace Atv.LogicTests.Operations;

/// <summary>Covers phase-05 acceptance criterion 3 (`state` accepts only running/paused) and the "leaving NeedsAttention drops the question" half of acceptance criterion 4.</summary>
[TestClass]
public sealed class TaskOperationsStateTests
{
    [TestMethod]
    public void SetState_Running_IsAccepted()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");

        var outcome = h.Ops.SetState("h1", AppTaskState.Running, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Running, outcome.View!.State);
    }

    [TestMethod]
    public void SetState_Paused_IsAccepted()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");

        var outcome = h.Ops.SetState("h1", AppTaskState.Paused, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Paused, outcome.View!.State);
    }

    [TestMethod]
    [DataRow(AppTaskState.Completed)]
    [DataRow(AppTaskState.Error)]
    [DataRow(AppTaskState.NeedsAttention)]
    public void SetState_AnythingOtherThanRunningOrPaused_IsRefusedAsInvalidArgument_NoStoreWrite(AppTaskState requested)
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        var before = h.Store.FindAll().Single();

        var outcome = h.Ops.SetState("h1", requested, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.RefusedInvalidArgument, outcome.Kind);
        Assert.IsFalse(outcome.Success);
        var after = h.Store.FindAll().Single();
        Assert.AreEqual(before.State, after.State, "no store write for an invalid state argument");
    }

    [TestMethod]
    public void SetState_InvalidArgument_NeverTouchesTheWriteGateOrRecycleBin()
    {
        // The invalid-argument check must short-circuit before any resolve/reconcile --
        // it rejects even a handle that was never started.
        using var h = new OperationsHarness();

        var outcome = h.Ops.SetState("never-started", AppTaskState.Completed, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.RefusedInvalidArgument, outcome.Kind);
        Assert.IsEmpty(h.Store.FindAll());
    }

    [TestMethod]
    public void SetState_Running_AfterAttention_LeavesNeedsAttention_AndDropsTheQuestion()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        var attention = h.Ops.Attention("h1", "continue?", OperationsHarness.Now);
        Assert.AreEqual(OutcomeKind.Accepted, attention.Kind);
        Assert.AreEqual(AppTaskState.NeedsAttention, h.Store.FindAll().Single().State);

        // If the question were still attached, Running + SequenceOfSteps + question
        // would be OUTSIDE the safe set and this call would be refused instead.
        var outcome = h.Ops.SetState("h1", AppTaskState.Running, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind, "leaving NeedsAttention must drop the question mutator so the rebuilt content is safe under Running");
        Assert.AreEqual(AppTaskState.Running, outcome.View!.State);

        // Confirming follow-up: step (which also emits a bare, question-less
        // SequenceOfSteps) now succeeds too, whereas it was refused while NeedsAttention.
        var step = h.Ops.Step("h1", "step one", OperationsHarness.Now);
        Assert.AreEqual(OutcomeKind.Accepted, step.Kind);
    }

    [TestMethod]
    public void SetState_OnUnknownHandle_IsCleanNoOp()
    {
        using var h = new OperationsHarness();

        var outcome = h.Ops.SetState("never-seen", AppTaskState.Running, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.UnknownHandleNoOp, outcome.Kind);
    }
}
