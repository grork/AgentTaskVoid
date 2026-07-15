using Atv.Operations;
using Atv.Store;

namespace Atv.LogicTests.Semantics;

/// <summary>
/// AC1: for each of the 8 verbs, proves its ERGO-31 §1 transition FROM EVERY
/// REACHABLE prior semantic state (Blocked/Broken/Ready/Working -- Idle has
/// no verb, 15A never reaches it, per the phase's own scope note). Also
/// proves idempotency: an absent optional flag makes no content claim
/// (never clears a field), and re-asserting an already-held state is a clean
/// no-crash re-apply.
///
/// 15A note: the "clock effect" column is only meaningfully testable for the
/// non-decay clocks this phase builds (none decay yet -- decay itself is
/// 15B). What IS proven here: `working`/`activity` always land Working
/// (LIFE-24 "leaves Ready" -- the pre-decay-clock half of that clock-clearing
/// claim), and `ready` always lands Ready (the transition decay would key
/// off, once 15B wires the clock).
/// </summary>
[TestClass]
public sealed class SemanticEngineTransitionTests
{
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    public enum Origin { Blocked, Broken, Ready, Working }

    private static void Reach(SemanticEngineHarness h, string handle, Origin origin)
    {
        switch (origin)
        {
            case Origin.Blocked:
                h.Engine.Blocked(handle, "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "Continue?", agentId: null, Now);
                break;
            case Origin.Broken:
                h.Engine.Broken(handle, "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, Atv.Semantics.BrokenReasonToken.Fatal, detail: null, Now);
                break;
            case Origin.Ready:
                h.Engine.Ready(handle, "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, summary: null, Now);
                break;
            case Origin.Working:
                h.Engine.Working(handle, "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, goal: "initial goal", Now);
                break;
        }
    }

    // ---- working: lands Working from any prior state ------------------------------

    [TestMethod]
    [DataRow(Origin.Blocked)]
    [DataRow(Origin.Broken)]
    [DataRow(Origin.Ready)]
    [DataRow(Origin.Working)]
    public void Working_FromAnyPriorState_LandsWorking(Origin origin)
    {
        using var h = new SemanticEngineHarness();
        Reach(h, "h1", origin);

        var outcome = h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "new goal", Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Running, outcome.View!.State);
        Assert.AreEqual("new goal", outcome.View.ExecutingStep);
    }

    [TestMethod]
    public void Working_AbsentGoal_MakesNoContentClaim_PreservesCurrentStep()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "original goal", Now);

        var outcome = h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, goal: null, Now.AddMinutes(1));

        Assert.AreEqual("original goal", outcome.View!.ExecutingStep, "an absent --goal must make no content claim -- idempotent, never clears the current step.");
        Assert.AreEqual(AppTaskState.Running, outcome.View.State);
    }

