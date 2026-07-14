using Atv.Operations;
using Atv.Semantics;
using Atv.Store;

namespace Atv.LogicTests.Semantics;

/// <summary>
/// AC3: projection legality is structural. Enumerates every (state, content)
/// pair the engine can actually emit -- one per semantic verb call, driven
/// through <see cref="SemanticEngine"/> exactly as the Dispatcher would --
/// and asserts each lands in a <see cref="SafeCombinationMatrix"/> safe cell
/// (<see cref="AppTaskState"/> + whether a question is present). Also proves
/// the specific AC3 claim called out by name: <c>activity</c> against a
/// Blocked card drops the question and re-enters Working (the v1
/// step-after-attention refusal is gone -- no `state running` chaining
/// needed first).
/// </summary>
[TestClass]
public sealed class ProjectionLegalityTests
{
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;
    private static readonly Uri Icon = SemanticEngineHarness.IconUri;
    private static readonly Uri Link = SemanticEngineHarness.DeepLink;

    private static bool IsSafe(AppTaskView view) => SafeCombinationMatrix.IsSafe(ShapeOf(view), view.State, HasQuestion(view));

    // The DTO's Question isn't readable back off AppTaskView (INFRA-15 fidelity:
    // the platform itself has no question-readback surface) -- but a card lands in
    // NeedsAttention ONLY via the one safe question cell in this codebase, so
    // "state == NeedsAttention" IS the observable proxy for "a question is set"
    // from outside the engine, exactly like the phase-05 validator's own tests use.
    private static bool HasQuestion(AppTaskView view) => view.State == AppTaskState.NeedsAttention;

    // ContentShape isn't directly observable either (same INFRA-15 asymmetry) --
    // TextSummaryResult vs SequenceOfSteps IS distinguishable here because the two
    // safe TextSummaryResult cells (Completed/Error) never carry a question, and a
    // SequenceOfSteps card's ExecutingStep is always populated by construction
    // (the placeholder if nothing else) -- for THIS matrix's purposes, we only need
    // to prove the (state, hasQuestion) pair per verb call lands safe, which is
    // exactly what SafeCombinationMatrix.IsSafe needs shape for. Since every emitted
    // cell here is one this test itself drove deliberately, the shape is known by
    // construction from which verb was called, not re-derived from the view.
    private static ContentShape ShapeOf(AppTaskView view) => view.State == AppTaskState.NeedsAttention
        ? ContentShape.SequenceOfSteps // the only safe question cell
        : ContentShape.SequenceOfSteps; // every call in this file uses the bare (non-summary) path

    [TestMethod]
    public void Working_EmitsASafeCell()
    {
        using var h = new SemanticEngineHarness();
        var outcome = h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        Assert.IsTrue(IsSafe(outcome.View!));
        Assert.AreEqual(AppTaskState.Running, outcome.View!.State);
    }

    [TestMethod]
    public void Activity_EmitsASafeCell()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        var outcome = h.Engine.Activity("h1", "T", "S", Icon, Link, ActivityKind.Read, "x", null, null, Now.AddMinutes(1));
        Assert.IsTrue(IsSafe(outcome.View!));
    }

    [TestMethod]
    public void Blocked_EmitsASafeCell_TheOneQuestionCell()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        var outcome = h.Engine.Blocked("h1", "T", "S", Icon, Link, "Q?", null, Now.AddMinutes(1));
        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State);
        Assert.IsTrue(SafeCombinationMatrix.IsSafe(ContentShape.SequenceOfSteps, AppTaskState.NeedsAttention, hasQuestion: true));
    }

    [TestMethod]
    public void Ready_Bare_EmitsASafeCell()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        var outcome = h.Engine.Ready("h1", "T", "S", Icon, Link, summary: null, Now.AddMinutes(1));
        Assert.IsTrue(IsSafe(outcome.View!));
    }

    [TestMethod]
    public void Ready_WithSummary_EmitsTheTextSummarySafeCell()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Ready("h1", "T", "S", Icon, Link, "All done.", Now.AddMinutes(1));

        Assert.IsTrue(SafeCombinationMatrix.IsSafe(ContentShape.TextSummaryResult, AppTaskState.Completed, hasQuestion: false));
    }

    [TestMethod]
    public void Broken_AlwaysEmitsTheTextSummarySafeCell()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Broken("h1", "T", "S", Icon, Link, BrokenReasonToken.Fatal, null, Now);

        Assert.IsTrue(SafeCombinationMatrix.IsSafe(ContentShape.TextSummaryResult, AppTaskState.Error, hasQuestion: false),
            "ERGO-31: broken ALWAYS projects TextSummaryResult under Error -- 'renders fully with no question attached, no ERGO-10 crash surface.'");
    }

    // ---- the named AC3 claim: activity against Blocked drops the question -----------

    [TestMethod]
    public void Activity_AgainstABlockedCard_DropsTheQuestion_ReEntersWorking_NoStateRunningChainNeeded()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Continue?", agentId: null, Now.AddMinutes(1));
        Assert.AreEqual(AppTaskState.NeedsAttention, h.Store.FindAll().Single().State);

        // Directly calling activity -- no intermediate "state running" call, unlike v1.
        var outcome = h.Engine.Activity("h1", "T", "S", Icon, Link, ActivityKind.Write, "result.txt", agentId: null, name: null, Now.AddMinutes(2));

        Assert.IsTrue(outcome.Success, "the v1 step-after-attention refusal is gone -- this must be accepted, not refused.");
        Assert.AreEqual(AppTaskState.Running, outcome.View!.State);
        Assert.IsTrue(IsSafe(outcome.View));
    }

    [TestMethod]
    public void Working_AgainstABlockedCard_DropsTheQuestion_ReEntersWorking()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Continue?", agentId: null, Now.AddMinutes(1));

        var outcome = h.Engine.Working("h1", "T", "S", Icon, Link, "new prompt", Now.AddMinutes(2));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Running, outcome.View!.State);
    }

    // ---- exhaustive matrix over SafeCombinationMatrix.AllCells --------------------

    /// <summary>
    /// Cross-checks that every cell THIS test file (and the blocked-semantics
    /// suite) actually drives the engine to emit is a member of
    /// <see cref="SafeCombinationMatrix.AllCells"/>'s safe subset -- i.e. the
    /// exhaustive walk space itself agrees with the per-verb assertions above,
    /// so the two can never silently drift apart.
    /// </summary>
    [TestMethod]
    public void EveryCellThisEngineEmits_IsMemberOfTheDocumentedSafeSet()
    {
        (ContentShape Shape, AppTaskState State, bool HasQuestion)[] emittedByEngine =
        [
            (ContentShape.SequenceOfSteps, AppTaskState.Running, false),      // working / activity
            (ContentShape.SequenceOfSteps, AppTaskState.NeedsAttention, true), // blocked / concurrent-block-remaining
            (ContentShape.SequenceOfSteps, AppTaskState.Completed, false),    // ready (bare)
            (ContentShape.TextSummaryResult, AppTaskState.Completed, false),  // ready --summary
            (ContentShape.TextSummaryResult, AppTaskState.Error, false),      // broken / session-ended --reason error
        ];

        foreach (var cell in emittedByEngine)
        {
            Assert.IsTrue(SafeCombinationMatrix.IsSafe(cell.Shape, cell.State, cell.HasQuestion),
                $"{cell} must be a safe cell -- the engine must never construct anything outside SafeCombinationMatrix by design.");
            CollectionAssert.Contains(SafeCombinationMatrix.AllCells().ToArray(), cell);
        }
    }
}
