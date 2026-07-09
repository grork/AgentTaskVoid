using Atv.Operations;
using Atv.Store;

namespace Atv.LogicTests.Operations;

/// <summary>Covers phase-05 acceptance criterion 4: `done`/`fail` bare vs `--summary` content-shape choice, both reaching their terminal state; `attention` setting the question on the one safe NeedsAttention cell.</summary>
[TestClass]
public sealed class TaskOperationsDoneFailAttentionTests
{
    [TestMethod]
    public void Done_Bare_KeepsSequenceOfSteps_ReachesCompleted()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        h.Ops.Step("h1", "step one", OperationsHarness.Now);

        var outcome = h.Ops.Done("h1", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
        Assert.AreEqual("step one", outcome.View.ExecutingStep, "bare done keeps the SequenceOfSteps content as read, just changes state");
        Assert.IsNotNull(outcome.View.EndTime);
    }

    [TestMethod]
    public void Done_WithSummary_ReachesCompleted_ContentShapeIsTextSummaryResult()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");

        var outcome = h.Ops.Done("h1", OperationsHarness.Now, summary: "All done.");

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
        // TextSummaryResult+Completed is a documented-safe cell -- Accepted (not
        // Refused) here is itself the proof the right shape was chosen; the fake's
        // AppTaskView has no readback for TextSummaryResult text by design (mirrors
        // the real platform), so state+outcome-kind is the observable surface.
    }

    [TestMethod]
    public void Fail_Bare_And_WithSummary_ReachErrorState()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        h.Ops.Step("h1", "working", OperationsHarness.Now);

        var bare = h.Ops.Fail("h1", OperationsHarness.Now);
        Assert.AreEqual(OutcomeKind.Accepted, bare.Kind);
        Assert.AreEqual(AppTaskState.Error, bare.View!.State);
        Assert.AreEqual("working", bare.View.ExecutingStep);

        using var h2 = new OperationsHarness();
        h2.StartNew("h2");
        var withSummary = h2.Ops.Fail("h2", OperationsHarness.Now, summary: "boom");
        Assert.AreEqual(OutcomeKind.Accepted, withSummary.Kind);
        Assert.AreEqual(AppTaskState.Error, withSummary.View!.State);
    }

    [TestMethod]
    public void Attention_SetsQuestion_OnTheSafeCell_ReachesNeedsAttention()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        h.Ops.Step("h1", "step one", OperationsHarness.Now);

        var outcome = h.Ops.Attention("h1", "Proceed with X?", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State);
        Assert.AreEqual("step one", outcome.View.ExecutingStep, "attention preserves the readable steps underneath the question");
    }

    [TestMethod]
    public void Attention_ThenDone_DropsTheQuestion_Succeeds()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        h.Ops.Attention("h1", "continue?", OperationsHarness.Now);

        var outcome = h.Ops.Done("h1", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind, "done must drop the question mutator so Completed + SequenceOfSteps stays safe");
        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
    }

    [TestMethod]
    public void Attention_ThenFail_DropsTheQuestion_Succeeds()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        h.Ops.Attention("h1", "continue?", OperationsHarness.Now);

        var outcome = h.Ops.Fail("h1", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Error, outcome.View!.State);
    }

    [TestMethod]
    public void Done_OnUnknownHandle_IsCleanNoOp()
    {
        using var h = new OperationsHarness();
        var outcome = h.Ops.Done("never-seen", OperationsHarness.Now);
        Assert.AreEqual(OutcomeKind.UnknownHandleNoOp, outcome.Kind);
    }

    [TestMethod]
    public void Attention_OnUnknownHandle_IsCleanNoOp()
    {
        using var h = new OperationsHarness();
        var outcome = h.Ops.Attention("never-seen", "q?", OperationsHarness.Now);
        Assert.AreEqual(OutcomeKind.UnknownHandleNoOp, outcome.Kind);
    }
}