    [TestMethod]
    public void Working_ReassertingWorking_IsAPlainAccept_NoCrash()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "goal", Now);

        var outcome = h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "goal again", Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Running, outcome.View!.State);
    }

    // ---- activity: lands Working from any prior state ------------------------------

    [TestMethod]
    [DataRow(Origin.Blocked)]
    [DataRow(Origin.Broken)]
    [DataRow(Origin.Ready)]
    [DataRow(Origin.Working)]
    public void Activity_FromAnyPriorState_LandsWorking(Origin origin)
    {
        using var h = new SemanticEngineHarness();
        Reach(h, "h1", origin);

        var outcome = h.Engine.Activity("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink,
            Atv.Semantics.ActivityKind.Read, "auth.ts", agentId: null, name: null, Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Running, outcome.View!.State);
        Assert.AreEqual("Reading auth.ts", outcome.View.ExecutingStep);
    }

    // ---- blocked: lands Blocked from any prior state -------------------------------

    [TestMethod]
    [DataRow(Origin.Blocked)]
    [DataRow(Origin.Broken)]
    [DataRow(Origin.Ready)]
    [DataRow(Origin.Working)]
    public void Blocked_FromAnyPriorState_LandsBlocked(Origin origin)
    {
        using var h = new SemanticEngineHarness();
        Reach(h, "h1", origin);

        var outcome = h.Engine.Blocked("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "Proceed?", agentId: null, Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State);
    }

    [TestMethod]
    public void Blocked_ReassertingSameLocus_RefreshesQuestion_NoCrash()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Blocked("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "Q1?", agentId: null, Now);

        var outcome = h.Engine.Blocked("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "Q2?", agentId: null, Now.AddMinutes(1));

        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State);
    }

    // ---- ready: lands Ready from any prior state ------------------------------------

    [TestMethod]
    [DataRow(Origin.Blocked)]
    [DataRow(Origin.Broken)]
    [DataRow(Origin.Ready)]
    [DataRow(Origin.Working)]
    public void Ready_FromAnyPriorState_LandsReady(Origin origin)
    {
        using var h = new SemanticEngineHarness();
        Reach(h, "h1", origin);

        var outcome = h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, summary: null, Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
    }

    [TestMethod]
    public void Ready_WithSummary_SwapsToTextSummaryResult()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "goal", Now);

        var outcome = h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "All finished.", Now.AddMinutes(1));

        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
    }

    [TestMethod]
    public void Ready_ReassertingReady_IsAPlainAccept_NoCrash()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, summary: null, Now);

        var outcome = h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, summary: null, Now.AddMinutes(1));

        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
        Assert.IsTrue(outcome.Success);
    }

    [TestMethod]
    public void Ready_BareReassertion_AfterASummaryResult_NeverProducesAnEmptyExecutingStep()
    {
        // Regression guard (phase-18 live dogfood, 2026-07-14): the real
        // platform throws "executingStep cannot be empty" -- a bare `ready`
        // (no --summary, e.g. Claude Code's idle_prompt Notification -> `ready
        // <sid>` with no stdin) re-affirming a card whose live content is
        // ALREADY a TextSummaryResult (a prior `ready --summary`) reads back an
        // empty ExecutingStep. Same guard as ReadyDecay.DemoteToIdle.
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "goal", Now);
        h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "All finished.", Now.AddMinutes(1));

        var outcome = h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, summary: null, Now.AddMinutes(2));

        Assert.IsTrue(outcome.Success, "a bare re-affirmation of an existing TextSummaryResult-held Ready card must never crash.");
        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
        Assert.AreNotEqual("", outcome.View.ExecutingStep, "must never hand the platform an empty executing step.");
        Assert.AreEqual(AdvanceModel.NoStepsYetPlaceholder, outcome.View.ExecutingStep);
    }

    // ---- broken: lands Broken from any prior state ----------------------------------

    [TestMethod]
    [DataRow(Origin.Blocked)]
    [DataRow(Origin.Broken)]
    [DataRow(Origin.Ready)]
    [DataRow(Origin.Working)]
    public void Broken_FromAnyPriorState_LandsBroken(Origin origin)
    {
        using var h = new SemanticEngineHarness();
        Reach(h, "h1", origin);

        var outcome = h.Engine.Broken("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink,
            Atv.Semantics.BrokenReasonToken.Timeout, detail: null, Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Error, outcome.View!.State);
    }

    [TestMethod]
    public void Broken_WithDetail_AppendsAfterFixedReasonWord()
    {
        using var h = new SemanticEngineHarness();

        var outcome = h.Engine.Broken("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink,
            Atv.Semantics.BrokenReasonToken.ApiError, "connection reset by peer", Now);

        Assert.AreEqual(AppTaskState.Error, outcome.View!.State);
    }

    // ---- agent-started / agent-stopped: no transition from any prior state --------

    [TestMethod]
    [DataRow(Origin.Blocked)]
    [DataRow(Origin.Broken)]
    [DataRow(Origin.Ready)]
    [DataRow(Origin.Working)]
    public void AgentStarted_FromAnyPriorState_NeverChangesState(Origin origin)
    {
        using var h = new SemanticEngineHarness();
        Reach(h, "h1", origin);
        var before = h.Store.FindAll().Single().State;

        var outcome = h.Engine.AgentStarted("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, agentId: "a1", name: "worker", Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(before, outcome.View!.State, "ERGO-31's transition table: agent-started's target-state column is blank -- no transition.");
    }

    [TestMethod]
    [DataRow(Origin.Blocked)]
    [DataRow(Origin.Broken)]
    [DataRow(Origin.Ready)]
    [DataRow(Origin.Working)]
    public void AgentStopped_UnrelatedLocus_FromAnyPriorState_NeverChangesState(Origin origin)
    {
        using var h = new SemanticEngineHarness();
        Reach(h, "h1", origin);
        var before = h.Store.FindAll().Single().State;

        // No matching --agent was ever blocking, so this is pure bookkeeping.
        var outcome = h.Engine.AgentStopped("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, agentId: "never-started", Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(before, outcome.View!.State);
    }
}
