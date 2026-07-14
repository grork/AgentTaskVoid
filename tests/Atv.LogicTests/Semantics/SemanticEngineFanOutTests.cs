using Atv.Operations;
using Atv.Semantics;
using Atv.Store;

namespace Atv.LogicTests.Semantics;

/// <summary>
/// AC6's fan-out addressing coverage (ERGO-31 §5): the 2nd-concurrent
/// <c>agent-started</c> mints REAL child cards (including a retroactive mint
/// for the 1st worker), the deterministic <c>&lt;session&gt;#&lt;agent_id&gt;</c>
/// handle format, byte-identical parent icon reuse, retirement at
/// <c>agent-stopped</c>, cascade on parent <c>remove</c>/<c>session-ended</c>,
/// name-only degraded resolution (no card), and the structural refusal of
/// <c>blocked</c> against a child handle.
/// </summary>
[TestClass]
public sealed class SemanticEngineFanOutTests
{
    private static readonly Uri Icon = SemanticEngineHarness.IconUri;
    private static readonly Uri Link = SemanticEngineHarness.DeepLink;
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    private static AppTaskView? FindChild(SemanticEngineHarness h, string childHandle)
    {
        var entry = h.Sidecar.Read(childHandle);
        return entry is null ? null : h.Store.Find(entry.Id);
    }

    // ---- mint at the 2nd concurrent start, retroactive 1st ---------------------

