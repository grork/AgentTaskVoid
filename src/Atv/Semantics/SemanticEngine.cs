using Atv.Icons;
using Atv.Operations;
using Atv.Persistence;
using Atv.Store;

namespace Atv.Semantics;

/// <summary>
/// ERGO-31/LIFE-24's engine: the eight v2 semantic verbs. Every verb except
/// <see cref="SessionEnded"/> is an idempotent CLAIM that UPSERTS the card
/// (the first semantic verb creates it -- no session-start verb) and, by
/// construction, only ever emits a <see cref="SafeCombinationMatrix"/> safe
/// (content, state) cell -- see each <c>Claim*</c> method's own remarks for
/// why. Reuses the phase-05 primitives (<see cref="Validator"/>,
/// <see cref="AdvanceModel"/>, <see cref="Reconciler"/>) and composes
/// <see cref="TaskOperations"/> for the one thing it fully overlaps with
/// (<c>session-ended --reason finished</c> == <see cref="TaskOperations.Remove"/>).
///
/// Engine memory (goal / blocked-on loci / known agent loci -- LIFE-24: "if
/// it needs memory or a clock, it is engine") lives in the sidecar's
/// <see cref="EngineMemory"/> (<see cref="SidecarStore.WriteWithMemory"/>).
/// <see cref="SemanticState"/> itself is never persisted here -- every claim
/// reads the card's CURRENT state off the live <see cref="AppTaskView"/> it
/// just resolved, so there is exactly one source of truth.
///
/// Unlike the retired v1 update-class verbs (ERGO-19 "update never sweeps"),
/// every verb here resolves its handle via the PER-HANDLE
/// <see cref="Reconciler.ResolveHandle"/>-shaped rules (never a full
/// <see cref="Reconciler.ReconcileAll"/> sweep) even on a create -- a
/// deliberate 15A call: every semantic verb can now create on first write
/// (unlike only v1 <c>start</c>), and a translator calls the high-frequency
/// ones (<c>activity</c> especially) once per tool call, so paying for a
/// full identity-wide <c>FindAll()</c>-based sweep on every single call would
/// regress ERGO-19's own perf intent. <c>remove</c>/<c>clear</c> (unchanged,
/// still on <see cref="TaskOperations"/>) remain the sweep-triggering paths.
///
/// 15A note: does NOT implement Ready→Idle presence-gated decay (a later
/// phase's clock -- see <see cref="SemanticState.Idle"/>'s remarks) or
/// multi-card fan-out (child-card minting/handle addressing/cascade).
/// <see cref="AgentStarted"/>/<see cref="AgentStopped"/> only do the
/// non-fan-out-shaped bookkeeping available without child cards: locus
/// registration (<see cref="EngineMemory.ActiveAgentLoci"/>, an on-ramp for a
/// later "mint at the 2nd concurrent start" decision) and same-locus
/// block-clearing (LIFE-24: "...its <c>agent-stopped</c>...").
/// </summary>
public sealed class SemanticEngine
{
    private readonly IAppTaskStore _store;
    private readonly SidecarStore _sidecar;
    private readonly RecycleBin _recycleBin;
    private readonly WriteGate _writeGate;
    private readonly TimeSpan _recycleBinTtl;
    private readonly TaskOperations _ops;
    private readonly IconService? _icons;
    private readonly Action<string> _log;

    public SemanticEngine(
        IAppTaskStore store,
        SidecarStore sidecar,
        RecycleBin recycleBin,
        WriteGate writeGate,
        TimeSpan recycleBinTtl,
        TaskOperations ops,
        IconService? icons = null,
        Action<string>? log = null)
    {
        _store = store;
        _sidecar = sidecar;
        _recycleBin = recycleBin;
        _writeGate = writeGate;
        _recycleBinTtl = recycleBinTtl;
        _ops = ops;
        _icons = icons;
        _log = log ?? (_ => { });
    }

    // ==== shared claim shapes ================================================

