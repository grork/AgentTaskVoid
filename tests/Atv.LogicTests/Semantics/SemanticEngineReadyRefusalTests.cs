using Atv.Operations;
using Atv.Semantics;
using Atv.Store;

namespace Atv.LogicTests.Semantics;

/// <summary>
/// Phase 19 Part C (found live during AC11's own dogfood, 2026-07-15): Claude
/// Code's own <c>Stop</c> fires as soon as it dispatches Task-tool subagent
/// calls, without waiting for them to finish -- <c>translate.ps1</c>'s
/// unconditional <c>Stop -&gt; ready &lt;sid&gt; --summary -</c> mapping then
/// claims the PARENT card into Completed while children are still
/// demonstrably running. <see cref="SemanticEngine.Ready"/> now structurally
/// refuses the Completed transition while the addressed handle's own
/// <see cref="Atv.Persistence.EngineMemory.ActiveAgentLoci"/> (the FULL active
/// set, not <see cref="Atv.Persistence.EngineMemory.CardedAgentLoci"/> -- a
/// lone, not-yet-carded subagent's activity is designed to land on the
/// parent per Part A decision point 4, so the parent legitimately still has
/// real outstanding work below the 2-concurrent carding threshold too) is
/// non-empty -- a true no-op refusal, same shape as the existing
/// <c>refuseIfChild</c> structural refusal used by <c>Blocked</c>/<c>Broken</c>.
/// </summary>
[TestClass]
public sealed class SemanticEngineReadyRefusalTests
{
    private static readonly Uri Icon = SemanticEngineHarness.IconUri;
    private static readonly Uri Link = SemanticEngineHarness.DeepLink;
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    private static AppTaskView Parent(SemanticEngineHarness h, string handle)
        => h.Store.Find(h.Sidecar.Read(handle)!.Id)!;

    /// <summary>Working, then two concurrent `agent-started` calls -- carding both a1/a2 (the 2nd-concurrent-start mint, retroactively carding the 1st too).</summary>
    private static void CardTwoAgents(SemanticEngineHarness h, string session, string a1 = "a1", string a2 = "a2")
    {
        h.Engine.Working(session, "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted(session, "T", "S", Icon, Link, agentId: a1, name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted(session, "T", "S", Icon, Link, agentId: a2, name: null, Now.AddMinutes(2));
    }

    // ---- AC12: refusal while active ---------------------------------------------

    [TestMethod]
    public void Ready_Bare_RefusedWhileASingleUncardedWorkerIsActive()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "solo", name: null, Now.AddMinutes(1)); // never carded (needs a 2nd concurrent worker).
        var before = Parent(h, "session");

        var outcome = h.Engine.Ready("session", "T", "S", Icon, Link, summary: null, Now.AddMinutes(2));