    [TestMethod]
    public void SecondConcurrentAgentStarted_MintsBothChildCards_RetroactivelyCardingThe1st()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);

        var first = h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: "Worker One", Now.AddMinutes(1));
        Assert.IsNull(FindChild(h, "session#a1"), "the 1st worker alone must not yet have a card.");

        var second = h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: "Worker Two", Now.AddMinutes(2));

        var childA1 = FindChild(h, "session#a1");
        var childA2 = FindChild(h, "session#a2");
        Assert.IsNotNull(childA1, "the 2nd concurrent start must retroactively card the 1st worker too.");
        Assert.IsNotNull(childA2, "and mint the 2nd worker's own card.");
        Assert.IsTrue(first.Success);
        Assert.IsTrue(second.Success);
    }

    [TestMethod]
    public void ChildHandleFormat_IsExactlyParentHashAgentId()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("my-session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("my-session", "T", "S", Icon, Link, agentId: "worker-7", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("my-session", "T", "S", Icon, Link, agentId: "worker-8", name: null, Now.AddMinutes(2));

        Assert.IsNotNull(h.Sidecar.Read("my-session#worker-7"));
        Assert.IsNotNull(h.Sidecar.Read("my-session#worker-8"));
    }

    [TestMethod]
    public void ChildCard_IconUri_IsByteIdenticalToTheParentsCall_NeverReMintedViaIconService()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));

        var parent = h.Store.FindAll().Single(t => t.Title == "T" && h.Sidecar.Read("session")!.Id == t.Id);
        var childA1 = FindChild(h, "session#a1")!;
        var childA2 = FindChild(h, "session#a2")!;

        Assert.AreEqual(Icon, parent.IconUri);
        Assert.AreEqual(Icon, childA1.IconUri, "the child must reuse the parent's OWN resolved icon URI byte-for-byte.");
        Assert.AreEqual(Icon, childA2.IconUri);
    }

    [TestMethod]
    public void ChildCard_StartsWorking_WithNameAsTitle_FallsBackToAgentIdWhenNoName()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: "Worker One", Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));

        var childA1 = FindChild(h, "session#a1")!;
        var childA2 = FindChild(h, "session#a2")!;

        Assert.AreEqual(AppTaskState.Running, childA1.State);
        Assert.AreEqual("Worker One", childA1.Title, "the retroactively-carded 1st worker must still get its ORIGINAL --name via the persisted name hint.");
        Assert.AreEqual(AppTaskState.Running, childA2.State);
        Assert.AreEqual("a2", childA2.Title, "no --name was ever given for a2 -- falls back to the bare agent id.");
    }

    [TestMethod]
    public void ThirdConcurrentStart_AfterFanOutAlreadyEstablished_MintsJustItsOwnCard()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));

        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a3", name: null, Now.AddMinutes(3));

        Assert.IsNotNull(FindChild(h, "session#a3"));
    }

    [TestMethod]
    public void ReassertingAgentStarted_SameLocus_NeverDoubleMints()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));
        var childBefore = FindChild(h, "session#a1")!;

        // Re-registering a1 (idempotent claim) must not re-mint / re-create its card.
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(3));

        var childAfter = FindChild(h, "session#a1")!;
        Assert.AreEqual(childBefore.Id, childAfter.Id, "the SAME live card -- no Remove+Create churn from a re-assert.");
    }

    // ---- name-only registration: degraded resolution, no card ------------------

    [TestMethod]
    public void NameOnlyRegistration_NoAgentId_MintsNoChildCard_EvenWithConcurrency()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);

        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: null, name: "Some worker", Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: null, name: "Another worker", Now.AddMinutes(2));

        // Neither call carried an agent id, so there is no locus to card at all --
        // confirm the store never grew beyond the parent card.
        Assert.HasCount(1, h.Store.FindAll(), "no child card minted -- degraded resolution renders as a parent activity line instead (translator's job, not this verb's).");
    }

    // ---- retirement at agent-stopped -------------------------------------------

    [TestMethod]
    public void AgentStopped_ForACardedLocus_RetiresItsChildCard()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));
        Assert.IsNotNull(FindChild(h, "session#a1"));

        h.Engine.AgentStopped("session", "T", "S", Icon, Link, agentId: "a1", Now.AddMinutes(3));

        Assert.IsNull(FindChild(h, "session#a1"), "the child's card+sidecar entry must be gone.");
        Assert.IsNotNull(FindChild(h, "session#a2"), "the sibling child must be UNAFFECTED.");
    }

    [TestMethod]
    public void AgentStopped_SoloLocus_NeverCarded_NothingToRetire_PureBookkeeping()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "solo", name: null, Now.AddMinutes(1));

        var outcome = h.Engine.AgentStopped("session", "T", "S", Icon, Link, agentId: "solo", Now.AddMinutes(2));

        Assert.IsTrue(outcome.Success);
        Assert.HasCount(1, h.Store.FindAll(), "a solo locus (never concurrent) has no card to retire.");
    }

    [TestMethod]
    public void AStoppedThenRestartedAgentId_CanBeCardedAgain_IfConcurrencyReturns()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));
        h.Engine.AgentStopped("session", "T", "S", Icon, Link, agentId: "a1", Now.AddMinutes(3));
        h.Engine.AgentStopped("session", "T", "S", Icon, Link, agentId: "a2", Now.AddMinutes(4));
        Assert.IsNull(FindChild(h, "session#a1"));
        Assert.IsNull(FindChild(h, "session#a2"));

        // A fresh pair of concurrent workers, re-using one of the earlier ids.
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(5));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a3", name: null, Now.AddMinutes(6));

        Assert.IsNotNull(FindChild(h, "session#a1"), "a1 retired cleanly and can be carded again on a later concurrent burst.");
        Assert.IsNotNull(FindChild(h, "session#a3"));
    }

    // ---- cascade on parent remove / session-ended -------------------------------

    [TestMethod]
    public void RemoveParent_CascadesToEveryLiveChild()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));
        Assert.IsNotNull(FindChild(h, "session#a1"));
        Assert.IsNotNull(FindChild(h, "session#a2"));

        var outcome = h.Ops.Remove("session", Now.AddMinutes(3));

        Assert.AreEqual(OutcomeKind.Removed, outcome.Kind);
        Assert.IsNull(h.Sidecar.Read("session"));
        Assert.IsNull(FindChild(h, "session#a1"));
        Assert.IsNull(FindChild(h, "session#a2"));
        Assert.IsEmpty(h.Store.FindAll(), "parent + both children must all be gone.");
    }

    [TestMethod]
    public void SessionEndedFinished_CascadesToEveryLiveChild()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));

        var outcome = h.Engine.SessionEnded("session", SessionEndedReasonToken.Finished, Now.AddMinutes(3));

        Assert.AreEqual(OutcomeKind.Removed, outcome.Kind);
        Assert.IsNull(FindChild(h, "session#a1"));
        Assert.IsNull(FindChild(h, "session#a2"));
        Assert.IsEmpty(h.Store.FindAll());
    }

    [TestMethod]
    public void SessionEndedError_CascadesToEveryLiveChild_ParentBecomesBroken()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));

        var outcome = h.Engine.SessionEnded("session", SessionEndedReasonToken.Error, Now.AddMinutes(3));

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Error, outcome.View!.State);
        Assert.IsNull(FindChild(h, "session#a1"), "the session is over either way -- children cascade away even on the error path.");
        Assert.IsNull(FindChild(h, "session#a2"));
        Assert.HasCount(1, h.Store.FindAll(), "only the parent (now Broken) remains.");
    }

    [TestMethod]
    public void RemoveChildDirectly_TargetsExactlyThatOneChild_ParentAndSiblingUntouched()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));

        var outcome = h.Ops.Remove("session#a1", Now.AddMinutes(3));

        Assert.AreEqual(OutcomeKind.Removed, outcome.Kind);
        Assert.IsNull(FindChild(h, "session#a1"));
        Assert.IsNotNull(h.Sidecar.Read("session"), "the parent must be UNTOUCHED by a direct child remove.");
        Assert.IsNotNull(FindChild(h, "session#a2"), "the sibling must be UNTOUCHED.");
    }

    // ---- children are scaffolding-only: blocked structurally refused -----------

    [TestMethod]
    public void Blocked_AgainstAChildHandle_IsStructurallyRefused()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));

        var outcome = h.Engine.Blocked("session#a1", "T", "S", Icon, Link, "Continue?", agentId: null, Now.AddMinutes(3));

        Assert.AreEqual(OutcomeKind.RefusedInvalidArgument, outcome.Kind);
        Assert.AreEqual(AppTaskState.Running, FindChild(h, "session#a1")!.State, "the refusal must be a true no-op -- the child stays exactly as it was.");
    }

    [TestMethod]
    public void Blocked_AgainstAnOrdinaryHandleContainingHash_IsNotTreatedAsAChild()
    {
        // Only a REAL minted child (EngineMemory.ParentHandle set) is refused --
        // an ordinary handle that merely happens to contain '#' is unaffected
        // (no string-pattern guessing, ParentHandle is the only signal).
        using var h = new SemanticEngineHarness();

        var outcome = h.Engine.Blocked("not-a-child#weird", "T", "S", Icon, Link, "Continue?", agentId: null, Now);

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State);
    }

    // ---- children are scaffolding-only: the rule is EXHAUSTIVE (Working/Completed
    // only) -- broken and session-ended --reason error must be refused too, not just
    // blocked. A child that reached Error would be a third state beyond the two
    // sanctioned ones. ----------------------------------------------------------

    [TestMethod]
    public void Broken_AgainstAChildHandle_IsStructurallyRefused()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));

        var outcome = h.Engine.Broken("session#a1", "T", "S", Icon, Link, BrokenReasonToken.Timeout, detail: null, Now.AddMinutes(3));

        Assert.AreEqual(OutcomeKind.RefusedInvalidArgument, outcome.Kind);
        Assert.AreEqual(AppTaskState.Running, FindChild(h, "session#a1")!.State, "the refusal must be a true no-op -- the child stays exactly as it was, never reaching Error.");
    }

    [TestMethod]
    public void SessionEndedError_AgainstAChildHandle_IsStructurallyRefused()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "a2", name: null, Now.AddMinutes(2));

        var outcome = h.Engine.SessionEnded("session#a1", SessionEndedReasonToken.Error, Now.AddMinutes(3));

        Assert.AreEqual(OutcomeKind.RefusedInvalidArgument, outcome.Kind);
        Assert.IsNotNull(FindChild(h, "session#a1"), "the child card must still exist -- refused, not removed.");
        Assert.AreEqual(AppTaskState.Running, FindChild(h, "session#a1")!.State, "the refusal must be a true no-op -- the child stays exactly as it was, never reaching Error/Broken.");
    }
}
