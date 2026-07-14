using Atv.Icons;
using Atv.Persistence;
using Atv.Store;

namespace Atv.Operations;

/// <summary>How a <see cref="TaskOperations"/> call resolved. Boundary note: this is a structured result for a caller (phase 08) to map to exit codes / `--json` -- this type does no exit-code or CLI-output work itself.</summary>
public enum OutcomeKind
{
    /// <summary>Store write happened; the combination was in the documented safe set.</summary>
    Accepted,

    /// <summary>Store write happened despite being outside the documented safe set, because the caller passed <c>--unsafe</c>.</summary>
    AcceptedUnsafe,

    /// <summary>No store write: the ERGO-10 validator refused the combination (and <c>--unsafe</c> wasn't set).</summary>
    RefusedUnsafeCombo,

    /// <summary>No store write: the arguments themselves are invalid for this verb (e.g. <c>state</c> given anything but running/paused).</summary>
    RefusedInvalidArgument,

    /// <summary>No store write: the handle is not live and not recoverable from the recycle bin (never seen, or past TTL).</summary>
    UnknownHandleNoOp,

    /// <summary>Store write happened: the handle was found in the recycle bin within TTL and the card was re-created.</summary>
    Resurrected,

    /// <summary>Store write happened: the live task (and its sidecar entry) were removed.</summary>
    Removed,

    /// <summary>No store write: the write mutex could not be acquired within the bounded wait (non-strict <see cref="WriteGate"/> timeout).</summary>
    WriteGateUnavailable,
}

/// <summary>
/// Structured result of one <see cref="TaskOperations"/> call. Carries enough
/// for a caller to decide exit code / `--json` shape (phase 06/08's job, not
/// this type's) and a human-readable <see cref="Reason"/> suitable for the
/// FAIL-3 durable log. <see cref="View"/> is the resulting task view on a
/// successful write (or the pre-write view for a refusal, where available),
/// <see langword="null"/> when there's nothing to show (e.g. an unknown
/// handle).
/// </summary>
public sealed record OperationOutcome(OutcomeKind Kind, string Handle, string Reason, AppTaskView? View = null, bool IconChanged = false)
{
    /// <summary><see langword="true"/> for every kind that represents a completed write (accepted, unsafe-accepted, resurrected, removed).</summary>
    public bool Success => Kind is OutcomeKind.Accepted or OutcomeKind.AcceptedUnsafe or OutcomeKind.Resurrected or OutcomeKind.Removed;
}

/// <summary>
/// The phase-05 operation core, now (phase 15) reduced to the surface that
/// survives the ERGO-31 v2 surface migration: <c>remove</c>, the
/// identity-global <c>list</c>/<c>clear</c> queries, and the `run` wrapper's
/// two special-shaped writes (<see cref="ReplaceSteps"/>/<see cref="TouchKeepAlive"/>).
/// The v1 lifecycle verbs this type used to own (<c>start</c>/<c>step</c>/
/// <c>state</c>/<c>done</c>/<c>fail</c>/<c>attention</c>) are RETIRED --
/// their claim-semantics successors (<c>working</c>/<c>activity</c>/
/// <c>blocked</c>/<c>ready</c>/<c>broken</c>) now live in
/// <see cref="Atv.Semantics.SemanticEngine"/>, which composes THIS type only
/// for <c>session-ended --reason finished</c> (== <see cref="Remove"/>).
///
/// Every write here still runs WriteGate -&gt; reconcile -&gt; miss-path check
/// (where applicable) -&gt; validate -&gt; store write -&gt; sidecar stamp, all
/// inside exactly one <see cref="WriteGate.TryRun{T}"/> call per public
/// method (the phase-05 AC7 shape, still upheld). <see cref="RunUpdateClassVerb"/>
/// is the one piece of shared v1-era machinery that survives, because
/// <see cref="ReplaceSteps"/>/<see cref="TouchKeepAlive"/> still need its
/// per-handle-resolve-then-miss-path-resurrect shape.
/// </summary>
public sealed class TaskOperations
{
    private readonly IAppTaskStore _store;
    private readonly SidecarStore _sidecar;
    private readonly RecycleBin _recycleBin;
    private readonly WriteGate _writeGate;
    private readonly TimeSpan _recycleBinTtl;
    private readonly Action<string> _log;
    private readonly IconService? _icons;