    /// <summary>What a <c>Claim*</c> method sees: the card's live view if one already exists (<see langword="null"/> for a brand-new or about-to-be-resurrected handle -- in which case <see cref="CurrentSteps"/> reports the fresh baseline) and its existing engine memory (<see cref="Atv.Persistence.EngineMemory.Empty"/> for a fresh/resurrected handle -- engine memory restarts fresh exactly like steps/state do, LIFE-21 precedent).</summary>
    private readonly record struct ClaimContext(AppTaskView? Live, EngineMemory Memory);

    /// <summary>
    /// What a <c>Claim*</c> method returns: <see cref="Content"/>/<see cref="State"/>
    /// are BOTH <see langword="null"/> together, meaning "no card content/state
    /// change" (pure engine-memory bookkeeping -- <see cref="AgentStarted"/>'s
    /// only shape, and <see cref="AgentStopped"/>'s shape when the stopped
    /// locus wasn't blocking anything), or BOTH non-null together, the claim's
    /// real (content, state) pair. <see cref="Memory"/> is always set --
    /// every claim, even a pure no-op one, gets to update engine memory.
    /// </summary>
    private sealed record ClaimResult(AppTaskContentDto? Content, AppTaskState? State, EngineMemory Memory)
    {
        public bool HasContentClaim => Content is not null;
    }

    // ==== working ============================================================

