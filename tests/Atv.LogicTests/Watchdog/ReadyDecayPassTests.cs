using Codevoid.AgentTaskVoid.Persistence;
using Codevoid.AgentTaskVoid.Store;
using Codevoid.AgentTaskVoid.Watchdog;

namespace Codevoid.AgentTaskVoid.LogicTests.Watchdog;

/// <summary>
/// AC5's watchdog-side coverage: the LIFE-24 §6 presence-gated Ready-&gt;Idle
/// decay pass, proven as a SEPARATE mechanism from the pre-existing LIFE-22
/// hygiene reap (<see cref="WatchdogLoopTickTests"/>) -- accrual only while
/// present, a demotion at threshold, and (the control) the hygiene reap
/// firing regardless of presence. The "only a transition INTO Ready starts
/// the clock" half of AC5 is <c>Codevoid.AgentTaskVoid.Semantics.SemanticEngine</c>'s job
/// (<c>ReadyDecayClockTests.cs</c>) -- this file only covers what happens to
/// an ALREADY-started clock once the watchdog observes it.
/// </summary>
[TestClass]
public sealed class ReadyDecayPassTests
{
    private static Codevoid.AgentTaskVoid.Store.AppTaskView SeedReadyCard(Codevoid.AgentTaskVoid.LogicTests.Store.FakeAppTaskStore store, string title = "T")
        => store.SeedEntrylessTask(title, "S", AppTaskState.Completed);

    /// <summary>
    /// Every test in this file EXCEPT the two hygiene-independence controls at
    /// the bottom is deliberately isolating the DECAY pass alone -- pushed out
    /// to an IdleCompleted long enough that the UNRELATED hygiene reap could
    /// never fire within the test's own time window and confound the result
    /// (a real interaction the harness's default 10-minute IdleCompleted would
    /// otherwise trip, since these tests exercise decay windows of similar or
    /// longer magnitude). The two control tests at the bottom deliberately use
    /// the harness's ordinary default instead, to prove the real interaction.
    /// </summary>
    private static WatchdogTestHarness NewIsolatedHarness()
    {
        var h = new WatchdogTestHarness();
        h.Settings = h.Settings with { IdleCompleted = TimeSpan.FromDays(3650) };
        return h;
    }

    // ---- accrues only while present ------------------------------------------

    [TestMethod]
    public void RunTick_ReadyDecay_AccruesWhilePresent_DoesNotBumpHygieneLastUpdate()
    {
        using var h = NewIsolatedHarness();
        var view = SeedReadyCard(h.Store);
        DateTimeOffset entryLastUpdate = WatchdogTestHarness.Now - TimeSpan.FromMinutes(1);
        var decay = new ReadyDecayState(entryLastUpdate, TimeSpan.Zero);
        h.Sidecar.WriteWithMemory("h1", view.Id, entryLastUpdate, EngineMemory.Empty with { ReadyDecay = decay });
        h.Presence.Present = true;

        DateTimeOffset tickTime = entryLastUpdate + TimeSpan.FromMinutes(5); // well under the 20-min threshold
        WatchdogLoop.RunTick(h.Deps(), tickTime);

        var entry = h.Sidecar.Read("h1")!;
        Assert.AreEqual(TimeSpan.FromMinutes(5), entry.EngineMemory!.ReadyDecay!.AccruedPresentTime);
        Assert.AreEqual(tickTime, entry.EngineMemory.ReadyDecay.LastSampledAt, "the sampling anchor always advances, whether or not the delta counted.");
        Assert.AreEqual(entryLastUpdate, entry.LastUpdate, "a pure decay-sampling write must NEVER bump the UNRELATED hygiene clock's LastUpdate (LIFE-24: never conflated).");
        Assert.AreEqual(AppTaskState.Completed, h.Store.Find(view.Id)!.State, "well under threshold -- no demotion yet.");
    }

