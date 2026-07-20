using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.Persistence;
using Codevoid.AgentTaskVoid.Presence;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.Watchdog;

/// <summary>
/// Everything one <see cref="WatchdogLoop"/> tick needs, gathered in one
/// place (LIFE-16: one watchdog per package identity). Deliberately plain
/// data + delegates -- no package identity, no real WinRT type -- so the
/// whole loop is fake-testable (INFRA-21). The watchdog observes tasks ONLY
/// through <see cref="Store"/>'s <see cref="IAppTaskStore.FindAll"/> (via
/// <see cref="Reconciler"/>), exactly like every other consumer -- there is
/// NO production raw <c>tasks.json</c> reader (ratified 2026-07-07).
/// </summary>
/// <summary>
/// <see cref="Presence"/> (phase 15B, LIFE-24 §6) is the presence gate for
/// the Ready-&gt;Idle decay pass (<see cref="WatchdogLoop.RunTick"/>'s SEPARATE
/// decay step, never folded into the pre-existing hygiene-reap pass above) --
/// <see langword="null"/> in any context that never wires one (e.g. the
/// LIFE-20 boot-recovery flat clear, which has no need for it): decay simply
/// never advances in that case, and the unrelated hygiene reap is entirely
/// unaffected either way (the two clocks are independent by construction, not
/// just by convention).
/// </summary>
public sealed record WatchdogDeps(
    IAppTaskStore Store,
    SidecarStore Sidecar,
    RecycleBin RecycleBin,
    WriteGate WriteGate,
    IconService? Icons,
    Action<string> Log,
    Func<DateTimeOffset> Clock,
    Settings Settings,
    IPresenceSource? Presence = null);

/// <summary>
/// One tick's outcome (breadcrumb counters for the FAIL-3 log / tests).
/// <see cref="SupervisedCount"/> is <see langword="null"/> when the tick
/// could not acquire the write mutex within the bounded wait (non-strict
/// <see cref="WriteGate"/> timeout) -- an inconclusive tick, never treated as
/// evidence of an empty supervised set by <see cref="WatchdogLoop.Run"/>.
/// </summary>
public sealed record TickResult(
    int? SupervisedCount,
    int ExpiredCount,
    int HiddenSweptCount,
    int EntrylessReapedCount,
    int RecycleScavengedCount,
    int ReadyDecayedCount = 0);

/// <summary>
/// Everything <see cref="WatchdogLoop.Run"/> needs to actually run the loop,
/// on top of <see cref="WatchdogDeps"/>: the LIFE-18 single-instance mutex,
/// the sleep primitive (real <see cref="Thread.Sleep(TimeSpan)"/> in
/// production, a no-op/counting delegate in tests), the LIFE-20 boot-recovery
/// startup-item enable/disable hooks, and an optional test-only
/// <see cref="ShouldStop"/> escape hatch so a unit test can bound an
/// otherwise-infinite loop deterministically.
/// </summary>
public sealed record RunContext(
    WatchdogDeps Deps,
    Mutex InstanceMutex,
    Action<TimeSpan> Sleep,
    Action EnableStartupTask,
    Action DisableStartupTask,
    Func<bool>? ShouldStop = null);

/// <summary>
/// INFRA-21's shared supervision logic core -- identical across the spawned-
/// process, in-proc-thread, and test hosts (<see cref="IWatchdogHost"/>
/// implementations only differ in HOW this gets hosted, never in what it
/// does). Stateless-over-disk (LIFE-16): <see cref="RunTick"/> is a pure
/// function of (disk state via <see cref="WatchdogDeps"/>, wall clock) -- no
/// in-memory per-task timer state, so a respawn reconstructs everything.
/// </summary>
public static class WatchdogLoop
{
    /// <summary>
    /// LIFE-19's anti-flap grace: the loop tolerates this many EXTRA
    /// consecutive empty ticks (beyond the first) before exiting, so a quick
    /// <c>start</c>-&gt;<c>done</c>-&gt;<c>remove</c> burst does not thrash
    /// spawn/exit. 1 extra tick = the loop needs to observe the supervised
    /// set empty on two consecutive polls before it gives up.
    /// </summary>
    public const int AntiFlapGraceTicks = 1;

    // ---- one tick ------------------------------------------------------------

