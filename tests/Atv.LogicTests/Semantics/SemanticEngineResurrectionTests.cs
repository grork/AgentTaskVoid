using Codevoid.AgentTaskVoid.Operations;
using Codevoid.AgentTaskVoid.Persistence;
using Codevoid.AgentTaskVoid.Semantics;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Semantics;

/// <summary>
/// Phase-15 counterpart of phase-05's <c>TaskOperationsResurrectionTests</c>
/// -- the LIFE-15/21 miss-path resurrection, now driven through every
/// upserting <see cref="SemanticEngine"/> verb instead of the retired v1
/// update-class verbs. A tombstone record is written directly into the
/// harness's <see cref="Codevoid.AgentTaskVoid.Persistence.RecycleBin"/> (standing in for the
/// watchdog that will normally put it there at expiry).
/// </summary>
[TestClass]
public sealed class SemanticEngineResurrectionTests
{
    private static readonly Uri RecordDeepLink = new("https://example.invalid/from-record");
    private static readonly Uri RecordIconRef = new("ms-appx:///Assets/RecordIcon.png");
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    [TestMethod]
    public void Working_OnTombstonedHandle_WithinTtl_Resurrects_UsingTheCallersFields()
    {
        using var h = new SemanticEngineHarness();
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "Stored Title", "Stored Subtitle", RecordIconRef.ToString(), RecordDeepLink, Now.AddHours(-1)));

        var outcome = h.Engine.Working("h1", "Caller Title", "Caller Subtitle", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "first goal", Now);

        Assert.AreEqual(OutcomeKind.Resurrected, outcome.Kind);
        Assert.IsTrue(outcome.Success);
        // v2 semantics: EVERY upserting verb carries its own fully-resolved fields
        // (unlike v1's bare update-class verbs) -- the caller's values always win.
        Assert.AreEqual("Caller Title", outcome.View!.Title);
        Assert.AreEqual("Caller Subtitle", outcome.View.Subtitle);
        Assert.AreEqual(SemanticEngineHarness.DeepLink, outcome.View.DeepLink);
        Assert.AreEqual(SemanticEngineHarness.IconUri, outcome.View.IconUri);
        Assert.AreEqual(AppTaskState.Running, outcome.View.State);
        Assert.IsEmpty(outcome.View.CompletedSteps, "engine memory (and steps) restart fresh on resurrection.");
        Assert.AreEqual("first goal", outcome.View.ExecutingStep);

        var sidecarEntry = h.Sidecar.Read("h1");
        Assert.IsNotNull(sidecarEntry);
        Assert.AreEqual(outcome.View.Id, sidecarEntry!.Id);
        Assert.IsNull(h.RecycleBin.TryResurrect("h1", Now, SemanticEngineHarness.Ttl), "tombstone consumed");
    }

    [TestMethod]
    public void Blocked_OnTombstonedHandle_WithinTtl_Resurrects_ReachesBlocked()
    {
        using var h = new SemanticEngineHarness();
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "T", "S", null, RecordDeepLink, Now.AddMinutes(-30)));

        var outcome = h.Engine.Blocked("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "continue?", agentId: null, Now);

        Assert.AreEqual(OutcomeKind.Resurrected, outcome.Kind);
        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State);
    }

    [TestMethod]
    public void Ready_OnTombstonedHandle_WithinTtl_Resurrects_ReachesReady()
    {
        using var h = new SemanticEngineHarness();
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "T", "S", null, RecordDeepLink, Now.AddMinutes(-30)));

        var outcome = h.Engine.Ready("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, summary: null, Now);

        Assert.AreEqual(OutcomeKind.Resurrected, outcome.Kind);
        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
    }

    [TestMethod]
    public void MissingIconRef_FallsBackToTheCallersFreshIcon_NeverThePlaceholder()
    {
        // Unlike v1's update-class resurrection (which had no icon flag at all and so
        // fell back to Resurrection.FallbackIconUri), every v2 verb always carries its
        // own resolved icon -- so a missing/stale tombstone icon-ref is simply moot.
        using var h = new SemanticEngineHarness();
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "T", "S", null, RecordDeepLink, Now.AddMinutes(-30)));

        // OtherIconUri is deliberately distinct from Resurrection.FallbackIconUri's
        // literal value (unlike the harness's default IconUri, which happens to share
        // it) -- so this proves the CALLER's icon wins, not a same-string coincidence.
        var outcome = h.Engine.Working("h1", "T", "S", SemanticEngineHarness.OtherIconUri, SemanticEngineHarness.DeepLink, "x", Now);

        Assert.AreEqual(SemanticEngineHarness.OtherIconUri, outcome.View!.IconUri);
        Assert.AreNotEqual(Resurrection.FallbackIconUri, outcome.View.IconUri);
    }

    [TestMethod]
    public void PastTtl_IsACleanCreate_NotAResurrection_TombstoneLeftInPlace()
    {
        using var h = new SemanticEngineHarness(ttl: TimeSpan.FromDays(1));
        var tombstonedAt = Now.Subtract(SemanticEngineHarness.Ttl).AddMinutes(-5);
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "T", "S", null, RecordDeepLink, tombstonedAt));

        var outcome = h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "x", Now);

        // Unlike v1's update-class verbs (whose total-miss is a no-op), EVERY v2
        // verb upserts -- a past-TTL tombstone just means "not a resurrection",
        // not "refuse": a genuinely new card is still created.
        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.IsTrue(outcome.Success);
        Assert.IsNotNull(h.RecycleBin.TryResurrect("h1", Now, TimeSpan.FromDays(10)), "the past-TTL tombstone itself must be left untouched (not consumed) by a plain create -- widening the TTL window here proves the record is still on disk, unmodified.");
    }

    [TestMethod]
    public void RecycleBin_IsOnlyReadOnTheMissPath_LiveHandleNeverConsultsIt()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "x", Now);
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "Stray", "Record", null, RecordDeepLink, Now.AddMinutes(-1)));

        var outcome = h.Engine.Activity("h1", "T", "S", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, ActivityKind.Read, "y", null, null, Now.AddMinutes(1));

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind, "live handle -- ordinary accepted update, not a resurrection");
        Assert.IsNotNull(h.RecycleBin.TryResurrect("h1", Now, SemanticEngineHarness.Ttl),
            "the stray tombstone must be left untouched -- the live-handle path never consults or clears the recycle bin");
    }
}