    /// <summary>ERGO-31 §1's <c>working</c> row: sets the turn's goal (altitude 2). Absent <paramref name="goal"/> makes no content claim (idempotent) but still lands the card in Working, clearing any pending Blocked -- "a new prompt... means the block resolved" (LIFE-24).</summary>
    public OperationOutcome Working(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, string? goal, DateTimeOffset now, bool unsafeBypass = false)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, ctx => ClaimWorking(ctx, goal));

    private static ClaimResult ClaimWorking(ClaimContext ctx, string? goal)
    {
        var (completed, executing) = CurrentSteps(ctx);
        if (goal is not null)
        {
            string line = Normalizer.Normalize(goal, FieldBudgets.Goal);
            (completed, executing) = Advance(completed, executing, line);
        }

        var memory = ctx.Memory with { Goal = goal ?? ctx.Memory.Goal, BlockedLoci = [] };
        return new ClaimResult(new AppTaskContentDto.SequenceOfSteps(completed, executing), AppTaskState.Running, memory);
    }

    // ==== activity ===========================================================

    /// <summary>ERGO-31 §1's <c>activity</c> row: the current activity line (altitude 3). Clears the locus attributed to <paramref name="agentId"/> (or the parent locus if absent) -- AC3's "activity against a Blocked card drops the question and re-enters Working" for the single-locus case; when OTHER loci remain blocked (LIFE-24's concurrent-block case), the card stays Blocked showing the latest remaining question instead.</summary>
    public OperationOutcome Activity(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, ActivityKind kind, string? label, string? agentId, string? name, DateTimeOffset now, bool unsafeBypass = false)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, ctx => ClaimActivity(ctx, kind, label, agentId, name));

    private static ClaimResult ClaimActivity(ClaimContext ctx, ActivityKind kind, string? label, string? agentId, string? name)
    {
        var (completed, executing) = CurrentSteps(ctx);
        string line = Rendering.BuildActivityLine(kind, label, name);
        (completed, executing) = Advance(completed, executing, line);

        var remaining = RemoveLocus(ctx.Memory.BlockedLoci, agentId);
        return ProjectAfterLocusChange(completed, executing, ctx.Memory with { BlockedLoci = remaining });
    }

    // ==== blocked ============================================================

    /// <summary>ERGO-31 §1's <c>blocked</c> row: platform-enforced literal question (ERGO-10: <c>NeedsAttention</c> requires <c>SetQuestion</c>). Records/refreshes <paramref name="agentId"/>'s locus (or the parent locus if absent) and always DISPLAYS the latest raised question -- LIFE-24's concurrent-block rule.</summary>
    public OperationOutcome Blocked(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, string question, string? agentId, DateTimeOffset now, bool unsafeBypass = false)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, ctx => ClaimBlocked(ctx, question, agentId, now));

    private static ClaimResult ClaimBlocked(ClaimContext ctx, string question, string? agentId, DateTimeOffset now)
    {
        var (completed, executing) = CurrentSteps(ctx);
        string normalizedQuestion = Normalizer.Normalize(question, FieldBudgets.Question);

        var remaining = RemoveLocus(ctx.Memory.BlockedLoci, agentId);
        List<BlockedLocus> updated = [.. remaining, new BlockedLocus(agentId, normalizedQuestion, now)];

        BlockedLocus latest = LatestLocus(updated);
        var content = (AppTaskContentDto)(new AppTaskContentDto.SequenceOfSteps(completed, executing) { Question = latest.Question });
        return new ClaimResult(content, AppTaskState.NeedsAttention, ctx.Memory with { BlockedLoci = updated });
    }

    // ==== ready ==============================================================

    /// <summary>ERGO-31 §1's <c>ready</c> row: bare preserves the current step content; <paramref name="summary"/> swaps to a <c>TextSummaryResult</c>. A turn-end event -- clears EVERY pending blocked locus (LIFE-24: turn-end events are never <c>--agent</c>-scoped).</summary>
    public OperationOutcome Ready(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, string? summary, DateTimeOffset now, bool unsafeBypass = false)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, ctx => ClaimReady(ctx, summary));

    private static ClaimResult ClaimReady(ClaimContext ctx, string? summary)
    {
        var (completed, executing) = CurrentSteps(ctx);
        AppTaskContentDto content = summary is not null
            ? new AppTaskContentDto.TextSummaryResult(Normalizer.Normalize(summary, FieldBudgets.Summary))
            : new AppTaskContentDto.SequenceOfSteps(completed, executing);

        return new ClaimResult(content, AppTaskState.Completed, ctx.Memory with { BlockedLoci = [] });
    }

    // ==== broken =============================================================

    /// <summary>ERGO-31 §1/§3's <c>broken</c> row: ALWAYS a <c>TextSummaryResult</c> of the rendered reason (+ optional <paramref name="detail"/>) -- "CreateTextSummaryResult under Error renders fully with no question attached" (ERGO-31). A turn-end event -- clears every pending blocked locus.</summary>
    public OperationOutcome Broken(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, BrokenReasonToken reason, string? detail, DateTimeOffset now, bool unsafeBypass = false)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, ctx => ClaimBroken(ctx, reason, detail));

    private static ClaimResult ClaimBroken(ClaimContext ctx, BrokenReasonToken reason, string? detail)
    {
        string text = BrokenReasons.Render(reason);
        if (detail is { Length: > 0 })
            text = $"{text}: {Normalizer.Normalize(detail, FieldBudgets.Summary)}";

        return new ClaimResult(new AppTaskContentDto.TextSummaryResult(text), AppTaskState.Error, ctx.Memory with { BlockedLoci = [] });
    }

    // ==== agent-started / agent-stopped (15A: bookkeeping only, no fan-out) ===

    /// <summary>ERGO-31 §1's <c>agent-started</c> row: registers a child locus. 15A: no child card is minted (15B's job) -- pure <see cref="EngineMemory.ActiveAgentLoci"/> bookkeeping, never touches the card's content/state (the table's target-state column is blank).</summary>
    public OperationOutcome AgentStarted(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, string? agentId, string? name, DateTimeOffset now, bool unsafeBypass = false)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, ctx => ClaimAgentStarted(ctx, agentId));

    private static ClaimResult ClaimAgentStarted(ClaimContext ctx, string? agentId)
    {
        var active = agentId is { Length: > 0 } && !ctx.Memory.ActiveAgentLoci.Contains(agentId)
            ? (IReadOnlyList<string>)[.. ctx.Memory.ActiveAgentLoci, agentId]
            : ctx.Memory.ActiveAgentLoci;

        return new ClaimResult(null, null, ctx.Memory with { ActiveAgentLoci = active });
    }

    /// <summary>ERGO-31 §1's <c>agent-stopped</c> row: retires the child locus (fan-in). LIFE-24: agent-stopped is one of the same-locus block-clearing trigger events -- if <paramref name="agentId"/> WAS a pending blocked locus, clearing it may re-project the card (Working, if it was the last one; otherwise still Blocked showing the next-latest question). If it wasn't blocking anything, this is pure bookkeeping (no content/state touch at all).</summary>
    public OperationOutcome AgentStopped(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, string? agentId, DateTimeOffset now, bool unsafeBypass = false)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, ctx => ClaimAgentStopped(ctx, agentId));

    private static ClaimResult ClaimAgentStopped(ClaimContext ctx, string? agentId)
    {
        var active = (IReadOnlyList<string>)[.. ctx.Memory.ActiveAgentLoci.Where(a => a != agentId)];
        bool wasBlocked = ctx.Memory.BlockedLoci.Any(l => l.AgentId == agentId);
        var remaining = RemoveLocus(ctx.Memory.BlockedLoci, agentId);
        var memory = ctx.Memory with { ActiveAgentLoci = active, BlockedLoci = remaining };

        if (!wasBlocked)
            return new ClaimResult(null, null, memory);

        var (completed, executing) = CurrentSteps(ctx);
        return ProjectAfterLocusChange(completed, executing, memory);
    }

    // ==== session-ended (no upsert, no identity flags -- ERGO-31 §1 intro) ===

    /// <summary>
    /// ERGO-31 §1's <c>session-ended</c> row: the ONE verb that does NOT
    /// accept identity flags and does NOT upsert -- it only acts on an
    /// ALREADY-live handle (nothing to end on a handle with no card).
    /// <see cref="SessionEndedReasonToken.Finished"/> delegates straight to
    /// <see cref="TaskOperations.Remove"/> (own single <see cref="WriteGate"/>
    /// acquisition, no nesting); <see cref="SessionEndedReasonToken.Error"/>
    /// projects Broken with a fixed phrase (no reason token/detail carried by
    /// this verb) and clears engine memory (turn is over).
    /// </summary>
    public OperationOutcome SessionEnded(string handle, SessionEndedReasonToken reason, DateTimeOffset now)
    {
        if (reason == SessionEndedReasonToken.Finished)
            return _ops.Remove(handle, now);

        OperationOutcome? outcome = null;
        bool ran = _writeGate.TryRun(() => outcome = SessionEndedErrorCore(handle, now));
        return ran ? outcome! : GateUnavailable(handle);
    }

    private OperationOutcome SessionEndedErrorCore(string handle, DateTimeOffset now)
    {
        var (entry, live) = ResolveLive(handle);
        if (entry is null || live is null)
        {
            string reason = "Handle is not live -- nothing to end.";
            Log($"{handle}: {reason}");
            return new OperationOutcome(OutcomeKind.UnknownHandleNoOp, handle, reason);
        }

        // TextSummaryResult + Error + no-question is unconditionally a safe cell
        // (SafeCombinationMatrix) -- validated anyway for uniformity/defensiveness,
        // matching every other write in this codebase.
        var content = new AppTaskContentDto.TextSummaryResult("Session ended in error.");
        var validation = Validator.Validate(content, AppTaskState.Error, bypass: false);
        if (!validation.Allowed)
        {
            LogRefusal(handle, validation.Reason);
            return new OperationOutcome(OutcomeKind.RefusedUnsafeCombo, handle, validation.Reason, live);
        }

        _store.Update(entry.Id, AppTaskState.Error, content);
        _sidecar.WriteWithMemory(handle, entry.Id, now, EngineMemory.Empty);
        var updated = _store.Find(entry.Id)!;
        Log($"{handle}: session ended in error -- Broken.");
        return new OperationOutcome(OutcomeKind.Accepted, handle, "Session ended in error.", updated);
    }

    // ==== the shared upsert-claim pipeline (working/activity/blocked/ready/broken/agent-*) ====

    private OperationOutcome ApplyClaim(
        string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, DateTimeOffset now, bool unsafeBypass,
        Func<ClaimContext, ClaimResult> computeClaim)
    {
        ValidateHandle(handle);
        OperationOutcome? outcome = null;
        bool ran = _writeGate.TryRun(() =>
            outcome = ApplyClaimCore(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, computeClaim));
        return ran ? outcome! : GateUnavailable(handle);
    }

    private OperationOutcome ApplyClaimCore(
        string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, DateTimeOffset now, bool unsafeBypass,
        Func<ClaimContext, ClaimResult> computeClaim)
    {
        var (entry, live) = ResolveLive(handle);

        if (live is not null)
        {
            if (live.IconUri != iconUri)
                return ApplyIconForcedRecreate(handle, entry!.Id, title, subtitle, iconUri, deepLink, now, unsafeBypass, computeClaim, entry.EngineMemory ?? EngineMemory.Empty, live);

            var ctx = new ClaimContext(live, entry!.EngineMemory ?? EngineMemory.Empty);
            var result = computeClaim(ctx);

            if (result.HasContentClaim)
            {
                var validation = Validator.Validate(result.Content!, result.State!.Value, unsafeBypass);
                if (!validation.Allowed)
                {
                    LogRefusal(handle, validation.Reason);
                    return new OperationOutcome(OutcomeKind.RefusedUnsafeCombo, handle, validation.Reason, live);
                }

                ApplyIdentityIfClaimed(entry.Id, title, subtitle, live);
                _store.UpdateDeepLink(entry.Id, deepLink);
                _store.Update(entry.Id, result.State!.Value, result.Content!);
                _sidecar.WriteWithMemory(handle, entry.Id, now, result.Memory);

                var kind = validation.Outcome == ValidationOutcome.UnsafeBypassed ? OutcomeKind.AcceptedUnsafe : OutcomeKind.Accepted;
                return new OperationOutcome(kind, handle, validation.Reason, _store.Find(entry.Id)!);
            }

            // No content/state claim (agent-started, and agent-stopped on a
            // non-blocking locus): a TRUE no-op on the platform -- deliberately
            // does NOT call UpdateTitles/UpdateDeepLink/Update at all. Two reasons:
            // (1) nothing was actually claimed, so there is genuinely nothing to
            // write; (2) if the live card's current content is a TextSummaryResult
            // (Ready-with-summary or Broken), its text is UNREADABLE back off
            // AppTaskView (INFRA-15: the platform has no content readback beyond
            // the two step fields) -- there would be no way to "preserve" it through
            // a real Update call even if we wanted to, so the only sound option is
            // to touch nothing. The already-known `live` view is still accurate to
            // return -- nothing about it changed.
            _sidecar.WriteWithMemory(handle, entry.Id, now, result.Memory);
            return new OperationOutcome(OutcomeKind.Accepted, handle, "No content/state claim -- pure engine-memory bookkeeping.", live);
        }

        // Not live -- consult the recycle bin (miss path, read ONLY here), then build
        // the claim against a FRESH baseline (empty steps, fresh engine memory) BEFORE
        // touching the store, so a refusal here is still zero store writes.
        var record = _recycleBin.TryResurrect(handle, now, _recycleBinTtl);
        var freshCtx = new ClaimContext(Live: null, EngineMemory.Empty);
        var freshResult = computeClaim(freshCtx);

        AppTaskContentDto finalContent;
        AppTaskState finalState;
        if (freshResult.HasContentClaim)
        {
            var validation = Validator.Validate(freshResult.Content!, freshResult.State!.Value, unsafeBypass);
            if (!validation.Allowed)
            {
                LogRefusal(handle, validation.Reason);
                return new OperationOutcome(OutcomeKind.RefusedUnsafeCombo, handle, validation.Reason);
            }
            finalContent = freshResult.Content!;
            finalState = freshResult.State!.Value;
        }
        else
        {
            finalContent = new AppTaskContentDto.SequenceOfSteps([], AdvanceModel.NoStepsYetPlaceholder);
            finalState = AppTaskState.Running;
        }

        if (record is not null)
        {
            // Every v2 verb always carries its own fully-resolved identity/icon (the
            // stateless translator passes them every call) -- unlike v1's update-class
            // resurrection, there is no need to move the OLD recycled icon back; the
            // caller's fresh iconUri (already placed by the caller) wins, and the now-
            // orphaned recycled copy is reaped, mirroring v1 Start's own ERGO-25 path.
            _recycleBin.Remove(handle);
            _icons?.ReapRecycledIcon(handle);
        }

        // Create() tolerates an empty title/subtitle fine (unlike UpdateTitles on an
        // already-live card, see ApplyIdentityIfClaimed's remarks) -- an absent
        // --title/--subtitle on a brand-new handle just creates with "".
        var created = _store.Create(title ?? "", subtitle ?? "", deepLink, iconUri, finalContent);
        if (finalState != AppTaskState.Running)
            _store.Update(created.Id, finalState, finalContent);

        _sidecar.WriteWithMemory(handle, created.Id, now, freshResult.Memory);
        var finalView = _store.Find(created.Id)!;

        if (record is not null)
        {
            string resurrectReason = "Resurrected from the recycle bin; re-created using the fields this call carried.";
            Log($"{handle}: {resurrectReason} (tombstoned {record.WhenTombstoned:O}; new id {created.Id})");
            return new OperationOutcome(OutcomeKind.Resurrected, handle, resurrectReason, finalView);
        }

        return new OperationOutcome(OutcomeKind.Accepted, handle, "Created.", finalView);
    }

    private OperationOutcome ApplyIconForcedRecreate(
        string handle, string oldId, string? title, string? subtitle, Uri iconUri, Uri deepLink, DateTimeOffset now, bool unsafeBypass,
        Func<ClaimContext, ClaimResult> computeClaim, EngineMemory existingMemory, AppTaskView oldLive)
    {
        // Icon is immutable per task (the grouping key) -- a changed icon token forces
        // a platform Remove+Create, losing step history unavoidably (ERGO-25's icon
        // caveat, carried into v2). The claim is computed against a FRESH baseline
        // (step history is about to be lost) and validated BEFORE the old card is
        // removed, so a refusal here leaves the old card untouched.
        var ctx = new ClaimContext(Live: null, existingMemory);
        var result = computeClaim(ctx);

        AppTaskContentDto finalContent;
        AppTaskState finalState;
        if (result.HasContentClaim)
        {
            var validation = Validator.Validate(result.Content!, result.State!.Value, unsafeBypass);
            if (!validation.Allowed)
            {
                LogRefusal(handle, validation.Reason);
                return new OperationOutcome(OutcomeKind.RefusedUnsafeCombo, handle, validation.Reason, oldLive);
            }
            finalContent = result.Content!;
            finalState = result.State!.Value;
        }
        else
        {
            finalContent = new AppTaskContentDto.SequenceOfSteps([], AdvanceModel.NoStepsYetPlaceholder);
            finalState = AppTaskState.Running;
        }

        _store.Remove(oldId);
        // Preserve the old card's title/subtitle across the forced recreate when
        // not explicitly re-claimed this call -- same idempotency rule as the
        // ordinary update path (ApplyIdentityIfClaimed); Create() itself tolerates
        // an empty string fine either way.
        var recreated = _store.Create(title ?? oldLive.Title, subtitle ?? oldLive.Subtitle, deepLink, iconUri, finalContent);
        if (finalState != AppTaskState.Running)
            _store.Update(recreated.Id, finalState, finalContent);

        _sidecar.WriteWithMemory(handle, recreated.Id, now, result.Memory);
        string reason = "Icon token changed -- forced Remove+Create; step history lost.";
        Log($"{handle}: {reason} (old id {oldId}, new id {recreated.Id})");
        return new OperationOutcome(OutcomeKind.Accepted, handle, reason, _store.Find(recreated.Id)!, IconChanged: true);
    }

    // ==== shared helpers ========================================================

    /// <summary>
    /// Applies the identity-flag claim to an EXISTING live card -- idempotent,
    /// like every other optional field: an absent <paramref name="title"/>/
    /// <paramref name="subtitle"/> makes no claim (falls back to whatever
    /// <paramref name="live"/> already has), and <see cref="IAppTaskStore.UpdateTitles"/>
    /// is skipped ENTIRELY when nothing was actually claimed AND when the
    /// effective title would be empty -- empirically, the real platform's
    /// <c>UpdateTitles</c> THROWS ("Title cannot be empty") when called with an
    /// empty title on an already-live card (phase-15 real-adapter discovery;
    /// <c>Create</c> tolerates an empty title fine, this is an Update-only
    /// quirk). A translator that (reasonably) omits <c>--title</c> on every
    /// call after the first must never crash the card it's updating.
    /// </summary>
    private void ApplyIdentityIfClaimed(string id, string? title, string? subtitle, AppTaskView live)
    {
        if (title is null && subtitle is null) return;

        string effectiveTitle = title ?? live.Title;
        string effectiveSubtitle = subtitle ?? live.Subtitle;
        if (effectiveTitle.Length > 0)
            _store.UpdateTitles(id, effectiveTitle, effectiveSubtitle);
    }

    /// <summary>Per-handle resolve (rules 1-2 only, never a full sweep -- see the type-level remarks): reads the sidecar entry, drops it if the store no longer knows the id (our own stale mapping), and returns both.</summary>
    private (SidecarEntry? Entry, AppTaskView? Live) ResolveLive(string handle)
    {
        var entry = _sidecar.Read(handle);
        var live = entry is not null ? _store.Find(entry.Id) : null;
        if (entry is not null && live is null)
        {
            _sidecar.Delete(handle);
            _icons?.ReapLiveIcon(handle);
            entry = null;
        }
        return (entry, live);
    }

    private static (IReadOnlyList<string> Completed, string Executing) CurrentSteps(ClaimContext ctx)
        => ctx.Live is { } live ? (live.CompletedSteps, live.ExecutingStep) : ([], AdvanceModel.NoStepsYetPlaceholder);

    private static (IReadOnlyList<string> Completed, string Executing) Advance(IReadOnlyList<string> completed, string executing, string newLine)
    {
        var advanced = AdvanceModel.Advance(completed, executing, newLine);
        return (advanced.CompletedSteps, advanced.ExecutingStep);
    }

    private static IReadOnlyList<BlockedLocus> RemoveLocus(IReadOnlyList<BlockedLocus> loci, string? agentId)
        => [.. loci.Where(l => l.AgentId != agentId)];

    private static BlockedLocus LatestLocus(IReadOnlyList<BlockedLocus> loci)
        => loci.OrderByDescending(l => l.WhenBlocked).First();

    /// <summary>
    /// AC3's structural guarantee: given the step content already advanced,
    /// project the (content, state) pair consistent with whatever blocked
    /// loci remain in <paramref name="memory"/> -- NONE remaining -&gt;
    /// (SequenceOfSteps, Running, no question), the single-locus AC3 case;
    /// SOME remaining -&gt; (SequenceOfSteps, NeedsAttention, latest question),
    /// LIFE-24's concurrent-block "surface the other" case. Both are safe
    /// cells of <see cref="SafeCombinationMatrix"/> by construction.
    /// </summary>
    private static ClaimResult ProjectAfterLocusChange(IReadOnlyList<string> completed, string executing, EngineMemory memory)
    {
        if (memory.BlockedLoci.Count == 0)
            return new ClaimResult(new AppTaskContentDto.SequenceOfSteps(completed, executing), AppTaskState.Running, memory);

        BlockedLocus latest = LatestLocus(memory.BlockedLoci);
        var content = (AppTaskContentDto)(new AppTaskContentDto.SequenceOfSteps(completed, executing) { Question = latest.Question });
        return new ClaimResult(content, AppTaskState.NeedsAttention, memory);
    }

    private static void ValidateHandle(string handle)
    {
        if (string.IsNullOrEmpty(handle))
            throw new ArgumentException("A caller-supplied handle is required (ERGO-27 C3) -- a contract violation of the engine layer, not a runtime outcome; the CLI must never call in without one.", nameof(handle));
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
