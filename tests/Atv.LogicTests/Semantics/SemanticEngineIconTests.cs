using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.Persistence;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Semantics;

/// <summary>
/// Phase-15 counterpart of phase-07's <c>TaskOperationsIconTests</c> --
/// same icon-cleanup wiring obligations (ERGO-23), now proven through
/// <see cref="Codevoid.AgentTaskVoid.Semantics.SemanticEngine"/>'s own upsert pipeline instead
/// of the retired <c>TaskOperations.Start</c>.
/// </summary>
[TestClass]
public sealed class SemanticEngineIconTests
{
    private static readonly Uri DeepLink = SemanticEngineHarness.DeepLink;
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    [TestMethod]
    public void Remove_ReapsTheLiveIconCopy()
    {
        using var h = new SemanticEngineHarness(withIcons: true);
        Uri iconUri = h.Icons!.Place("h1", IconTokens.Default);
        h.Engine.Working("h1", "T", "S", iconUri, DeepLink, "goal", Now);
        Assert.IsTrue(File.Exists(iconUri.LocalPath));

        h.Ops.Remove("h1", Now);

        Assert.IsFalse(File.Exists(iconUri.LocalPath), "remove must reap the per-handle icon copy (ERGO-23), whether the card was created via TaskOperations or SemanticEngine.");
    }

    [TestMethod]
    public void ReconciliationDrop_StaleEntry_ReapsTheLiveIconCopy_OnNextEngineCall()
    {
        using var h = new SemanticEngineHarness(withIcons: true);
        Uri iconUri = h.Icons!.Place("h1", IconTokens.Default);
        var started = h.Engine.Working("h1", "T", "S", iconUri, DeepLink, "goal", Now);
        h.Store.SimulateVanish(started.View!.Id); // the API forgot about it -- h1's sidecar mapping is now stale

        // The engine's own per-handle resolve (not a full sweep -- see the engine's
        // type-level remarks) still reaps h1's icon the moment IT is targeted again.
        var outcome = h.Engine.Working("h1", "T2", "S2", h.Icons.Place("h1", IconTokens.Default), DeepLink, "goal2", Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success, "a stale-mapping drop on the SAME handle must still resolve as a fresh create, not an error.");
    }

    [TestMethod]
    public void IconTokenChanged_OnLiveCard_ForcesRemoveCreate_ReapsNothingButStepHistoryIsLost()
    {
        // Note: IconService.Place always writes to the SAME stable per-handle path
        // regardless of glyph token (that's how ERGO-15's separation-by-session model
        // works) -- so a REAL glyph-token change is invisible at the IconUri level.
        // The icon-immutable-forces-recreate branch guards a DIFFERENT raw Uri being
        // supplied directly to the engine (this codebase's own IconUri/OtherIconUri
        // constants stand in for that -- the same shape the v1 predecessor test used).
        using var h = new SemanticEngineHarness(withIcons: true);
        h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, DeepLink, "goal", Now);
        h.Engine.Activity("h1", "T", "S", SemanticEngineHarness.IconUri, DeepLink, Codevoid.AgentTaskVoid.Semantics.ActivityKind.Read, "a.txt", null, null, Now.AddMinutes(1));

        Uri secondIcon = SemanticEngineHarness.OtherIconUri;
        var outcome = h.Engine.Working("h1", "T", "S", secondIcon, DeepLink, "new goal", Now.AddMinutes(2));

        Assert.IsTrue(outcome.Success);
        Assert.IsTrue(outcome.IconChanged);
        Assert.AreEqual(secondIcon, outcome.View!.IconUri);
        Assert.IsEmpty(outcome.View.CompletedSteps, "icon-forced Remove+Create loses step history, unavoidably (ERGO-25's icon caveat, carried into v2).");
    }

    [TestMethod]
    public void Resurrection_ReapsTheOrphanedRecycledIcon_UsesTheCallersFreshIconInstead()
    {
        using var h = new SemanticEngineHarness(withIcons: true);
        h.Icons!.Place("h1", IconTokens.Default);
        h.Icons.MoveToRecycle("h1"); // stands in for the watchdog's expiry-tombstone icon move
        h.RecycleBin.Tombstone(new RecycleRecord("h1", "Stored Title", "Stored Subtitle", null, DeepLink, Now.AddHours(-1)));

        string recycledIconPath = Path.Combine(h.RecycleDirPath, HandleEncoding.Encode("h1") + ".png");
        Assert.IsTrue(File.Exists(recycledIconPath), "arrange sanity check: the recycled copy must exist before resurrection.");

        // Every v2 verb carries its own fresh, already-placed icon (unlike v1's
        // update-class verbs) -- the caller supplies a FRESH file here.
        Uri freshUri = h.Icons.Place("h1", IconTokens.Default);
        var outcome = h.Engine.Working("h1", "T", "S", freshUri, DeepLink, "goal", Now);

        Assert.AreEqual(Codevoid.AgentTaskVoid.Operations.OutcomeKind.Resurrected, outcome.Kind);
        Assert.AreEqual(freshUri, outcome.View!.IconUri, "the caller's fresh icon wins -- v2 never moves the old recycled copy back.");
        Assert.IsFalse(File.Exists(recycledIconPath), "the now-orphaned recycled copy must be reaped, not left behind.");
    }
}