    public TaskOperations(
        IAppTaskStore store,
        SidecarStore sidecar,
        RecycleBin recycleBin,
        WriteGate writeGate,
        TimeSpan recycleBinTtl,
        Action<string>? log = null,
        IconService? icons = null)
    {
        _store = store;
        _sidecar = sidecar;
        _recycleBin = recycleBin;
        _writeGate = writeGate;
        _recycleBinTtl = recycleBinTtl;
        _log = log ?? (_ => { });
        _icons = icons;
    }

    // ---- remove ---------------------------------------------------------------

    /// <summary>Removes the live task and its sidecar entry. No resurrection consultation (remove is not one of LIFE-15/21's update-class verbs) -- a handle that isn't live is a clean no-op.</summary>
    public OperationOutcome Remove(string handle, DateTimeOffset now)
    {
        ValidateHandle(handle);
        OperationOutcome? outcome = null;
        bool ran = _writeGate.TryRun(() => outcome = RemoveCore(handle));
        return ran ? outcome! : GateUnavailable(handle);
    }

    private OperationOutcome RemoveCore(string handle)
    {
        // Full pass (phase-04 scoping: start/remove use ReconcileAll).
        ReapDroppedIcons(Reconciler.ReconcileAll(_store, _sidecar));
        var entry = _sidecar.Read(handle);
        if (entry is null)
        {
            string reason = "Handle is not live -- nothing to remove.";
            Log($"{handle}: {reason}");
            return new OperationOutcome(OutcomeKind.UnknownHandleNoOp, handle, reason);
        }

        // ERGO-31 §5's cascade -- BEFORE removing the parent itself, so a
        // mid-cascade failure never leaves the parent gone with orphaned
        // children still live. A no-op for a handle with no children (including
        // when `handle` is ITSELF a child -- children have no children of their
        // own, so `remove <child-handle>` targets exactly that one card, per AC6).
        // Known non-blocking gap (15B review): a direct `remove <child-handle>`
        // bypasses SemanticEngine.ClaimAgentStopped entirely, so the PARENT's
        // EngineMemory.CardedAgentLoci/ActiveAgentLoci never learns the locus is
        // gone -- narrow, out-of-band-only desync (the card itself is still
        // correctly gone); not fixed here.
        CascadeRemoveChildren(handle);

        _store.Remove(entry.Id);
        _sidecar.Delete(handle);
        _icons?.ReapLiveIcon(handle);
        Log($"{handle}: removed (id {entry.Id}).");
        return new OperationOutcome(OutcomeKind.Removed, handle, "Removed.");
    }

    /// <summary>
    /// ERGO-31 §5's cascade: removes every still-live CHILD card minted under
    /// <paramref name="parentHandle"/> (handle <c>&lt;parent&gt;#&lt;agentId&gt;</c>,
    /// identified purely via <see cref="EngineMemory.ParentHandle"/> --
    /// <see cref="SidecarStore.ReadChildrenOf"/>). Shared by <see cref="Remove"/>
    /// above and, via composition, <c>Atv.Semantics.SemanticEngine</c>'s
    /// <c>session-ended --reason error</c> path (which also cascades -- a
    /// session that's over takes its fanned-out workers with it either way).
    /// Callers MUST already be running inside their own <see cref="WriteGate"/>
    /// critical section; this does not acquire one of its own (same contract
    /// as <see cref="ReapDroppedIcons"/> and every other private write helper
    /// in this file).
    /// </summary>
    public void CascadeRemoveChildren(string parentHandle)
    {
        foreach (var (childHandle, childEntry) in _sidecar.ReadChildrenOf(parentHandle))
        {
            _store.Remove(childEntry.Id);
            _sidecar.Delete(childHandle);
            _icons?.ReapLiveIcon(childHandle);
            Log($"{parentHandle}: cascaded removal to child '{childHandle}' (id {childEntry.Id}).");
        }
    }