    [TestMethod]
    public void RunTick_ReadyDecay_DoesNotAccrueWhileAbsent()
    {
        using var h = NewIsolatedHarness();
        var view = SeedReadyCard(h.Store);
        DateTimeOffset entryLastUpdate = WatchdogTestHarness.Now - TimeSpan.FromMinutes(1);
        var decay = new ReadyDecayState(entryLastUpdate, TimeSpan.Zero);
        h.Sidecar.WriteWithMemory("h1", view.Id, entryLastUpdate, EngineMemory.Empty with { ReadyDecay = decay });
        h.Presence.Present = false;

        DateTimeOffset tickTime = entryLastUpdate + TimeSpan.FromMinutes(5);
        WatchdogLoop.RunTick(h.Deps(), tickTime);

        var entry = h.Sidecar.Read("h1")!;
        Assert.AreEqual(TimeSpan.Zero, entry.EngineMemory!.ReadyDecay!.AccruedPresentTime, "absent time must never count toward the accrual.");
        Assert.AreEqual(tickTime, entry.EngineMemory.ReadyDecay.LastSampledAt, "the anchor still advances even though nothing was counted -- an absent stretch is never retroactively counted once presence returns.");
    }

    [TestMethod]
    public void RunTick_ReadyDecay_AccrualAcrossMultipleTicks_MixedPresence()
    {
        using var h = NewIsolatedHarness();
        var view = SeedReadyCard(h.Store);
        DateTimeOffset start = WatchdogTestHarness.Now;
        h.Sidecar.WriteWithMemory("h1", view.Id, start, EngineMemory.Empty with { ReadyDecay = new ReadyDecayState(start, TimeSpan.Zero) });

        h.Presence.Present = true;
        WatchdogLoop.RunTick(h.Deps(), start + TimeSpan.FromMinutes(5)); // +5 present

        h.Presence.Present = false;
        WatchdogLoop.RunTick(h.Deps(), start + TimeSpan.FromMinutes(15)); // +10 absent -- must not count

        h.Presence.Present = true;
        WatchdogLoop.RunTick(h.Deps(), start + TimeSpan.FromMinutes(18)); // +3 present

        var entry = h.Sidecar.Read("h1")!;
        Assert.AreEqual(TimeSpan.FromMinutes(8), entry.EngineMemory!.ReadyDecay!.AccruedPresentTime, "only the two present intervals (5 + 3) should have counted.");
    }

    // ---- demotion at threshold -------------------------------------------------

    [TestMethod]
    public void RunTick_ReadyDecay_ReachesThreshold_DemotesToIdle_ClearsClock()
    {
        using var h = NewIsolatedHarness();
        var view = SeedReadyCard(h.Store);
        DateTimeOffset start = WatchdogTestHarness.Now;
        h.Sidecar.WriteWithMemory("h1", view.Id, start, EngineMemory.Empty with { ReadyDecay = new ReadyDecayState(start, TimeSpan.Zero) });
        h.Presence.Present = true;

        DateTimeOffset tickTime = start + h.Settings.ReadyDecayThreshold; // exactly at threshold
        var result = WatchdogLoop.RunTick(h.Deps(), tickTime);

        Assert.AreEqual(1, result.ReadyDecayedCount);
        var found = h.Store.Find(view.Id)!;
        Assert.AreEqual(AppTaskState.Paused, found.State, "a courtesy demotion, never a deletion -- the card is still live.");

        var entry = h.Sidecar.Read("h1")!;
        Assert.IsNull(entry.EngineMemory!.ReadyDecay, "the clock is cleared once the card has left Ready.");
        Assert.AreEqual(tickTime, entry.LastUpdate, "an ACTUAL demotion is a real write, unlike pure sampling -- LastUpdate legitimately advances, giving the card a fresh hygiene grace period under its new (longer) Paused threshold.");
    }

    [TestMethod]
    public void RunTick_ReadyDecay_DemotesUsingSequenceOfSteps_EvenIfContentWasATextSummary()
    {
        // A `ready --summary` swap leaves the platform holding a TextSummaryResult,
        // whose text has NO readback (INFRA-15) -- decay must still land safely on
        // Paused, which only SequenceOfSteps supports (SafeCombinationMatrix).
        // No EngineMemory.LastSummary is seeded here, so this specifically covers
        // the "nothing remembered" placeholder fallback -- see the next test for
        // the (now more common) remembered-summary case.
        using var h = NewIsolatedHarness();
        var view = h.Store.Create("T", "S", WatchdogTestHarness.DeepLink, WatchdogTestHarness.IconUri,
            new AppTaskContentDto.TextSummaryResult("All done."));
        h.Store.Update(view.Id, AppTaskState.Completed, new AppTaskContentDto.TextSummaryResult("All done."));
        DateTimeOffset start = WatchdogTestHarness.Now;
        h.Sidecar.WriteWithMemory("h1", view.Id, start, EngineMemory.Empty with { ReadyDecay = new ReadyDecayState(start, TimeSpan.Zero) });
        h.Presence.Present = true;

        var result = WatchdogLoop.RunTick(h.Deps(), start + h.Settings.ReadyDecayThreshold);

        Assert.AreEqual(1, result.ReadyDecayedCount);
        var found = h.Store.Find(view.Id)!;
        Assert.AreEqual(AppTaskState.Paused, found.State);
        Assert.AreEqual(Codevoid.AgentTaskVoid.Operations.AdvanceModel.NoStepsYetPlaceholder, found.ExecutingStep);
    }

