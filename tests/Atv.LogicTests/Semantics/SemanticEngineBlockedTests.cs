using Codevoid.AgentTaskVoid.Semantics;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Semantics;

/// <summary>
/// AC2: <c>blocked</c> semantics -- the platform-enforced literal-question
/// requirement (proven at the Dispatcher layer, since the engine itself has
/// no argument-shape validation of its own -- see
/// <see cref="Cli.DispatcherSemanticVerbsTests"/>), same-locus clearing for
/// both agent-attributed and parent-locus blocks, LIFE-24's concurrent-block
/// "display latest, surface the other on clear" rule, and the degraded
/// no-attribution fallback (a parent-locus block clears on ANY parent-
/// attributed activity, which is exactly what a host that can't resolve
/// fan-out attribution produces by construction -- see the engine's own
/// type-level remarks).
/// </summary>
[TestClass]
public sealed class SemanticEngineBlockedTests
{
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;
    private static readonly Uri Icon = SemanticEngineHarness.IconUri;
    private static readonly Uri Link = SemanticEngineHarness.DeepLink;

    // ---- same-locus clearing --------------------------------------------------------

    [TestMethod]
    public void ParentLocusBlock_ClearsOnParentActivity_NoAgentFlag()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Continue?", agentId: null, Now.AddMinutes(1));
        Assert.AreEqual(AppTaskState.NeedsAttention, h.Store.FindAll().Single().State);

        var outcome = h.Engine.Activity("h1", "T", "S", Icon, Link, ActivityKind.Read, "file.txt", agentId: null, name: null, Now.AddMinutes(2));