    /// <summary>
    /// Reaps the live per-handle icon for every entry a full
    /// <see cref="Reconciler.ReconcileAll"/> pass dropped (rule 2, our own
    /// stale mapping) or swept (rule 3, the ERGO-2 hidden sweep) -- the
    /// "entry-drop/reconciliation reap" wiring point ERGO-23 calls for.
    /// <see cref="Reconciler"/> itself stays icon-unaware (its own contract
    /// is unchanged from phase 04): this reacts to the summary it already
    /// returns rather than threading an icons collaborator through it.
    /// </summary>
    private void ReapDroppedIcons(ReconcileSummary summary)
    {
        if (_icons is null) return;
        foreach (var r in summary.DroppedStaleEntries) _icons.ReapLiveIcon(r.Handle);
        foreach (var r in summary.SweptHidden) _icons.ReapLiveIcon(r.Handle);
    }

    // ---- list (identity-global enumeration; lock-free, no WriteGate) --------

    /// <summary>
    /// One row of <see cref="List"/>'s identity-global truth (ERGO-16):
    /// <see cref="Handle"/>/<see cref="LastUpdate"/> are populated only via a
    /// matching sidecar entry -- both <see langword="null"/> for an
    /// ENTRYLESS live task (a task the platform knows about that this
    /// identity's sidecar never recorded a handle for), which is still
    /// included rather than filtered out.
    /// </summary>
    public sealed record TaskListEntry(string? Handle, string Title, string ExecutingStep, AppTaskState State, DateTimeOffset? LastUpdate);

    /// <summary>
    /// Every live task for this identity (<see cref="IAppTaskStore.FindAll"/>),
    /// correlated against the sidecar index. Deliberately lock-free -- no
    /// <see cref="WriteGate.TryRun{T}"/> acquisition -- matching
    /// <see cref="SidecarStore.Write"/>'s own remarks that `list`/the
    /// watchdog read the sidecar OUTSIDE the WriteGate; a snapshot read
    /// racing a concurrent writer is harmless here (worst case, one row is a
    /// write stale), and matches ERGO-16's "identity-global truth" -- this
    /// never sweeps or mutates anything, just reports what is there.
    /// </summary>
    public IReadOnlyList<TaskListEntry> List()
    {
        var handleById = new Dictionary<string, (string Handle, SidecarEntry Entry)>(StringComparer.Ordinal);
        foreach (var (handle, entry) in _sidecar.ReadAll())
            handleById[entry.Id] = (handle, entry);

        return [.. _store.FindAll().Select(t => handleById.TryGetValue(t.Id, out var mapped)
            ? new TaskListEntry(mapped.Handle, t.Title, t.ExecutingStep, t.State, mapped.Entry.LastUpdate)
            : new TaskListEntry(null, t.Title, t.ExecutingStep, t.State, null))];
    }

    // ---- clear (bulk purge; ERGO-27 C4) --------------------------------------

    /// <summary>Result of a <see cref="ClearAll"/> pass. <see cref="GateAcquired"/> is <see langword="false"/> only on a non-strict WriteGate timeout (FAIL-1) -- both counts are then meaningless zeros, nothing was touched.</summary>
    public sealed record ClearSummary(bool GateAcquired, int TasksRemoved, int RecycleRecordsRemoved);