        Assert.AreEqual(OutcomeKind.RefusedInvalidArgument, outcome.Kind);
        var after = Parent(h, "session");
        Assert.AreEqual(before.State, after.State, "a true no-op -- state byte-unchanged.");
        Assert.AreEqual(before.ExecutingStep, after.ExecutingStep, "a true no-op -- content byte-unchanged.");
        CollectionAssert.AreEqual(before.CompletedSteps.ToArray(), after.CompletedSteps.ToArray());
    }

    [TestMethod]
    public void Ready_WithSummary_RefusedWhileASingleUncardedWorkerIsActive()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "solo", name: null, Now.AddMinutes(1));
        var before = Parent(h, "session");

        var outcome = h.Engine.Ready("session", "T", "S", Icon, Link, summary: "Premature turn summary.", Now.AddMinutes(2));

        Assert.AreEqual(OutcomeKind.RefusedInvalidArgument, outcome.Kind);
        var after = Parent(h, "session");
        Assert.AreEqual(before.State, after.State);
        Assert.AreEqual(before.ExecutingStep, after.ExecutingStep);
    }

    [TestMethod]
    public void Ready_Bare_RefusedDuringATwoAgentCardedFanOut()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");
        var before = Parent(h, "session");

        var outcome = h.Engine.Ready("session", "T", "S", Icon, Link, summary: null, Now.AddMinutes(3));

        Assert.AreEqual(OutcomeKind.RefusedInvalidArgument, outcome.Kind);
        var after = Parent(h, "session");
        Assert.AreEqual(before.State, after.State);
        Assert.AreEqual(before.ExecutingStep, after.ExecutingStep);
    }

    [TestMethod]
    public void Ready_WithSummary_RefusedDuringATwoAgentCardedFanOut()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");
        var before = Parent(h, "session");

        var outcome = h.Engine.Ready("session", "T", "S", Icon, Link, summary: "Premature turn summary.", Now.AddMinutes(3));

        Assert.AreEqual(OutcomeKind.RefusedInvalidArgument, outcome.Kind);
        var after = Parent(h, "session");
        Assert.AreEqual(before.State, after.State);
        Assert.AreEqual(before.ExecutingStep, after.ExecutingStep);
    }

    // ---- AC13: recovery once clear ------------------------------------------------

    [TestMethod]
    public void Ready_SucceedsOnceEveryActiveAgentHasStopped()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");
        h.Engine.AgentStopped("session", "T", "S", Icon, Link, agentId: "a1", Now.AddMinutes(3));
        h.Engine.AgentStopped("session", "T", "S", Icon, Link, agentId: "a2", Now.AddMinutes(4));

        var outcome = h.Engine.Ready("session", "T", "S", Icon, Link, summary: "All done.", Now.AddMinutes(5));

        // A TextSummaryResult's text is unreadable back off AppTaskView
        // (INFRA-15) -- only State/Kind are observable here.
        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Completed, Parent(h, "session").State);
    }

    [TestMethod]
    public void Ready_Bare_SucceedsOnceEveryActiveAgentHasStopped()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "solo", name: null, Now.AddMinutes(1));
        h.Engine.AgentStopped("session", "T", "S", Icon, Link, agentId: "solo", Now.AddMinutes(2));

        var outcome = h.Engine.Ready("session", "T", "S", Icon, Link, summary: null, Now.AddMinutes(3));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Completed, Parent(h, "session").State);
    }

    // ---- AC14: the single-worker (never-carded) regression, explicitly ------------

    [TestMethod]
    public void Ready_GatedOnActiveAgentLoci_NotCardedAgentLoci_SingleUncardedWorkerStillRefuses()
    {
        // The exact live-dogfood scenario: one lone subagent, never carded
        // (concurrency never reached 2), still active when `ready` arrives.
        // CardedAgentLoci is empty here by construction -- the refusal must
        // be gated on ActiveAgentLoci, not CardedAgentLoci, or this case
        // would slip through.
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "solo", name: null, Now.AddMinutes(1));

        var entry = h.Sidecar.Read("session")!;
        CollectionAssert.Contains(entry.EngineMemory!.ActiveAgentLoci.ToArray(), "solo", "sanity: the lone worker is active.");
        CollectionAssert.DoesNotContain(entry.EngineMemory!.CardedAgentLoci.ToArray(), "solo", "sanity: a lone worker is never carded.");

        var outcome = h.Engine.Ready("session", "T", "S", Icon, Link, summary: "Too soon.", Now.AddMinutes(2));

        Assert.AreEqual(OutcomeKind.RefusedInvalidArgument, outcome.Kind, "must be refused even though the worker was never carded.");
    }

    // ---- AC15: Broken is untouched (locking test, out of scope by decision) -------

    [TestMethod]
    public void Broken_DoesNotGainTheRefusal_StillSucceedsWithActiveChildren()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");

        var outcome = h.Engine.Broken("session", "T", "S", Icon, Link, BrokenReasonToken.Timeout, detail: null, Now.AddMinutes(3));

        Assert.IsTrue(outcome.Success, "Broken/ClaimBroken is explicitly out of scope for this refusal (operator decision, 2026-07-15) -- it must still succeed.");
        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Error, Parent(h, "session").State);
    }

    // ---- AC16: no regression --------------------------------------------------------

    [TestMethod]
    public void Ready_OrdinaryNonFanOutPath_StillSucceeds()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);

        var outcome = h.Engine.Ready("h1", "T", "S", Icon, Link, summary: "All finished.", Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Completed, Parent(h, "h1").State);
    }

    [TestMethod]
    public void Ready_AgainstAFreshNeverSeenHandle_StillSucceeds()
    {
        // No entry at all -- entry?.EngineMemory?.ActiveAgentLoci must degrade
        // to "nothing active" rather than throwing/refusing.
        using var h = new SemanticEngineHarness();

        var outcome = h.Engine.Ready("brand-new", "T", "S", Icon, Link, summary: "Done.", Now);

        Assert.IsTrue(outcome.Success);
    }

    [TestMethod]
    public void Ready_AgainstAChildHandle_StillSucceeds_ChildsOwnMemoryHasNoActiveLoci()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");

        var outcome = h.Engine.Ready("session#a1", "T", "S", Icon, Link, summary: "Reviewed foo.cs", Now.AddMinutes(3));

        Assert.IsTrue(outcome.Success, "a child's own ready call is unaffected -- its own EngineMemory carries no ActiveAgentLoci.");
        Assert.AreEqual(AppTaskState.Completed, h.Store.Find(h.Sidecar.Read("session#a1")!.Id)!.State);
    }
}