        Assert.AreEqual(AppTaskState.Running, outcome.View!.State, "the parent-locus block must clear on the next parent-attributed activity.");
    }

    [TestMethod]
    public void AgentAttributedBlock_ClearsOnlyOnSameAgentsActivity_NotOnParentActivity()
    {
        // "worker chatter leaves a main-thread prompt standing" -- LIFE-24's S1-walk.
        // Here the inverse: a subagent's OWN block must not be cleared by unrelated
        // PARENT activity either -- attribution must match exactly.
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Worker needs permission?", agentId: "worker-1", Now.AddMinutes(1));

        // Parent-attributed activity (no --agent) must NOT clear worker-1's block.
        var afterParentActivity = h.Engine.Activity("h1", "T", "S", Icon, Link, ActivityKind.Read, "file.txt", agentId: null, name: null, Now.AddMinutes(2));
        Assert.AreEqual(AppTaskState.NeedsAttention, afterParentActivity.View!.State, "an unrelated parent-locus activity must not clear a different agent's block.");

        // The SAME agent's own activity clears it.
        var afterWorkerActivity = h.Engine.Activity("h1", "T", "S", Icon, Link, ActivityKind.Write, "out.txt", agentId: "worker-1", name: null, Now.AddMinutes(3));
        Assert.AreEqual(AppTaskState.Running, afterWorkerActivity.View!.State);
    }

    [TestMethod]
    public void MainThreadPromptStandsWhileWorkerChatters_WorkerChatterDoesNotClearParentBlock()
    {
        // LIFE-24 verbatim: "worker chatter leaves a main-thread prompt standing."
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Main thread needs input?", agentId: null, Now.AddMinutes(1));

        var afterWorkerChatter = h.Engine.Activity("h1", "T", "S", Icon, Link, ActivityKind.Read, "some-file.txt", agentId: "worker-1", name: null, Now.AddMinutes(2));

        Assert.AreEqual(AppTaskState.NeedsAttention, afterWorkerChatter.View!.State, "a subagent's own activity must not clear the parent-locus block.");
    }

    [TestMethod]
    public void AgentAttributedBlock_ClearsOnAgentStopped_SameAgent()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Permission?", agentId: "worker-1", Now.AddMinutes(1));

        var outcome = h.Engine.AgentStopped("h1", "T", "S", Icon, Link, agentId: "worker-1", Now.AddMinutes(2));

        Assert.AreEqual(AppTaskState.Running, outcome.View!.State, "LIFE-24: agent-stopped is a same-locus block-clearing trigger.");
    }

    [TestMethod]
    public void Block_ClearsOnTurnEndReady_RegardlessOfLocus()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Q?", agentId: "worker-1", Now.AddMinutes(1));

        var outcome = h.Engine.Ready("h1", "T", "S", Icon, Link, summary: null, Now.AddMinutes(2));

        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State, "a turn-end ready/broken event is never --agent-scoped -- it always clears every pending block.");
    }

    [TestMethod]
    public void Block_ClearsOnTurnEndBroken_RegardlessOfLocus()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Q?", agentId: "worker-1", Now.AddMinutes(1));

        var outcome = h.Engine.Broken("h1", "T", "S", Icon, Link, BrokenReasonToken.Fatal, detail: null, Now.AddMinutes(2));

        Assert.AreEqual(AppTaskState.Error, outcome.View!.State);
    }

    // ---- concurrent blocks: latest-then-surface-other --------------------------------

    [TestMethod]
    public void ConcurrentBlocks_DisplaysTheLatestQuestion()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Parent question?", agentId: null, Now.AddMinutes(1));

        // A second, later block on a DIFFERENT locus -- both now pending.
        var outcome = h.Engine.Blocked("h1", "T", "S", Icon, Link, "Worker question?", agentId: "worker-1", Now.AddMinutes(2));

        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State, "the card must still be Blocked with two concurrent loci pending.");
    }

    [TestMethod]
    public void ConcurrentBlocks_ClearingTheLatestLocus_SurfacesTheOtherStillBlocked()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Parent question?", agentId: null, Now.AddMinutes(1));
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Worker question?", agentId: "worker-1", Now.AddMinutes(2));

        // Clear the LATEST (worker-1)'s block -- the parent's block is still pending.
        var outcome = h.Engine.AgentStopped("h1", "T", "S", Icon, Link, agentId: "worker-1", Now.AddMinutes(3));

        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State,
            "LIFE-24: 'when its locus progresses, surface the other' -- the parent's block must still be showing, the card must NOT have re-entered Working.");
    }

    [TestMethod]
    public void ConcurrentBlocks_ClearingBothLoci_FinallyReturnsToWorking()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Parent question?", agentId: null, Now.AddMinutes(1));
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Worker question?", agentId: "worker-1", Now.AddMinutes(2));

        h.Engine.AgentStopped("h1", "T", "S", Icon, Link, agentId: "worker-1", Now.AddMinutes(3));
        var outcome = h.Engine.Activity("h1", "T", "S", Icon, Link, ActivityKind.Read, "x", agentId: null, name: null, Now.AddMinutes(4));

        Assert.AreEqual(AppTaskState.Running, outcome.View!.State, "once every pending locus has cleared, the card returns to Working.");
    }

    [TestMethod]
    public void ConcurrentBlocks_ReassertingTheOlderLocus_MakesItLatestAgain()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Parent question v1?", agentId: null, Now.AddMinutes(1));
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Worker question?", agentId: "worker-1", Now.AddMinutes(2));

        // Parent re-raises a (possibly updated) question -- now the LATEST again.
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Parent question v2?", agentId: null, Now.AddMinutes(3));

        // Clearing the parent locus now should surface worker-1's still-pending block.
        var outcome = h.Engine.Activity("h1", "T", "S", Icon, Link, ActivityKind.Read, "x", agentId: null, name: null, Now.AddMinutes(4));
        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State, "worker-1's block is still pending after the parent's (re-latest) block clears.");
    }

    // ---- degraded no-attribution fallback ---------------------------------------------

    [TestMethod]
    public void DegradedFallback_NoAttributionEitherSide_AnyActivityClears()
    {
        // A host that cannot resolve fan-out attribution at all sends every
        // `blocked`/`activity` call with no --agent -- both land on the SAME
        // parent locus by construction, so "any activity clears" falls out of
        // the same-locus rule automatically (see the engine's own remarks).
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Q?", agentId: null, Now.AddMinutes(1));

        var outcome = h.Engine.Activity("h1", "T", "S", Icon, Link, ActivityKind.Shell, "any command", agentId: null, name: null, Now.AddMinutes(2));

        Assert.AreEqual(AppTaskState.Running, outcome.View!.State);
    }

    // ---- content preserved underneath the question -----------------------------------

    [TestMethod]
    public void Blocked_PreservesTheReadableStepsUnderneathTheQuestion()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Activity("h1", "T", "S", Icon, Link, ActivityKind.Read, "file.txt", agentId: null, name: null, Now.AddMinutes(1));

        var outcome = h.Engine.Blocked("h1", "T", "S", Icon, Link, "Continue?", agentId: null, Now.AddMinutes(2));

        Assert.AreEqual("Reading file.txt", outcome.View!.ExecutingStep, "the step content underneath the question must be preserved, not wiped.");
    }
}