    /// <summary>
    /// Runs exactly one tick under the shared INFRA-6 write mutex (the WHOLE
    /// tick is one critical section -- reconcile, expiry, entryless reap, and
    /// recycle-bin scavenge all happen inside the same
    /// <see cref="WriteGate.TryRun{T}"/> call, matching every other
    /// read-modify-write in this codebase). Order (requirements.md /
    /// plan/README.md standing invariant #6): reconcile (incl. the ERGO-2
    /// hidden sweep) FIRST, then expiry -- which re-reads each surviving
    /// handle's <see cref="SidecarEntry.LastUpdate"/> fresh from
    /// <see cref="SidecarStore.Read"/> immediately before comparing to
    /// <paramref name="now"/>, never reusing reconciliation's own snapshot --
    /// then the LIFE-23 entryless-orphan reap, then the recycle-bin TTL
    /// scavenge.
    /// </summary>
    /// <param name="onAfterReconcile">
    /// TEST-ONLY hook, invoked after reconciliation completes and before the
    /// expiry pass reads anything -- lets a test simulate a write landing
    /// between the two (e.g. a fresh <see cref="SidecarStore.Write"/>) and
    /// deterministically prove the expiry pass picks up the FRESH value
    /// rather than an earlier snapshot. <see langword="null"/> (the default)
    /// in every real host.
    /// </param>
    public static TickResult RunTick(WatchdogDeps deps, DateTimeOffset now, Action? onAfterReconcile = null)
    {
        TickResult? result = null;
        bool ran = deps.WriteGate.TryRun(() => result = RunTickCore(deps, now, onAfterReconcile));

        if (!ran)
        {
            deps.Log("watchdog: tick skipped -- could not acquire the write mutex within the bounded wait.");
            return new TickResult(null, 0, 0, 0, 0);
        }

        return result!;
    }

    private static TickResult RunTickCore(WatchdogDeps deps, DateTimeOffset now, Action? onAfterReconcile)
    {
        ReconcileSummary summary = Reconciler.ReconcileAll(deps.Store, deps.Sidecar);
        ReapIconsFor(deps, summary.DroppedStaleEntries);
        ReapIconsFor(deps, summary.SweptHidden);

        onAfterReconcile?.Invoke();

        int expired = ExpireIdle(deps, summary.Kept, now);
        int entrylessReaped = ReapEntryless(deps, summary.EntrylessIds);

        ScavengeResult scavenge = deps.RecycleBin.Scavenge(now, deps.Settings.RecycleBinTtl);
        if (deps.Icons is not null)
        {
            foreach (string handle in scavenge.Removed)
                deps.Icons.ReapRecycledIcon(handle);
        }

        // SEPARATE pass (LIFE-24 §6): the presence-gated Ready->Idle UX decay
        // clock, deliberately never folded into ExpireIdle above -- that pass
        // is the UNRELATED wall-clock hygiene reap (LIFE-22), which must keep
        // firing regardless of presence. Runs against summary.Kept (the same
        // surviving-entries list ExpireIdle used) with its own fresh re-read
        // per handle, so an entry ExpireIdle just expired this same tick is
        // silently skipped here (already gone) rather than double-handled.
        int decayed = ReadyDecay.RunPass(deps, summary.Kept, now);

        int supervisedCount = deps.Store.FindAll().Count;
        return new TickResult(supervisedCount, expired, summary.SweptHidden.Count, entrylessReaped, scavenge.Removed.Count, decayed);
    }

    /// <summary>LIFE-22's per-state idle expiry, LIFE-21's "what expiry does": re-read <see cref="SidecarStore.Read"/> fresh per surviving handle, compare wall-clock <c>now</c> vs the FRESH <c>lastUpdate</c>, and on expiry remove the card, tombstone a recycle record read off the live API card, and move its icon into the recycle folder.</summary>
    private static int ExpireIdle(WatchdogDeps deps, IReadOnlyList<ReconcileEntryResult> kept, DateTimeOffset now)
    {
        int expiredCount = 0;
        foreach (var k in kept)
        {
            // FRESHNESS (invariant #6): re-read right now, never reuse reconciliation's snapshot.
            SidecarEntry? fresh = deps.Sidecar.Read(k.Handle);
            if (fresh is null)
                continue; // Raced away since reconciliation (defensive only -- excluded by the mutex hold in production).

            AppTaskView? view = deps.Store.Find(fresh.Id);
            if (view is null)
                continue; // Vanished since reconciliation -- next tick's reconcile drops the now-stale entry.

            TimeSpan threshold = IdleThresholdFor(view.State, deps.Settings);
            if (now - fresh.LastUpdate < threshold)
                continue;

            ExpireOne(deps, k.Handle, view, now);
            expiredCount++;
        }
        return expiredCount;
    }

