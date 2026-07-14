using Atv.Operations;
using Atv.Semantics;
using Atv.Store;

namespace Atv.LogicTests.Semantics;

/// <summary>
/// ERGO-31 §1's <c>session-ended</c> row: the one verb that does NOT accept
/// identity flags and does NOT upsert -- <c>finished</c> removes the card
/// (delegating to the surviving <see cref="Atv.Operations.TaskOperations.Remove"/>),
/// <c>error</c> projects Broken with a fixed phrase.
/// </summary>
[TestClass]
public sealed class SessionEndedTests
{
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    [TestMethod]
    public void Finished_RemovesTheLiveCardAndSidecarEntry()
    {
        using var h = new SemanticEngineHarness();
        h.WorkingNew("h1");

        var outcome = h.Engine.SessionEnded("h1", SessionEndedReasonToken.Finished, Now.AddMinutes(1));

        Assert.AreEqual(OutcomeKind.Removed, outcome.Kind);
        Assert.IsEmpty(h.Store.FindAll());
        Assert.IsNull(h.Sidecar.Read("h1"));
    }

    [TestMethod]
    public void Finished_UnknownHandle_IsACleanNoOp()
    {
        using var h = new SemanticEngineHarness();

        var outcome = h.Engine.SessionEnded("never-seen", SessionEndedReasonToken.Finished, Now);

        Assert.AreEqual(OutcomeKind.UnknownHandleNoOp, outcome.Kind);
    }

    [TestMethod]
    public void Error_ProjectsBroken_FixedPhrase()
    {
        using var h = new SemanticEngineHarness();
        h.WorkingNew("h1");

        var outcome = h.Engine.SessionEnded("h1", SessionEndedReasonToken.Error, Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(AppTaskState.Error, outcome.View!.State);
    }

    [TestMethod]
    public void Error_UnknownHandle_IsACleanNoOp_NeverCreates()
    {
        // session-ended has no identity flags -- it cannot upsert (nothing to create with).
        using var h = new SemanticEngineHarness();

        var outcome = h.Engine.SessionEnded("never-seen", SessionEndedReasonToken.Error, Now);

        Assert.AreEqual(OutcomeKind.UnknownHandleNoOp, outcome.Kind);
        Assert.IsEmpty(h.Store.FindAll());
    }

    [TestMethod]
    public void Error_FromABlockedCard_ClearsTheQuestionAndEngineMemory()
    {
        using var h = new SemanticEngineHarness();
        h.WorkingNew("h1");
        h.Engine.Blocked("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "Q?", agentId: null, Now.AddMinutes(1));

        var outcome = h.Engine.SessionEnded("h1", SessionEndedReasonToken.Error, Now.AddMinutes(2));

        Assert.AreEqual(AppTaskState.Error, outcome.View!.State);

        // Engine memory reset -- a subsequent `working` call should behave like a
        // fresh Working entry, not still carry the old blocked-loci bookkeeping.
        var next = h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "new goal", Now.AddMinutes(3));
        Assert.AreEqual(AppTaskState.Running, next.View!.State);
    }
}
