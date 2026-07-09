using Atv.Operations;
using Atv.Persistence;
using Atv.Store;

namespace Atv.LogicTests.Operations;

/// <summary>
/// Covers phase-05 acceptance criterion 6: the LIFE-15/21 resurrection miss
/// path. A tombstone record is written directly into the harness's
/// <see cref="RecycleBin"/> (standing in for the not-yet-built watchdog that
/// will normally put it there at expiry) -- this phase only consumes the
/// record, never produces one.
/// </summary>
[TestClass]
public sealed class TaskOperationsResurrectionTests
{
    private static readonly Uri RecordDeepLink = new("https://example.invalid/from-record");
    private static readonly Uri RecordIconRef = new("ms-appx:///Assets/RecordIcon.png");

    [TestMethod]
    public void Step_OnTombstonedHandle_WithinTtl_Resurrects_WithStoredCoreInfo_AndAppliesTheUpdate()
    {
        using var h = new OperationsHarness();
        var tombstonedAt = OperationsHarness.Now.AddHours(-1);
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "Stored Title", "Stored Subtitle", RecordIconRef.ToString(), RecordDeepLink, tombstonedAt));

        var outcome = h.Ops.Step("h1", "first step after resurrection", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Resurrected, outcome.Kind);
        Assert.IsTrue(outcome.Success);
        Assert.AreEqual("Stored Title", outcome.View!.Title);
        Assert.AreEqual("Stored Subtitle", outcome.View.Subtitle);
        Assert.AreEqual(RecordDeepLink, outcome.View.DeepLink);
        Assert.AreEqual(RecordIconRef, outcome.View.IconUri);
        Assert.AreEqual(AppTaskState.Running, outcome.View.State, "steps/state restart fresh -- Running, not whatever the pre-expiry card had");
        Assert.IsEmpty(outcome.View.CompletedSteps, "nothing mutable was stored -- steps restart fresh");
        Assert.AreEqual("first step after resurrection", outcome.View.ExecutingStep);

        // The entry now lives in the sidecar under the SAME handle, mapped to the NEW id.
        var sidecarEntry = h.Sidecar.Read("h1");
        Assert.IsNotNull(sidecarEntry);
        Assert.AreEqual(outcome.View.Id, sidecarEntry!.Id);

        // The tombstone is consumed -- moved back to live, not left behind.
        Assert.IsNull(h.RecycleBin.TryResurrect("h1", OperationsHarness.Now, OperationsHarness.Ttl));
    }

    [TestMethod]
    public void Attention_OnTombstonedHandle_WithinTtl_Resurrects_ReachesNeedsAttention()
    {
        using var h = new OperationsHarness();
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "T", "S", null, RecordDeepLink, OperationsHarness.Now.AddMinutes(-30)));

        var outcome = h.Ops.Attention("h1", "continue?", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Resurrected, outcome.Kind);
        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State);
    }

    [TestMethod]
    public void State_OnTombstonedHandle_WithinTtl_Resurrects_AppliesRequestedState()
    {
        using var h = new OperationsHarness();
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "T", "S", null, RecordDeepLink, OperationsHarness.Now.AddMinutes(-30)));

        var outcome = h.Ops.SetState("h1", AppTaskState.Paused, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Resurrected, outcome.Kind);
        Assert.AreEqual(AppTaskState.Paused, outcome.View!.State);
    }

    [TestMethod]
    public void MissingIconRef_FallsBackToThePlaceholderIcon()
    {
        using var h = new OperationsHarness();
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "T", "S", null, RecordDeepLink, OperationsHarness.Now.AddMinutes(-30)));

        var outcome = h.Ops.Step("h1", "x", OperationsHarness.Now);

        Assert.AreEqual(Resurrection.FallbackIconUri, outcome.View!.IconUri);
    }

    [TestMethod]
    public void PastTtl_IsACleanNoOp_NoStoreWrite_TombstoneLeftInPlace()
    {
        using var h = new OperationsHarness(ttl: TimeSpan.FromDays(1));
        var tombstonedAt = OperationsHarness.Now.Subtract(OperationsHarness.Ttl).AddMinutes(-5);
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "T", "S", null, RecordDeepLink, tombstonedAt));

        var outcome = h.Ops.Step("h1", "x", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.UnknownHandleNoOp, outcome.Kind);
        Assert.IsFalse(outcome.Success);
        Assert.IsEmpty(h.Store.FindAll(), "no store write for a past-TTL resurrection attempt");
        Assert.IsNull(h.Sidecar.Read("h1"));
    }

    [TestMethod]
    public void NeverSeenHandle_IsACleanNoOp_NoStoreWrite()
    {
        using var h = new OperationsHarness();

        var outcome = h.Ops.Step("totally-unknown", "x", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.UnknownHandleNoOp, outcome.Kind);
        Assert.IsEmpty(h.Store.FindAll());
        Assert.IsNull(h.Sidecar.Read("totally-unknown"));
    }

    [TestMethod]
    public void RecycleBin_IsOnlyReadOnTheMissPath_LiveHandleNeverConsultsIt()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        // Contrived: a stray tombstone happens to exist under the SAME handle a live
        // task is already using. A correct implementation never looks at it, because
        // Reconciler.ResolveHandle already found a live entry (Kept) -- the miss path
        // is never entered.
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "Stray", "Record", null, RecordDeepLink, OperationsHarness.Now.AddMinutes(-1)));

        var outcome = h.Ops.Step("h1", "step one", OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind, "live handle -- ordinary accepted update, not a resurrection");
        Assert.IsNotNull(h.RecycleBin.TryResurrect("h1", OperationsHarness.Now, OperationsHarness.Ttl),
            "the stray tombstone must be left untouched -- the live-handle path never consults or clears the recycle bin");
    }

    [TestMethod]
    public void Start_OnTombstonedHandle_WithinTtl_Resurrects_UsingTheFieldsStartCarries()
    {
        // ERGO-25's recycle-bin caveat: a resurrecting `start` yields "restored core
        // info + whatever start carries" -- and start always carries fully-resolved
        // title/subtitle/icon/deepLink by the time it reaches this layer, so those
        // values win over the stored record's.
        using var h = new OperationsHarness();
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "Stored Title", "Stored Subtitle", RecordIconRef.ToString(), RecordDeepLink, OperationsHarness.Now.AddMinutes(-30)));

        var outcome = h.Ops.Start("h1", "Caller Title", "Caller Subtitle", OperationsHarness.IconUri, OperationsHarness.DeepLink, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Resurrected, outcome.Kind);
        Assert.AreEqual("Caller Title", outcome.View!.Title);
        Assert.AreEqual("Caller Subtitle", outcome.View.Subtitle);
        Assert.AreEqual(OperationsHarness.DeepLink, outcome.View.DeepLink);
        Assert.AreEqual(OperationsHarness.IconUri, outcome.View.IconUri);
        Assert.IsEmpty(outcome.View.CompletedSteps, "fresh step sequence on a resurrecting start");
        Assert.IsNull(h.RecycleBin.TryResurrect("h1", OperationsHarness.Now, OperationsHarness.Ttl), "tombstone consumed");
    }

    [TestMethod]
    public void Start_OnAGenuinelyNewHandle_NeverSeenAnywhere_StillCreates_NotANoOp()
    {
        // start's own primary purpose (create-or-adopt, ERGO-27) -- a handle absent
        // from BOTH the live sidecar and the recycle bin must still succeed as a
        // plain create; only the update-class verbs (step/state/done/fail/attention)
        // treat a total miss as a no-op, since they have no fields to create from.
        using var h = new OperationsHarness();

        var outcome = h.Ops.Start("brand-new", "T", "S", OperationsHarness.IconUri, OperationsHarness.DeepLink, OperationsHarness.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.IsTrue(outcome.Success);
        Assert.IsNotNull(outcome.View);
    }
}
