using Atv.Operations;
using Atv.Persistence;
using Atv.Store;

namespace Atv.Watchdog;

/// <summary>
/// LIFE-24 §6's presence-gated Ready-&gt;Idle UX decay clock -- a DELIBERATELY
/// SEPARATE pass from <see cref="WatchdogLoop"/>'s pre-existing hygiene reap
/// (<c>ExpireIdle</c>), living in its own file/type so the two clocks stay
/// structurally, not just conventionally, un-conflated: this type reads
/// <see cref="EngineMemory.ReadyDecay"/>/<see cref="WatchdogDeps.Presence"/>
/// and writes back with the entry's OWN existing <see cref="SidecarEntry.LastUpdate"/>
/// preserved (never the tick's <c>now</c>) on every non-demoting tick, so a
/// pure presence-sampling write can never masquerade as a real card write to
/// the hygiene clock. Only an actual demotion (a genuine content/state
/// mutation, exactly like every other real write in this codebase) legitimately
/// advances <see cref="SidecarEntry.LastUpdate"/> to <c>now</c>.
///
/// Only <see cref="AppTaskState.Completed"/> (semantically Ready) cards are
/// ever inspected -- Blocked/Broken/Working/Idle never decay (LIFE-24: "none
/// -- never decays" / Idle has no verb to begin with). A card with no
/// <see cref="EngineMemory.ReadyDecay"/> clock running (never claimed via
/// <c>ready</c> since the 15B upgrade, or a stale schema-&lt;3 entry) is
/// skipped -- there is nothing to accrue.
/// </summary>
internal static class ReadyDecay
{
    /// <summary>
    /// Runs one decay-pass tick over <paramref name="kept"/> (the SAME
    /// surviving-entries list <see cref="WatchdogLoop"/>'s hygiene pass just
    /// produced this tick). Samples <see cref="WatchdogDeps.Presence"/>
    /// exactly ONCE per tick, applied uniformly to every card considered this
    /// tick (LIFE-24: a courtesy heuristic, not an exact per-second timer).
    /// Returns the number of cards demoted Ready-&gt;Idle this tick.
    /// </summary>
    public static int RunPass(WatchdogDeps deps, IReadOnlyList<ReconcileEntryResult> kept, DateTimeOffset now)
    {
        if (deps.Presence is null) return 0;

        bool present = deps.Presence.IsPresent();
        int decayedCount = 0;

        foreach (var k in kept)
        {
            // FRESHNESS (invariant #6, same discipline as ExpireIdle): re-read
            // right now rather than trusting reconciliation's own snapshot --
            // also naturally skips any entry the hygiene pass already expired
            // this same tick (its fresh re-read comes back null).
            SidecarEntry? fresh = deps.Sidecar.Read(k.Handle);
            if (fresh is null) continue;

            AppTaskView? view = deps.Store.Find(fresh.Id);
            if (view is null) continue;

            if (view.State != AppTaskState.Completed) continue; // only Ready decays.

            EngineMemory memory = (fresh.EngineMemory ?? EngineMemory.Empty).Coalesced();
            if (memory.ReadyDecay is not { } decay) continue; // no clock running -- nothing to accrue.

            TimeSpan elapsed = now - decay.LastSampledAt;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero; // defensive: a backward-moving `now` never subtracts accrual.

            TimeSpan accrued = present ? decay.AccruedPresentTime + elapsed : decay.AccruedPresentTime;

            if (accrued >= deps.Settings.ReadyDecayThreshold)
            {
                DemoteToIdle(deps, k.Handle, fresh.Id, view, memory, now);
                decayedCount++;
            }
            else
            {
                // Pure bookkeeping progress: preserve `fresh.LastUpdate` UNCHANGED
                // (never `now`) -- see this type's own remarks on why a
                // decay-sampling write must never bump the hygiene clock's field.
                var updatedMemory = memory with { ReadyDecay = new ReadyDecayState(now, accrued) };
                deps.Sidecar.WriteWithMemory(k.Handle, fresh.Id, fresh.LastUpdate, updatedMemory);
            }
        }

        return decayedCount;
    }

    /// <summary>
    /// The courtesy demotion itself: Running/Completed/Paused/Error all share
    /// the safe <c>SequenceOfSteps</c> content shape (<c>SafeCombinationMatrix</c>),
    /// so decay ALWAYS normalizes the card's content back to that shape. Bug
    /// fix (2026-07-15 live dogfood): if the card most recently held a <c>ready
    /// --summary</c> TEXT result, its exact text has NO platform readback at
    /// all (<c>AppTaskInfo</c> exposes no getter for it -- an asymmetry
    /// documented on <see cref="Atv.Store.AppTaskView"/>), so
    /// <see cref="EngineMemory.LastSummary"/> -- the engine's OWN remembered
    /// copy of that text -- is used instead of the platform's (empty)
    /// step-readback fields, and the demoted card's step now shows the actual
    /// last message instead of resetting it. Only when there is truly nothing
    /// remembered (a schema-&lt;4 entry, or the card never held a summary) does
    /// a blank executing-step readback fall back to the same
    /// <see cref="AdvanceModel.NoStepsYetPlaceholder"/> baseline every other
    /// "nothing to archive yet" path in this codebase uses (the real platform
    /// throws on a genuinely empty executing step).
    /// </summary>
    private static void DemoteToIdle(WatchdogDeps deps, string handle, string id, AppTaskView view, EngineMemory memory, DateTimeOffset now)
    {
        string executing = view.ExecutingStep.Length > 0
            ? view.ExecutingStep
            : memory.LastSummary ?? AdvanceModel.NoStepsYetPlaceholder;
        var content = new AppTaskContentDto.SequenceOfSteps(view.CompletedSteps, executing);

        var validation = Validator.Validate(content, AppTaskState.Paused, bypass: false);
        if (!validation.Allowed)
        {
            // Should not happen by construction (SequenceOfSteps + Paused + no
            // question is an unconditionally safe cell) -- defensive only,
            // matching this codebase's uniform validate-before-write discipline.
            deps.Log($"watchdog: Ready->Idle decay for '{handle}' produced a refused combination ({validation.Reason}) -- skipped.");
            return;
        }

        deps.Store.Update(id, AppTaskState.Paused, content);
        // Leaving Ready retires both the clock and the remembered summary text
        // together -- the demoted card's own SequenceOfSteps content now holds
        // whatever it needed to (readable back normally from here on), so
        // there is nothing left for LastSummary to preserve.
        var clearedMemory = memory with { ReadyDecay = null, LastSummary = null };
        deps.Sidecar.WriteWithMemory(handle, id, now, clearedMemory);
        deps.Log($"watchdog: '{handle}' decayed Ready -> Idle (presence-gated courtesy demotion; id {id}).");
    }
}
