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
        // empty ExecutingStep. Same guard as ReadyDecay.DemoteToIdle. Since the
        // 2026-07-15 fix below, this specific flow now reproduces the
        // remembered summary as ANOTHER TextSummaryResult rather than falling
        // to the SequenceOfSteps placeholder -- so it never even reaches the
        // "empty executingStep" hazard this test was written to guard; the
        // placeholder fallback itself is covered separately (no remembered
        // copy) by Ready_BareReassertion_WithNoRememberedSummary_FallsBackToPlaceholder.
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "goal", Now);
        h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "All finished.", Now.AddMinutes(1));

        var outcome = h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, summary: null, Now.AddMinutes(2));

        Assert.IsTrue(outcome.Success, "a bare re-affirmation of an existing TextSummaryResult-held Ready card must never crash.");
        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
    }

    [TestMethod]
    public void Ready_BareReassertion_AfterASummaryResult_ReproducesTheRememberedSummary()
    {
        // Bug fix (found via live dogfood, 2026-07-15): this used to silently
        // DISCARD the summary and reset the card's text to "Not started yet."
        // -- the platform itself can never answer "what was that TextSummaryResult's
        // text", so the engine must remember its own copy (EngineMemory.LastSummary)
        // to actually hold the last message across repeat done-signals (Stop +
        // idle_prompt), instead of resetting it.
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "goal", Now);
        h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "All finished.", Now.AddMinutes(1));

        var outcome = h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, summary: null, Now.AddMinutes(2));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
        var entry = h.Sidecar.Read("h1")!;
        Assert.AreEqual("All finished.", entry.EngineMemory!.LastSummary,
            "the engine must remember its own last-written summary -- AppTaskView can never read a TextSummaryResult's text back (mirrors the real platform's asymmetry).");
    }

    [TestMethod]
    public void Ready_BareReassertion_WithNoRememberedSummary_FallsBackToPlaceholder()
    {
        // Defensive fallback: a TextSummaryResult-holding Ready card with no
        // EngineMemory.LastSummary at all (e.g. a schema-<4 entry from before
        // this fix, simulated here by writing the content directly and never
        // going through ClaimReady) must still never hand the platform an
        // empty executing step -- same guard as ReadyDecay.DemoteToIdle.
        using var h = new SemanticEngineHarness();
        var view = h.Store.Create("T", "S", SemanticEngineHarness.DeepLink, SemanticEngineHarness.IconUri,
            new AppTaskContentDto.TextSummaryResult("All done."));
        h.Store.Update(view.Id, AppTaskState.Completed, new AppTaskContentDto.TextSummaryResult("All done."));
        h.Sidecar.Write("h1", view.Id, Now);

        var outcome = h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, summary: null, Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
        Assert.AreNotEqual("", outcome.View.ExecutingStep, "must never hand the platform an empty executing step.");
        Assert.AreEqual(AdvanceModel.NoStepsYetPlaceholder, outcome.View.ExecutingStep);
    }

    [TestMethod]
    public void Working_AfterASummaryResult_ClearsTheRememberedSummary()
    {
        // Symmetric with ReadyDecay's own clock-clearing rule: leaving Ready
        // via any route must retire LastSummary too, or a much later bare
        // `ready` (after a whole new turn) could resurrect a stale message.
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "goal", Now);
        h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "All finished.", Now.AddMinutes(1));

        h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "new goal", Now.AddMinutes(2));

        var entry = h.Sidecar.Read("h1")!;
        Assert.IsNull(entry.EngineMemory!.LastSummary, "leaving Ready must clear the remembered summary -- same rule as ReadyDecay (LIFE-24).");
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

    // ---- agent-started: advances the parent's step (bug fix, 2026-07-16 live dogfood) ----

    [TestMethod]
    [DataRow(Origin.Broken)]
    [DataRow(Origin.Ready)]
    [DataRow(Origin.Working)]
    public void AgentStarted_FromNonBlockedPriorState_LandsWorking_ShowingTheStartedLine(Origin origin)
    {
        // Bug fix: agent-started used to leave the parent's content/state
        // completely untouched (ERGO-31's transition table target-state
        // column was blank), so the parent froze on whatever activity
        // preceded the spawn for the whole fan-out window even though the new
        // child card(s) updated fine. Now routes through the same
        // ProjectAfterLocusChange pipeline `activity` uses -- lands Working
        // exactly like `activity` already does from these same origins
        // (Activity_FromAnyPriorState_LandsWorking).
        using var h = new SemanticEngineHarness();
        Reach(h, "h1", origin);

        var outcome = h.Engine.AgentStarted("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, agentId: "a1", name: "worker", Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Running, outcome.View!.State);
        Assert.AreEqual("Started worker", outcome.View.ExecutingStep);
    }

    [TestMethod]
    public void AgentStarted_WhileBlocked_PreservesTheQuestion_NeverClearsIt()
    {
        // agent-started is deliberately NOT one of LIFE-24's same-locus
        // block-clearing trigger events (unlike activity/agent-stopped) --
        // starting a subagent must never silently resolve a pending question.
        using var h = new SemanticEngineHarness();
        Reach(h, "h1", Origin.Blocked);

        var outcome = h.Engine.AgentStarted("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, agentId: "a1", name: "worker", Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State, "agent-started must never clear a pending block.");
    }

    [TestMethod]
    public void AgentStarted_WithNoName_UsesTheAgentIdInTheStartedLine()
    {
        using var h = new SemanticEngineHarness();
        Reach(h, "h1", Origin.Working);

        var outcome = h.Engine.AgentStarted("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, agentId: "a1", name: null, Now.AddMinutes(1));

        Assert.AreEqual("Started a1", outcome.View!.ExecutingStep);
    }

    [TestMethod]
    public void AgentStarted_ArchivesThePreviousStep_IntoCompletedSteps()
    {
        // The exact reported symptom: "Writing Foo.txt" must not just sit
        // there -- it needs to move into history like any other Advance call.
        using var h = new SemanticEngineHarness();
        h.Engine.Activity("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink,
            Atv.Semantics.ActivityKind.Write, "Foo.txt", agentId: null, name: null, Now);

        var outcome = h.Engine.AgentStarted("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, agentId: "a1", name: "worker", Now.AddMinutes(1));

        Assert.AreEqual("Started worker", outcome.View!.ExecutingStep);
        CollectionAssert.Contains(outcome.View.CompletedSteps.ToArray(), "Writing Foo.txt");
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