    /// <summary>
    /// Purges EVERY active handle for this identity immediately (ERGO-27 C4,
    /// amending ERGO-16): every live task (<see cref="IAppTaskStore.Remove"/>),
    /// its sidecar entry, and its per-handle icon -- including ENTRYLESS
    /// tasks (removed from the store even though there is no handle/icon to
    /// reap for them, since `clear` is identity-global, not scoped to this
    /// process's own sidecar-tracked handles). Default scope EXCLUDES the
    /// recycle bin; <paramref name="includeRecycleBin"/> additionally wipes
    /// it (tombstone records + their co-located icon copies, via
    /// <see cref="RecycleBin.WipeAll"/>). One <see cref="WriteGate.TryRun{T}"/>
    /// call for the whole reconcile-then-act cycle (plan/README.md standing
    /// invariant #5). The canonical icon render-once cache is NEVER touched
    /// here -- a pure regenerable accelerator (ERGO-23).
    /// </summary>
    public ClearSummary ClearAll(bool includeRecycleBin)
    {
        ClearSummary? summary = null;
        bool ran = _writeGate.TryRun(() => summary = ClearAllCore(includeRecycleBin));
        if (ran) return summary!;

        Log("clear: could not acquire the write mutex within the bounded wait; skipped non-disruptively.");
        return new ClearSummary(GateAcquired: false, TasksRemoved: 0, RecycleRecordsRemoved: 0);
    }

    private ClearSummary ClearAllCore(bool includeRecycleBin)
    {
        ReapDroppedIcons(Reconciler.ReconcileAll(_store, _sidecar));

        var handleById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (handle, entry) in _sidecar.ReadAll())
            handleById[entry.Id] = handle;

        var live = _store.FindAll();
        foreach (var task in live)
        {
            _store.Remove(task.Id);
            if (handleById.TryGetValue(task.Id, out string? handle))
            {
                _sidecar.Delete(handle);
                _icons?.ReapLiveIcon(handle);
            }
        }

        int recycleRemoved = includeRecycleBin ? _recycleBin.WipeAll() : 0;