    private static TimeSpan IdleThresholdFor(AppTaskState state, Settings settings) => state switch
    {
        AppTaskState.Running => settings.IdleRunning,
        AppTaskState.Paused => settings.IdlePaused,
        AppTaskState.NeedsAttention => settings.IdleNeedsAttention,
        AppTaskState.Completed or AppTaskState.Error => settings.IdleCompleted,
        _ => settings.IdleRunning,
    };

    private static void ExpireOne(WatchdogDeps deps, string handle, AppTaskView view, DateTimeOffset now)
    {
        deps.Store.Remove(view.Id);
        deps.Sidecar.Delete(handle);
        deps.RecycleBin.Tombstone(new RecycleRecord(handle, view.Title, view.Subtitle, view.IconUri.ToString(), view.DeepLink, now));
        deps.Icons?.MoveToRecycle(handle);
        deps.Log($"watchdog: expired '{handle}' (id {view.Id}, state {view.State}) -- idle past the configured threshold; tombstoned to the recycle bin.");
    }

    /// <summary>LIFE-23: reap ALL live API tasks with no sidecar entry, unconditionally -- no mass-deletion guard (per-identity <c>FindAll</c> scoping already bounds the blast radius to our own cards). Audible: logs the count as a breadcrumb, never a gate.</summary>
    private static int ReapEntryless(WatchdogDeps deps, IReadOnlyList<string> entrylessIds)
    {
        int count = 0;
        foreach (string id in entrylessIds)
        {
            if (deps.Store.Remove(id))
                count++;
        }
        if (count > 0)
            deps.Log($"watchdog: reaped {count} entryless orphan task(s) with no sidecar entry.");
        return count;
    }

    private static void ReapIconsFor(WatchdogDeps deps, IReadOnlyList<ReconcileEntryResult> entries)
    {
        if (deps.Icons is null) return;
        foreach (var e in entries)
            deps.Icons.ReapLiveIcon(e.Handle);
    }

    // ---- the loop --------------------------------------------------------------

    /// <summary>
    /// LIFE-18's acquire-or-exit single instance enforcement (shared across
    /// spawn/inproc, INFRA-21) followed by the LIFE-19 poll-tick-shutdown
    /// loop. A startup-race loser (mutex already held by another instance)
    /// exits immediately WITHOUT touching the boot-recovery startup item --
    /// only the acquiring instance's own lifecycle governs that toggle.
    /// </summary>
    public static void Run(RunContext ctx)
    {
        bool acquired;
        try
        {
            acquired = ctx.InstanceMutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
            ctx.Deps.Log("watchdog: acquired the single-instance mutex abandoned by a crashed prior watchdog; proceeding.");
        }

        if (!acquired)
        {
            ctx.Deps.Log("watchdog: another instance already holds the single-instance mutex -- exiting cleanly (startup-race loser).");
            return;
        }

        try
        {
            SafeInvoke(ctx.EnableStartupTask, ctx.Deps.Log, "enable");

            int consecutiveEmptyTicks = 0;
            while (ctx.ShouldStop?.Invoke() != true)
            {
                TickResult result = RunTick(ctx.Deps, ctx.Deps.Clock());

                if (result.SupervisedCount is int count)
                {
                    if (count <= 0)
                    {
                        consecutiveEmptyTicks++;
                        if (consecutiveEmptyTicks > AntiFlapGraceTicks)
                            break;
                    }
                    else
                    {
                        consecutiveEmptyTicks = 0;
                    }
                }
                // A null SupervisedCount (gate busy this tick) is inconclusive --
                // leave the anti-flap counter untouched and just retry next poll.

                if (ctx.ShouldStop?.Invoke() == true) break;
                ctx.Sleep(ctx.Deps.Settings.WatchdogPollInterval);
            }

            ctx.Deps.Log("watchdog: supervised set empty past the anti-flap grace -- exiting.");
        }
        finally
        {
            SafeInvoke(ctx.DisableStartupTask, ctx.Deps.Log, "disable");
            try { ctx.InstanceMutex.ReleaseMutex(); }
            catch (Exception ex) { ctx.Deps.Log($"watchdog: failed to release the single-instance mutex on exit ({ex.GetType().Name}: {ex.Message})."); }
        }
    }

    private static void SafeInvoke(Action action, Action<string> log, string what)
    {
        try { action(); }
        catch (Exception ex) { log($"watchdog: failed to {what} the boot-recovery startup item ({ex.GetType().Name}: {ex.Message}) -- non-disruptive, continuing."); }
    }
}
