using Atv.Operations;
using Atv.Store;

namespace Atv.LogicTests.Operations;

/// <summary>Covers phase-05 acceptance criterion 2: the advance model wired through `step` -- executing=Nth, completedSteps FIFO in order, RMW reads from the live store, state preservation, and refusal on a NeedsAttention card.</summary>
[TestClass]
public sealed class TaskOperationsStepTests
{
    [TestMethod]
    public void Step_Sequential_ExecutingIsNth_CompletedStepsInOrder_CappedAtTen()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");

        for (int i = 1; i <= 12; i++)
        {
            var outcome = h.Ops.Step("h1", $"step{i}", OperationsHarness.Now);
            Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind, $"step{i} should be accepted");
        }

        var view = h.Store.FindAll().Single();
        Assert.AreEqual("step12", view.ExecutingStep);
        Assert.HasCount(10, view.CompletedSteps);
        CollectionAssert.AreEqual(
            new[] { "step2", "step3", "step4", "step5", "step6", "step7", "step8", "step9", "step10", "step11" },
            view.CompletedSteps.ToArray());
    }

    [TestMethod]
    public void Step_Rmw_ReadsFromTheLiveStore_NotACache()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        h.Ops.Step("h1", "step one", OperationsHarness.Now);
        var id = h.Store.FindAll().Single().Id;

        // Mutate the live store OUTSIDE of TaskOperations -- simulates another
        // writer (or a differently-scoped call) changing the content between
        // our two Step calls.
        h.Store.Update(id, AppTaskState.Running, new AppTaskContentDto.SequenceOfSteps(["externally set"], "external executing"));

        var outcome = h.Ops.Step("h1", "step two", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        CollectionAssert.AreEqual(new[] { "externally set", "external executing" }, outcome.View!.CompletedSteps.ToArray(),
            "step's RMW must read the live store's CURRENT steps, not a value cached from an earlier call");
        Assert.AreEqual("step two", outcome.View.ExecutingStep);
    }

    [TestMethod]
    public void Step_PreservesTheCardsCurrentState_Paused()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        var setPaused = h.Ops.SetState("h1", AppTaskState.Paused, OperationsHarness.Now);
        Assert.AreEqual(OutcomeKind.Accepted, setPaused.Kind);

        var outcome = h.Ops.Step("h1", "step one", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Paused, outcome.View!.State, "step re-sends the state it read -- Paused stays Paused");
        Assert.AreEqual("step one", outcome.View.ExecutingStep);
    }

    [TestMethod]
    public void Step_OnNeedsAttentionCard_IsRefused_NoStoreWrite_Logged()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        h.Ops.Step("h1", "step one", OperationsHarness.Now);
        var attention = h.Ops.Attention("h1", "continue?", OperationsHarness.Now);
        Assert.AreEqual(OutcomeKind.Accepted, attention.Kind);

        var before = h.Store.FindAll().Single();

        var outcome = h.Ops.Step("h1", "step two", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.RefusedUnsafeCombo, outcome.Kind);
        var after = h.Store.FindAll().Single();
        Assert.AreEqual(before.State, after.State, "no store write on refusal");
        Assert.AreEqual(before.ExecutingStep, after.ExecutingStep, "no store write on refusal");
        CollectionAssert.AreEqual(before.CompletedSteps.ToArray(), after.CompletedSteps.ToArray(), "no store write on refusal");
        Assert.IsTrue(h.Logs.Any(l => l.Contains("h1", StringComparison.Ordinal)), "refusal must be logged");
    }

    [TestMethod]
    public void Step_OnNeedsAttentionCard_WithUnsafeBypass_EmitsAnyway()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        h.Ops.Attention("h1", "continue?", OperationsHarness.Now);

        var outcome = h.Ops.Step("h1", "step two", OperationsHarness.Now, unsafeBypass: true);

        Assert.AreEqual(OutcomeKind.AcceptedUnsafe, outcome.Kind);
        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State);
        Assert.AreEqual("step two", outcome.View.ExecutingStep);
    }

    [TestMethod]
    public void Step_OnUnknownHandle_NeverSeen_IsCleanNoOp()
    {
        using var h = new OperationsHarness();

        var outcome = h.Ops.Step("never-seen", "step one", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.UnknownHandleNoOp, outcome.Kind);
        Assert.IsFalse(outcome.Success);
        Assert.IsEmpty(h.Store.FindAll());
    }
}
