using Atv.Store;

namespace Atv.Persistence;

/// <summary>Result of resolving a single caller handle for an update-class verb (rules 1-2 only, ERGO-21 "scoped 2026-07-07").</summary>
public enum ResolveOutcome
{
    /// <summary>No sidecar entry for this handle at all -- a clean unknown-handle no-op (ERGO-21).</summary>
    Unknown,

    /// <summary>Entry existed but the API no longer knows its Id (rule 2) -- the stale entry was dropped.</summary>
    Dropped,

    /// <summary>Entry existed and the API still knows the Id (rule 1) -- <see cref="ResolveResult.Id"/> is usable.</summary>
    Kept,
}

/// <summary>Outcome of <see cref="Reconciler.ResolveHandle"/>.</summary>
public readonly record struct ResolveResult(ResolveOutcome Outcome, string? Id);

/// <summary>What the full pass did to one sidecar entry.</summary>
public enum ReconcileAction
{
    /// <summary>Rule 1: entry present, API knows the id, not hidden.</summary>
    Kept,

    /// <summary>Rule 2: entry present, API no longer knows the id -- our own stale mapping, dropped.</summary>
    DroppedStaleEntry,

    /// <summary>Rule 3 (the ERGO-2 sweep): API id is HiddenByUser -- Remove()'d + entry dropped.</summary>
    SweptHidden,
}

/// <summary>One sidecar entry's outcome from a full <see cref="Reconciler.ReconcileAll"/> pass.</summary>
public readonly record struct ReconcileEntryResult(string Handle, string Id, ReconcileAction Action);

/// <summary>
/// Full four-rule pass result. <see cref="EntrylessIds"/> (rule 4) are
/// reported for visibility only -- reconciliation never touches them
/// (LIFE-23/phase 09 territory, out of scope here).
/// </summary>
public sealed record ReconcileSummary(
    IReadOnlyList<ReconcileEntryResult> Kept,
    IReadOnlyList<ReconcileEntryResult> DroppedStaleEntries,
    IReadOnlyList<ReconcileEntryResult> SweptHidden,
    IReadOnlyList<string> EntrylessIds);

/// <summary>
/// ERGO-21's four reconciliation rules, split into the two scopes the
/// decision record ratifies (2026-07-07): a per-handle resolver for the
/// update-class hot path (rules 1-2 only, no <c>FindAll()</c>, no sweep --
/// ERGO-19, "Should update invocations also trigger the user-hidden GC
/// sweep" -- decided no), and a full pass for start/remove/clear/watchdog
/// ticks (all four rules, one <c>FindAll()</c>).
///
/// NON-DESTRUCTIVE except for rule 3's <c>Remove()</c> (the ERGO-2 hidden
/// sweep) -- every other action only touches the sidecar's own files, never
/// a live API task. Callers MUST invoke these from inside a
/// <see cref="WriteGate"/> critical section; this type has no mutex
/// awareness of its own, matching WriteGate's role as the sole
/// synchronization primitive above this seam (INFRA-6).
/// </summary>
public static class Reconciler
{
    /// <summary>
    /// Scoped resolution for update-class verbs (step/state/done/fail/
    /// attention). Looks up ONLY <paramref name="handle"/>'s own sidecar
    /// entry and a single <see cref="IAppTaskStore.Find"/> call -- never
    /// <see cref="IAppTaskStore.FindAll"/>, never a sweep.
    ///
    /// Deliberately does not inspect <see cref="AppTaskView.HiddenByUser"/>:
    /// rule 3's ACTION (Remove + drop) is a full-pass-only concern, and this
    /// path performs no sweep either way -- so a hidden task's entry still
    /// resolves as <see cref="ResolveOutcome.Kept"/> here. This matches
    /// ERGO-19 ("update never sweeps"): hidden-state cleanup is left
    /// entirely to the next full pass, never blocking the update itself.
    /// </summary>
    public static ResolveResult ResolveHandle(IAppTaskStore store, SidecarStore sidecar, string handle)
    {
        var entry = sidecar.Read(handle);
        if (entry is null)
            return new ResolveResult(ResolveOutcome.Unknown, null);

        if (store.Find(entry.Id) is null)
        {
            sidecar.Delete(handle); // rule 2: our own stale mapping
            return new ResolveResult(ResolveOutcome.Dropped, null);
        }

        return new ResolveResult(ResolveOutcome.Kept, entry.Id); // rule 1
    }

    /// <summary>
    /// The full four-rule pass: one <see cref="IAppTaskStore.FindAll"/>, one
    /// <see cref="SidecarStore.ReadAll"/>, applied together. Rule 4
    /// (entryless) is reported, never acted on (LIFE-23/phase 09 territory).
    /// </summary>
    public static ReconcileSummary ReconcileAll(IAppTaskStore store, SidecarStore sidecar)
    {
        var apiById = store.FindAll().ToDictionary(t => t.Id);
        var entries = sidecar.ReadAll();

        var kept = new List<ReconcileEntryResult>();
        var dropped = new List<ReconcileEntryResult>();
        var swept = new List<ReconcileEntryResult>();
        var seenIds = new HashSet<string>();

        foreach (var (handle, entry) in entries)
        {
            seenIds.Add(entry.Id);

            if (!apiById.TryGetValue(entry.Id, out var task))
            {
                sidecar.Delete(handle);
                dropped.Add(new ReconcileEntryResult(handle, entry.Id, ReconcileAction.DroppedStaleEntry));
                continue;
            }

            if (task.HiddenByUser)
            {
                store.Remove(entry.Id);
                sidecar.Delete(handle);
                swept.Add(new ReconcileEntryResult(handle, entry.Id, ReconcileAction.SweptHidden));
                continue;
            }

            kept.Add(new ReconcileEntryResult(handle, entry.Id, ReconcileAction.Kept));
        }

        string[] entryless = [.. apiById.Keys.Where(id => !seenIds.Contains(id))];

        return new ReconcileSummary(kept, dropped, swept, entryless);
    }
}