    [TestMethod]
    public void RunTick_ReadyDecay_DemotesUsingTheRememberedSummary_InsteadOfResettingTheText()
    {
        // Bug fix (found via live dogfood, 2026-07-15): the demotion used to
        // ALWAYS reset to "Not started yet." for a TextSummaryResult-held card,
        // even when the engine had its own remembered copy of the text
        // (EngineMemory.LastSummary, populated by every real `ready --summary`
        // claim) -- this is the exact "idle/paused resets the text instead of
        // holding the last message" bug report.
        using var h = NewIsolatedHarness();
        var view = h.Store.Create("T", "S", WatchdogTestHarness.DeepLink, WatchdogTestHarness.IconUri,
            new AppTaskContentDto.TextSummaryResult("All done."));
        h.Store.Update(view.Id, AppTaskState.Completed, new AppTaskContentDto.TextSummaryResult("All done."));
        DateTimeOffset start = WatchdogTestHarness.Now;
        h.Sidecar.WriteWithMemory("h1", view.Id, start,
            EngineMemory.Empty with { ReadyDecay = new ReadyDecayState(start, TimeSpan.Zero), LastSummary = "All done." });
        h.Presence.Present = true;

        var result = WatchdogLoop.RunTick(h.Deps(), start + h.Settings.ReadyDecayThreshold);

        Assert.AreEqual(1, result.ReadyDecayedCount);
        var found = h.Store.Find(view.Id)!;
        Assert.AreEqual(AppTaskState.Paused, found.State);
        Assert.AreEqual("All done.", found.ExecutingStep, "the Paused card must hold the last message, not reset to the placeholder.");

        var entry = h.Sidecar.Read("h1")!;
        Assert.IsNull(entry.EngineMemory!.LastSummary, "leaving Ready (even via decay) retires the remembered copy -- the demoted card's own content now holds it, readable normally from here on.");
    }

    // ---- state filter: only Completed(Ready) cards are ever considered --------

    [TestMethod]
    public void RunTick_ReadyDecay_NonCompletedCard_NeverConsidered_EvenIfClockDataPresent()
    {
        using var h = NewIsolatedHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Running);
        DateTimeOffset start = WatchdogTestHarness.Now;
        // Contrived: clock bookkeeping present on a Running card (should never
        // happen via the real engine -- ClaimReady is the only writer -- but the
        // watchdog pass must be robust to it regardless).
        h.Sidecar.WriteWithMemory("h1", view.Id, start, EngineMemory.Empty with { ReadyDecay = new ReadyDecayState(start, TimeSpan.FromHours(10)) });
        h.Presence.Present = true;

        var result = WatchdogLoop.RunTick(h.Deps(), start + h.Settings.ReadyDecayThreshold);

