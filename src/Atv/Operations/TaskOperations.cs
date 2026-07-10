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
/// The verb-independent operation core (phase 05): <c>start</c>/<c>step</c>/
/// <c>state</c>/<c>done</c>/<c>fail</c>/<c>attention</c>/<c>remove</c>, each
/// shaped as WriteGate -&gt; reconcile -&gt; miss-path check -&gt; validate -&gt;
/// store write -&gt; sidecar stamp, ALL inside exactly one
/// <see cref="WriteGate.TryRun{T}"/> call per public method (AC7).
///
/// Boundary (plan/phase-05-task-operations.md): this type does not parse
/// arguments, does not know about exit codes or `--json`, and does not
/// trigger sweeps beyond what phase 04 already scoped (a full
/// <see cref="Reconciler.ReconcileAll"/> pass on <c>start</c>/<c>remove</c>
/// only, per ERGO-19 "update never sweeps"). It consumes an already-resolved
/// <c>iconUri</c> on <c>start</c> -- icon rendering is phase 07's job.
///
/// Every content-emitting write in here builds its (content, state) pair,
/// then calls <see cref="Validator.Validate"/> BEFORE touching the store --
/// so a refusal is guaranteed to mean zero store writes, including on the
/// resurrection path (validate happens before the baseline
/// <see cref="Resurrection.RecreateFromRecord"/> call, not after).
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
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sidecar = sidecar ?? throw new ArgumentNullException(nameof(sidecar));
        _recycleBin = recycleBin ?? throw new ArgumentNullException(nameof(recycleBin));
        _writeGate = writeGate ?? throw new ArgumentNullException(nameof(writeGate));
        _recycleBinTtl = recycleBinTtl;
        _log = log ?? (_ => { });
        _icons = icons;
    }

    // ---- start (ERGO-25 upsert; LIFE-15/21 resurrection) -------------------

    /// <summary>
    /// Create-or-adopt (ERGO-25): a live handle is adopted in place (title/
    /// subtitle/deepLink re-applied, state forced to Running,
    /// <c>completedSteps</c>/<c>executingStep</c> PRESERVED unless
    /// <paramref name="reset"/>); a handle absent from the live sidecar but
    /// present in the recycle bin within TTL is resurrected using the fields
    /// THIS call carries (ERGO-25's recycle-bin caveat: "restored core info +
    /// whatever start carries" -- since `start` always carries fully-resolved
    /// title/subtitle/icon/deepLink by the time it reaches this layer, those
    /// values win); a genuinely new handle is a plain create. A DIFFERENT
    /// <paramref name="iconUri"/> than a live card's current one forces a
    /// platform Remove+Create (icon is immutable/the grouping key) -- new Id,
    /// step history lost, unavoidable.
    /// </summary>
    public OperationOutcome Start(
        string handle, string title, string subtitle, Uri iconUri, Uri deepLink,
        DateTimeOffset now, bool reset = false, bool unsafeBypass = false)
    {
        ValidateHandle(handle);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(subtitle);
        ArgumentNullException.ThrowIfNull(iconUri);
        ArgumentNullException.ThrowIfNull(deepLink);

        OperationOutcome? outcome = null;
        bool ran = _writeGate.TryRun(() =>
            outcome = StartCore(handle, title, subtitle, iconUri, deepLink, now, reset, unsafeBypass));
        return ran ? outcome! : GateUnavailable(handle);
    }

    private OperationOutcome StartCore(
        string handle, string title, string subtitle, Uri iconUri, Uri deepLink,
        DateTimeOffset now, bool reset, bool unsafeBypass)
    {
        // Full pass (phase-04 scoping: start/remove use ReconcileAll, never per-handle ResolveHandle).
        ReapDroppedIcons(Reconciler.ReconcileAll(_store, _sidecar));
        var entry = _sidecar.Read(handle);

        if (entry is not null)
        {
            var live = _store.Find(entry.Id)
                ?? throw new InvalidOperationException(
                    $"Reconciliation should guarantee sidecar entry for '{handle}' has a live backing task.");
            return AdoptLive(handle, entry.Id, live, title, subtitle, iconUri, deepLink, now, reset, unsafeBypass);
        }

        // Not live -- consult the recycle bin (miss path, read ONLY here).
        var record = _recycleBin.TryResurrect(handle, now, _recycleBinTtl);

        var content = new AppTaskContentDto.SequenceOfSteps([], AdvanceModel.NoStepsYetPlaceholder);
        var validation = Validator.Validate(content, AppTaskState.Running, unsafeBypass); // always Safe; validated for uniformity/defensiveness
        if (!validation.Allowed)
        {
            LogRefusal(handle, validation.Reason);
            return new OperationOutcome(OutcomeKind.RefusedUnsafeCombo, handle, validation.Reason);
        }

        var created = _store.Create(title, subtitle, deepLink, iconUri, content);
        _sidecar.Write(handle, created.Id, now);

        if (record is not null)
        {
            _recycleBin.Remove(handle);
            // Start always carries its own fully-resolved icon (ERGO-25) --
            // the recycled copy, if any, is now definitively orphaned (never
            // moved back), so reap it here rather than leaving it for the
            // backstop sweep.
            _icons?.ReapRecycledIcon(handle);
            string reason = "Resurrected from the recycle bin; re-created using the fields this start carried.";
            Log($"{handle}: {reason} (tombstoned {record.WhenTombstoned:O}; new id {created.Id})");
            return new OperationOutcome(OutcomeKind.Resurrected, handle, reason, created);
        }

        return new OperationOutcome(OutcomeKind.Accepted, handle, "Created.", created);
    }

    private OperationOutcome AdoptLive(
        string handle, string id, AppTaskView live, string title, string subtitle, Uri iconUri, Uri deepLink,
        DateTimeOffset now, bool reset, bool unsafeBypass)
    {
        if (live.IconUri != iconUri)
        {
            // Icon is immutable per task (it's the grouping key) -- a changed icon
            // token forces a platform Remove+Create, losing step history
            // unavoidably (ERGO-25's icon caveat).
            _store.Remove(id);
            var recreated = _store.Create(title, subtitle, deepLink, iconUri, new AppTaskContentDto.SequenceOfSteps([], AdvanceModel.NoStepsYetPlaceholder));
            _sidecar.Write(handle, recreated.Id, now);
            string reason = "Icon token changed -- forced Remove+Create; step history lost.";
            Log($"{handle}: {reason} (old id {id}, new id {recreated.Id})");
            return new OperationOutcome(OutcomeKind.Accepted, handle, reason, recreated, IconChanged: true);
        }

        var content = reset
            ? new AppTaskContentDto.SequenceOfSteps([], AdvanceModel.NoStepsYetPlaceholder)
            : new AppTaskContentDto.SequenceOfSteps(live.CompletedSteps, live.ExecutingStep);

        var validation = Validator.Validate(content, AppTaskState.Running, unsafeBypass); // always Safe
        if (!validation.Allowed)
        {
            LogRefusal(handle, validation.Reason);
            return new OperationOutcome(OutcomeKind.RefusedUnsafeCombo, handle, validation.Reason, live);
        }

        _store.UpdateTitles(id, title, subtitle);
        _store.UpdateDeepLink(id, deepLink);
        _store.Update(id, AppTaskState.Running, content);
        _sidecar.Write(handle, id, now);

        var updated = _store.Find(id)!;
        string acceptedReason = reset ? "Adopted live handle; --reset cleared step history." : "Adopted live handle; step history preserved.";
        return new OperationOutcome(OutcomeKind.Accepted, handle, acceptedReason, updated);
    }

    // ---- step (ERGO-8 advance model) ---------------------------------------

    /// <summary>Advances the executing step (ERGO-8): archives the previous one into <c>completedSteps</c> (FIFO cap 10), sets the new one. PRESERVES the card's current state -- a NeedsAttention card refuses this (no question in the rebuilt content).</summary>
    public OperationOutcome Step(string handle, string message, DateTimeOffset now, bool unsafeBypass = false)
    {
        ArgumentNullException.ThrowIfNull(message);
        return RunUpdateClassVerb(handle, now, unsafeBypass,
            onLive: view => (AdvanceModel.Advance(view.CompletedSteps, view.ExecutingStep, message), view.State),
            onResurrect: () => (AdvanceModel.Advance([], "", message), AppTaskState.Running));
    }

    // ---- state (ERGO-9/27: running|paused only) ----------------------------

    /// <summary>Sets Running or Paused (ERGO-9/C7) -- any other <see cref="AppTaskState"/> is refused as an invalid argument, no store write. Rebuilds content from the readable steps, dropping any question (so leaving NeedsAttention this way is never stuck).</summary>
    public OperationOutcome SetState(string handle, AppTaskState requestedState, DateTimeOffset now, bool unsafeBypass = false)
    {
        ValidateHandle(handle);
        if (requestedState is not (AppTaskState.Running or AppTaskState.Paused))
        {
            string reason = $"state accepts only Running or Paused (got {requestedState}); done/fail/attention are their own verbs.";
            Log($"{handle}: refused -- {reason}");
            return new OperationOutcome(OutcomeKind.RefusedInvalidArgument, handle, reason);
        }

        return RunUpdateClassVerb(handle, now, unsafeBypass,
            onLive: view => (new AppTaskContentDto.SequenceOfSteps(view.CompletedSteps, view.ExecutingStep), requestedState),
            onResurrect: () => (new AppTaskContentDto.SequenceOfSteps([], AdvanceModel.NoStepsYetPlaceholder), requestedState));
    }

    // ---- done / fail --------------------------------------------------------

    /// <summary>Completed (ERGO-9): bare keeps the current <c>SequenceOfSteps</c>; <paramref name="summary"/> swaps content to <c>TextSummaryResult</c>. Drops any question.</summary>
    public OperationOutcome Done(string handle, DateTimeOffset now, string? summary = null, bool unsafeBypass = false)
        => Finish(handle, AppTaskState.Completed, now, summary, unsafeBypass);

    /// <summary>Same shape options as <see cref="Done"/>, targeting <see cref="AppTaskState.Error"/> (the "fail" verb).</summary>
    public OperationOutcome Fail(string handle, DateTimeOffset now, string? summary = null, bool unsafeBypass = false)
        => Finish(handle, AppTaskState.Error, now, summary, unsafeBypass);

    private OperationOutcome Finish(string handle, AppTaskState endingState, DateTimeOffset now, string? summary, bool unsafeBypass)
        => RunUpdateClassVerb(handle, now, unsafeBypass,
            onLive: view => (BuildFinishContent(summary, view.CompletedSteps, view.ExecutingStep), endingState),
            onResurrect: () => (BuildFinishContent(summary, [], AdvanceModel.NoStepsYetPlaceholder), endingState));

    private static AppTaskContentDto BuildFinishContent(string? summary, IReadOnlyList<string> completedSteps, string executingStep)
        => summary is null
            ? new AppTaskContentDto.SequenceOfSteps(completedSteps, executingStep)
            : new AppTaskContentDto.TextSummaryResult(summary);

    // ---- attention ------------------------------------------------------------

    /// <summary>NeedsAttention + SetQuestion (ERGO-9/10) -- the one documented-safe question cell. Preserves the readable steps underneath the question.</summary>
    public OperationOutcome Attention(string handle, string question, DateTimeOffset now, bool unsafeBypass = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        return RunUpdateClassVerb(handle, now, unsafeBypass,
            onLive: view => ((AppTaskContentDto)(new AppTaskContentDto.SequenceOfSteps(view.CompletedSteps, view.ExecutingStep) { Question = question }), AppTaskState.NeedsAttention),
            onResurrect: () => ((AppTaskContentDto)(new AppTaskContentDto.SequenceOfSteps([], AdvanceModel.NoStepsYetPlaceholder) { Question = question }), AppTaskState.NeedsAttention));
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

        _store.Remove(entry.Id);
        _sidecar.Delete(handle);
        _icons?.ReapLiveIcon(handle);
        Log($"{handle}: removed (id {entry.Id}).");
        return new OperationOutcome(OutcomeKind.Removed, handle, "Removed.");
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

    // ---- shared update-class pipeline (step/state/done/fail/attention) -----

    /// <summary>
    /// The shared pipeline for the five update-class verbs: per-handle
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