        Log($"clear: removed {live.Count} live task(s){(includeRecycleBin ? $"; wiped {recycleRemoved} recycle-bin file(s)" : "")}.");
        return new ClearSummary(GateAcquired: true, live.Count, recycleRemoved);
    }

    // ---- run wrapper support (phase 11) --------------------------------------

    /// <summary>
    /// The `run` wrapper's step-stream write: replaces the WHOLE step list
    /// with <paramref name="lines"/> wholesale (last line -&gt; executingStep,
    /// everything before it -&gt; completedSteps) -- NOT the ERGO-8 "advance"
    /// model <c>Atv.Semantics.SemanticEngine.Activity</c> uses. The wrapper owns its rolling buffer
    /// exclusively (ERGO-5: "no read-back"), so there is nothing to archive;
    /// each call is a full, idempotent snapshot of "the last N lines as of
    /// now". Reuses the same resolve -&gt; validate -&gt; write -&gt; sidecar-stamp
    /// pipeline every other update-class verb uses (<see cref="RunUpdateClassVerb"/>),
    /// including its miss-path recycle-bin consultation -- defensive only,
    /// since the wrapper's minted handle is never expected to collide with a
    /// recycle-bin record in practice. Always <see cref="AppTaskState.Running"/>
    /// (the card is Running for the wrapper's whole lifetime; <c>Done</c>/<c>Fail</c>
    /// are separate, explicit terminal calls) -- (SequenceOfSteps, Running) is
    /// an unconditionally-safe cell (<see cref="SafeCombinationMatrix"/>), so
    /// <paramref name="unsafeBypass"/> is exposed only for uniformity/defensiveness.
    /// </summary>
    public OperationOutcome ReplaceSteps(string handle, IReadOnlyList<string> lines, DateTimeOffset now, bool unsafeBypass = false)
    {
        return RunUpdateClassVerb(handle, now, unsafeBypass,
            onLive: _ => (BuildStepContent(lines), AppTaskState.Running),
            onResurrect: () => (BuildStepContent(lines), AppTaskState.Running));
    }

    /// <summary>
    /// The `run` wrapper's LIFE-22 silent-child keepalive: refreshes the
    /// handle's sidecar <c>lastUpdate</c> WITHOUT touching store content --
    /// no <see cref="IAppTaskStore.Update"/> call at all, only
    /// <see cref="SidecarStore.Write"/>'s own unconditional timestamp stamp.
    /// So the watchdog never reaps an alive-but-quiet child between output
    /// bursts. A clean no-op (no write at all) if the handle isn't currently
    /// live -- there is nothing to keep alive.
    /// </summary>
    public OperationOutcome TouchKeepAlive(string handle, DateTimeOffset now)
    {
        ValidateHandle(handle);
        OperationOutcome? outcome = null;
        bool ran = _writeGate.TryRun(() => outcome = TouchKeepAliveCore(handle, now));
        return ran ? outcome! : GateUnavailable(handle);
    }

    private OperationOutcome TouchKeepAliveCore(string handle, DateTimeOffset now)
    {
        var resolve = Reconciler.ResolveHandle(_store, _sidecar, handle);
        if (resolve.Outcome != ResolveOutcome.Kept)
        {
            string reason = "Handle is not live -- nothing to keep alive.";
            Log($"{handle}: {reason}");
            return new OperationOutcome(OutcomeKind.UnknownHandleNoOp, handle, reason);
        }

        _sidecar.Write(handle, resolve.Id!, now);
        return new OperationOutcome(OutcomeKind.Accepted, handle, "Keepalive: lastUpdate refreshed, no content write.");
    }

    /// <summary>
    /// Maps a rolling-buffer snapshot onto <c>SequenceOfSteps</c>: the last
    /// line is the executing step, everything before it is completedSteps.
    /// An empty buffer (the wrapper hasn't seen any output line yet) uses
    /// the same <see cref="AdvanceModel.NoStepsYetPlaceholder"/> baseline
    /// every other "nothing to archive yet" path in this file uses -- the
    /// real platform throws E_INVALIDARG for an empty executingStep
    /// (phase-08 discovery).
    /// </summary>
    private static AppTaskContentDto.SequenceOfSteps BuildStepContent(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return new AppTaskContentDto.SequenceOfSteps([], AdvanceModel.NoStepsYetPlaceholder);

        List<string> completed = lines.Count > 1 ? [.. lines.Take(lines.Count - 1)] : [];
        return new AppTaskContentDto.SequenceOfSteps(completed, lines[^1]);
    }

    // ---- shared update-class pipeline (now only ReplaceSteps/TouchKeepAlive) -----

    /// <summary>
    /// The shared pipeline the v1 update-class verbs (step/state/done/fail/
    /// attention -- retired, phase 15) used to share, now used only by
    /// <see cref="ReplaceSteps"/>/<see cref="TouchKeepAlive"/>: per-handle
    /// resolve (never <see cref="Reconciler.ReconcileAll"/> -- ERGO-19,
    /// "update never sweeps") -&gt; on a live handle, build + validate + write
    /// via <paramref name="onLive"/>; on a miss, consult the recycle bin
    /// (LIFE-15/21) and, if found, re-create + build + validate + write via
    /// <paramref name="onResurrect"/>; otherwise a clean unknown-handle
    /// no-op. Exactly one <see cref="WriteGate.TryRun{T}"/> call wraps the
    /// whole thing (AC7).
    /// </summary>
    private OperationOutcome RunUpdateClassVerb(
        string handle,
        DateTimeOffset now,
        bool unsafeBypass,
        Func<AppTaskView, (AppTaskContentDto Content, AppTaskState State)> onLive,
        Func<(AppTaskContentDto Content, AppTaskState State)> onResurrect)
    {
        ValidateHandle(handle);
        OperationOutcome? outcome = null;
        bool ran = _writeGate.TryRun(() =>
            outcome = RunUpdateClassVerbCore(handle, now, unsafeBypass, onLive, onResurrect));
        return ran ? outcome! : GateUnavailable(handle);
    }

    private OperationOutcome RunUpdateClassVerbCore(
        string handle,
        DateTimeOffset now,
        bool unsafeBypass,
        Func<AppTaskView, (AppTaskContentDto Content, AppTaskState State)> onLive,
        Func<(AppTaskContentDto Content, AppTaskState State)> onResurrect)
    {
        var resolve = Reconciler.ResolveHandle(_store, _sidecar, handle);
        if (resolve.Outcome == ResolveOutcome.Kept)
        {
            var view = _store.Find(resolve.Id!)
                ?? throw new InvalidOperationException(
                    $"Reconciler.ResolveHandle reported Kept for '{handle}' but Find(id) returned null.");

            var (content, state) = onLive(view);
            var validation = Validator.Validate(content, state, unsafeBypass);
            if (!validation.Allowed)
            {
                LogRefusal(handle, validation.Reason);
                return new OperationOutcome(OutcomeKind.RefusedUnsafeCombo, handle, validation.Reason, view);
            }

            _store.Update(resolve.Id!, state, content);
            _sidecar.Write(handle, resolve.Id!, now);
            var updated = _store.Find(resolve.Id!)!;
            var kind = validation.Outcome == ValidationOutcome.UnsafeBypassed ? OutcomeKind.AcceptedUnsafe : OutcomeKind.Accepted;
            return new OperationOutcome(kind, handle, validation.Reason, updated);
        }

        // resolve.Outcome is Unknown or Dropped -- miss path: consult the recycle bin (read ONLY here).
        if (resolve.Outcome == ResolveOutcome.Dropped)
            _icons?.ReapLiveIcon(handle); // rule 2's entry-drop reap (the per-handle-resolve counterpart to ReapDroppedIcons above)

        var record = _recycleBin.TryResurrect(handle, now, _recycleBinTtl);
        if (record is null)
        {
            string reason = "Handle is not live and not in the recycle bin (never seen, or past TTL).";
            Log($"{handle}: {reason}");
            return new OperationOutcome(OutcomeKind.UnknownHandleNoOp, handle, reason);
        }

        var (freshContent, freshState) = onResurrect();
        var freshValidation = Validator.Validate(freshContent, freshState, unsafeBypass);
        if (!freshValidation.Allowed)
        {
            LogRefusal(handle, freshValidation.Reason);
            return new OperationOutcome(OutcomeKind.RefusedUnsafeCombo, handle, freshValidation.Reason);
        }

        // Baseline re-create always lands Running + bare SequenceOfSteps (Create has no state
        // parameter) -- "steps/state restart fresh" (LIFE-21). The verb's real target is then
        // layered on with a follow-up Update, itself already validated above.
        var baseline = new AppTaskContentDto.SequenceOfSteps([], AdvanceModel.NoStepsYetPlaceholder);
        var recreated = Resurrection.RecreateFromRecord(_store, record, baseline, _icons);
        _store.Update(recreated.Id, freshState, freshContent);
        var finalView = _store.Find(recreated.Id)!;

        _sidecar.Write(handle, recreated.Id, now);
        _recycleBin.Remove(handle);

        string resurrectReason = "Resurrected from the recycle bin; re-created with stored core info, steps/state restarted fresh.";
        Log($"{handle}: {resurrectReason} (tombstoned {record.WhenTombstoned:O}; new id {recreated.Id})");
        return new OperationOutcome(OutcomeKind.Resurrected, handle, resurrectReason, finalView);
    }

    // ---- shared helpers -------------------------------------------------------

    private static void ValidateHandle(string handle)
    {
        if (string.IsNullOrEmpty(handle))
            throw new ArgumentException("A caller-supplied handle is required (ERGO-27 C3) -- this is a contract violation of the operations layer, not a runtime outcome; the CLI (phase 08) must never call in without one.", nameof(handle));
    }

    private OperationOutcome GateUnavailable(string handle)
    {
        string reason = "Could not acquire the write mutex within the bounded wait; skipped non-disruptively.";
        Log($"{handle}: {reason}");
        return new OperationOutcome(OutcomeKind.WriteGateUnavailable, handle, reason);
    }

    private void LogRefusal(string handle, string reason) => Log($"{handle}: refused -- {reason}");

    private void Log(string message) => _log(message);
}