        Assert.AreEqual(0, result.ReadyDecayedCount);
        Assert.AreEqual(AppTaskState.Running, h.Store.Find(view.Id)!.State);
    }

    [TestMethod]
    public void RunTick_ReadyDecay_BlockedCard_NeverConsidered_EvenIfClockDataPresent()
    {
        using var h = NewIsolatedHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.NeedsAttention);
        DateTimeOffset start = WatchdogTestHarness.Now;
        h.Sidecar.WriteWithMemory("h1", view.Id, start, EngineMemory.Empty with { ReadyDecay = new ReadyDecayState(start, TimeSpan.FromHours(10)) });
        h.Presence.Present = true;

        var result = WatchdogLoop.RunTick(h.Deps(), start + h.Settings.ReadyDecayThreshold);

        Assert.AreEqual(0, result.ReadyDecayedCount, "LIFE-24: Blocked never decays.");
        Assert.AreEqual(AppTaskState.NeedsAttention, h.Store.Find(view.Id)!.State);
    }

    [TestMethod]
    public void RunTick_ReadyDecay_BrokenCard_NeverConsidered_EvenIfClockDataPresent()
    {
        using var h = NewIsolatedHarness();
        var view = h.Store.SeedEntrylessTask("T", "S", AppTaskState.Error);
        DateTimeOffset start = WatchdogTestHarness.Now;
        h.Sidecar.WriteWithMemory("h1", view.Id, start, EngineMemory.Empty with { ReadyDecay = new ReadyDecayState(start, TimeSpan.FromHours(10)) });
        h.Presence.Present = true;

        var result = WatchdogLoop.RunTick(h.Deps(), start + h.Settings.ReadyDecayThreshold);

        Assert.AreEqual(0, result.ReadyDecayedCount, "LIFE-24: Broken never decays.");
        Assert.AreEqual(AppTaskState.Error, h.Store.Find(view.Id)!.State);
    }

    [TestMethod]
    public void RunTick_ReadyDecay_NoClockRunning_NothingHappens()
    {
        using var h = NewIsolatedHarness();
        var view = SeedReadyCard(h.Store); // Completed, but never claimed via `ready` under 15B -- no ReadyDecay.
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now);
        h.Presence.Present = true;

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now + h.Settings.ReadyDecayThreshold + TimeSpan.FromDays(1));

        Assert.AreEqual(0, result.ReadyDecayedCount);
        Assert.AreEqual(AppTaskState.Completed, h.Store.Find(view.Id)!.State);
    }

    [TestMethod]
    public void RunTick_ReadyDecay_NoPresenceSourceWired_NeverAdvances()
    {
        using var h = NewIsolatedHarness();
        var view = SeedReadyCard(h.Store);
        DateTimeOffset start = WatchdogTestHarness.Now;
        h.Sidecar.WriteWithMemory("h1", view.Id, start, EngineMemory.Empty with { ReadyDecay = new ReadyDecayState(start, TimeSpan.Zero) });

        var result = WatchdogLoop.RunTick(h.Deps(withPresence: false), start + h.Settings.ReadyDecayThreshold + TimeSpan.FromDays(1));

        Assert.AreEqual(0, result.ReadyDecayedCount, "a context with no presence source wired must never advance decay.");
        Assert.AreEqual(AppTaskState.Completed, h.Store.Find(view.Id)!.State);
    }

    // ---- control: the UNRELATED hygiene reap still fires, regardless of presence ----

    [TestMethod]
    public void RunTick_HygieneReap_StillFiresOnAReadyCard_RegardlessOfPresence()
    {
        using var h = new WatchdogTestHarness();
        var view = SeedReadyCard(h.Store); // no ReadyDecay clock at all -- isolates the hygiene mechanism alone.
        h.Sidecar.Write("h1", view.Id, WatchdogTestHarness.Now - h.Settings.IdleCompleted);
        h.Presence.Present = false; // the user is entirely absent -- decay could never fire either way.

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(1, result.ExpiredCount, "LIFE-22's wall-clock hygiene reap must fire independent of presence -- it is a DIFFERENT clock from decay.");
        Assert.IsNull(h.Store.Find(view.Id), "the hygiene reap actually REMOVES the card (unlike decay's courtesy demotion) -- a real tombstone.");
        Assert.AreEqual(0, result.ReadyDecayedCount, "nothing for decay to do here -- confirms the two counts are independently reported, never conflated.");
    }

    [TestMethod]
    public void RunTick_HygieneReap_FiresEvenWhileADecayClockIsMidFlight_BothPassesIndependent()
    {
        using var h = new WatchdogTestHarness();
        var view = SeedReadyCard(h.Store);
        DateTimeOffset staleEntryTime = WatchdogTestHarness.Now - h.Settings.IdleCompleted - TimeSpan.FromMinutes(1);
        // A decay clock IS running (well under ITS OWN threshold), but the entry's
        // hygiene LastUpdate is independently already stale past IdleCompleted.
        h.Sidecar.WriteWithMemory("h1", view.Id, staleEntryTime, EngineMemory.Empty with { ReadyDecay = new ReadyDecayState(staleEntryTime, TimeSpan.FromMinutes(1)) });
        h.Presence.Present = false;

        var result = WatchdogLoop.RunTick(h.Deps(), WatchdogTestHarness.Now);

        Assert.AreEqual(1, result.ExpiredCount, "hygiene reaps this card on ITS OWN wall-clock terms, unaffected by the in-flight decay clock or presence.");
        Assert.IsNull(h.Store.Find(view.Id));
    }
}
