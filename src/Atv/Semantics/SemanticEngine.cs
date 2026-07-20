using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.Operations;
using Codevoid.AgentTaskVoid.Persistence;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.Semantics;

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
    private readonly Func<RepoDiscoveryResult>? _discoverRepo;
    private readonly IReadOnlyDictionary<string, string> _presentationEnv;
    private readonly IReadOnlyDictionary<string, string> _presentationUserFile;
    private readonly IconGroupRegistry? _groupRegistry;

    public SemanticEngine(
        IAppTaskStore store,
        SidecarStore sidecar,
        RecycleBin recycleBin,
        WriteGate writeGate,
        TimeSpan recycleBinTtl,
        TaskOperations ops,
        IconService? icons = null,
        Action<string>? log = null,
        Func<RepoDiscoveryResult>? discoverRepo = null,
        IReadOnlyDictionary<string, string>? presentationEnv = null,
        IReadOnlyDictionary<string, string>? presentationUserFile = null,
        IconGroupRegistry? groupRegistry = null)
    {
        _store = store;
        _sidecar = sidecar;
        _recycleBin = recycleBin;
        _writeGate = writeGate;
        _recycleBinTtl = recycleBinTtl;
        _ops = ops;
        _icons = icons;
        _log = log ?? (_ => { });
        _discoverRepo = discoverRepo;
        _presentationEnv = presentationEnv ?? EmptyStringMap;
        _presentationUserFile = presentationUserFile ?? EmptyStringMap;
        _groupRegistry = groupRegistry;
    }

    private static readonly Dictionary<string, string> EmptyStringMap = new(StringComparer.OrdinalIgnoreCase);

    // ==== shared claim shapes ================================================

    /// <summary>What a <c>Claim*</c> method sees: the card's live view if one already exists (<see langword="null"/> for a brand-new or about-to-be-resurrected handle -- in which case <see cref="CurrentSteps"/> reports the fresh baseline) and its existing engine memory (<see cref="Codevoid.AgentTaskVoid.Persistence.EngineMemory.Empty"/> for a fresh/resurrected handle -- engine memory restarts fresh exactly like steps/state do, LIFE-21 precedent).</summary>
    private readonly record struct ClaimContext(AppTaskView? Live, EngineMemory Memory);

    /// <summary>
    /// What a <c>Claim*</c> method returns: <see cref="Content"/>/<see cref="State"/>
    /// are BOTH <see langword="null"/> together, meaning "no card content/state
    /// change" (pure engine-memory bookkeeping -- <see cref="AgentStarted"/>'s
    /// usual shape, and <see cref="AgentStopped"/>'s shape when the stopped
    /// locus wasn't blocking anything), or BOTH non-null together, the claim's
    /// real (content, state) pair. <see cref="Memory"/> is always set --
    /// every claim, even a pure no-op one, gets to update engine memory.
    ///
    /// 15B fan-out fields (ERGO-31 §5), populated ONLY by
    /// <see cref="ClaimAgentStarted"/>/<see cref="ClaimAgentStopped"/>, empty/
    /// null for every other verb: <see cref="NewlyCardedAgentIds"/> -- agent
    /// ids that just crossed into "carded" this call (the 2nd-concurrent-start
    /// mint, including a retroactive 1st-worker mint in the SAME list) --
    /// consumed by <see cref="ApplyClaimCore"/>'s <c>afterWrite</c> hook to
    /// actually mint each child card; <see cref="RetiredChildAgentId"/> -- the
    /// single agent id (at most one per <c>agent-stopped</c> call) whose
    /// minted child card must now be cascaded away.
    /// </summary>
    private sealed record ClaimResult(
        AppTaskContentDto? Content,
        AppTaskState? State,
        EngineMemory Memory,
        IReadOnlyList<string>? NewlyCardedAgentIds = null,
        string? RetiredChildAgentId = null)
    {
        public bool HasContentClaim => Content is not null;
    }

    // ==== working ============================================================

    /// <summary>ERGO-31 §1's <c>working</c> row: sets the turn's goal (altitude 2). Absent <paramref name="goal"/> makes no content claim (idempotent) but still lands the card in Working, clearing any pending Blocked -- "a new prompt... means the block resolved" (LIFE-24).</summary>
    public OperationOutcome Working(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, string? goal, DateTimeOffset now, bool unsafeBypass = false, IconToken iconToken = default, bool iconExplicit = true)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, ctx => ClaimWorking(ctx, goal), iconToken: iconToken, iconExplicit: iconExplicit);

    private static ClaimResult ClaimWorking(ClaimContext ctx, string? goal)
    {
        var (completed, executing) = CurrentSteps(ctx);
        if (goal is not null)
        {
            string line = Normalizer.Normalize(goal, FieldBudgets.Goal);
            (completed, executing) = Advance(completed, executing, line);
        }

        var memory = ctx.Memory with { Goal = goal ?? ctx.Memory.Goal, BlockedLoci = [], ReadyDecay = null, LastSummary = null };
        return new ClaimResult(new AppTaskContentDto.SequenceOfSteps(completed, executing), AppTaskState.Running, memory);
    }

    // ==== activity ===========================================================

    /// <summary>
    /// ERGO-31 §1's <c>activity</c> row: the current activity line (altitude
    /// 3). Clears the locus attributed to <paramref name="agentId"/> (or the
    /// parent locus if absent) -- AC3's "activity against a Blocked card
    /// drops the question and re-enters Working" for the single-locus case;
    /// when OTHER loci remain blocked (LIFE-24's concurrent-block case), the
    /// card stays Blocked showing the latest remaining question instead.
    ///
    /// Phase 19A (ERGO-31 §5's redirect): when <paramref name="agentId"/>
    /// names an already-carded child of <paramref name="handle"/>
    /// (<see cref="EngineMemory.CardedAgentLoci"/>), the whole claim is
    /// redirected -- see <see cref="ActivityCore"/>.
    /// </summary>
    public OperationOutcome Activity(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, ActivityKind kind, string? label, string? agentId, string? name, DateTimeOffset now, bool unsafeBypass = false, IconToken iconToken = default, bool iconExplicit = true)
    {
        ValidateHandle(handle);
        OperationOutcome? outcome = null;
        bool ran = _writeGate.TryRun(() =>
            outcome = ActivityCore(handle, title, subtitle, iconUri, deepLink, kind, label, agentId, name, now, unsafeBypass, iconToken, iconExplicit));
        return ran ? outcome! : GateUnavailable(handle);
    }

    /// <summary>
    /// Phase 19A: whether to redirect is decided HERE, inside the SAME
    /// <see cref="WriteGate"/> critical section that performs the eventual
    /// write(s) (design note, invariant #5) -- reading
    /// <see cref="EngineMemory.CardedAgentLoci"/> and then writing based on
    /// what it said must be one atomic-under-the-mutex step, or a concurrent
    /// writer (e.g. that very agent's own <c>agent-stopped</c>) could retire
    /// the locus between the read and the write. An absent or uncarded
    /// <paramref name="agentId"/> takes the ordinary single-handle path,
    /// byte-for-byte unchanged from pre-19A behavior (decision point 4: never
    /// resurrects a retired child, never redirects a lone not-yet-2nd-worker).
    /// </summary>
    private OperationOutcome ActivityCore(
        string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, ActivityKind kind, string? label, string? agentId, string? name,
        DateTimeOffset now, bool unsafeBypass, IconToken iconToken, bool iconExplicit)
    {
        if (agentId is { Length: > 0 })
        {
            var (parentEntry, parentLive) = ResolveLive(handle);
            EngineMemory parentMemory = parentEntry?.EngineMemory ?? EngineMemory.Empty;
            if (parentLive is not null && parentMemory.CardedAgentLoci.Contains(agentId))
                return ApplyRedirectedActivity(handle, iconUri, deepLink, kind, label, agentId, name, now, unsafeBypass, iconToken, iconExplicit);
        }

        return ApplyClaimCore(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass,
            ctx => ClaimActivity(ctx, kind, label, agentId, name), afterWrite: null, refuseIfChild: false, refusedVerbPhrase: null, refuseIfActiveChildren: false, iconToken, iconExplicit);
    }

    /// <summary>
    /// Decision point 2's "exact equivalence": the CONTENT claim runs against
    /// the CHILD's own <see cref="ClaimContext"/>/sidecar entry via
    /// <see cref="ApplyClaimCore"/> -- the SAME pipeline (validator,
    /// icon-forced-recreate check, sidecar write) a direct
    /// <c>activity &lt;parentHandle&gt;#&lt;agentId&gt;</c> call would run,
    /// never re-titled/re-subtitled by this call (a translator never sends
    /// <c>--title</c>/<c>--subtitle</c> alongside <c>--agent</c> on
    /// <c>activity</c>) and never re-mints/re-places an icon: <paramref
    /// name="iconUri"/> is the SAME already-resolved <see cref="Uri"/> the
    /// caller resolved for the PARENT handle, which is exactly the
    /// byte-for-byte value the child was minted with
    /// (<see cref="MintChildCard"/>), so the live branch's own
    /// <c>live.IconUri != iconUri</c> check is false and no forced recreate
    /// (hence no <c>IconService.Place</c> call) ever fires here.
    ///
    /// Decision point 3: the PARENT still gets its own same-locus block
    /// clearing (<see cref="ClaimActivityParentLocusOnly"/>) -- content/steps
    /// on the parent are NEVER touched by a redirect, regardless of the
    /// child write's own outcome. Both writes share the ONE outer
    /// <see cref="WriteGate"/> critical section <see cref="Activity"/>
    /// already acquired (two sidecar entries, one mutex -- the design note's
    /// "shaping problem, not a locking one").
    ///
    /// The returned <see cref="OperationOutcome"/> is the CHILD's own --
    /// <see cref="OperationOutcome.Handle"/>/<see cref="OperationOutcome.View"/>
    /// report the child card, matching what a direct child-addressed call
    /// would return (AC2's equivalence, made observable on the outcome
    /// itself, not just the store's end state).
    /// </summary>
    private OperationOutcome ApplyRedirectedActivity(
        string parentHandle, Uri iconUri, Uri deepLink, ActivityKind kind, string? label, string agentId, string? name,
        DateTimeOffset now, bool unsafeBypass, IconToken iconToken, bool iconExplicit)
    {
        string childHandle = ChildHandle(parentHandle, agentId);
        var childOutcome = ApplyClaimCore(childHandle, null, null, iconUri, deepLink, now, unsafeBypass,
            ctx => ClaimActivity(ctx, kind, label, agentId: null, name), afterWrite: null, refuseIfChild: false, refusedVerbPhrase: null, refuseIfActiveChildren: false, iconToken, iconExplicit);

        ApplyClaimCore(parentHandle, null, null, iconUri, deepLink, now, unsafeBypass,
            ctx => ClaimActivityParentLocusOnly(ctx, agentId), afterWrite: null, refuseIfChild: false, refusedVerbPhrase: null, refuseIfActiveChildren: false, iconToken, iconExplicit);

        return childOutcome;
    }

    /// <summary>
    /// The PARENT half of a redirected <c>activity</c> claim (decision point
    /// 3): same-locus clearing ONLY, mirroring <see cref="ClaimAgentStopped"/>'s
    /// own "pure bookkeeping unless it actually clears a pending block" shape
    /// -- if <paramref name="agentId"/> wasn't blocking anything, this is a
    /// true no-op (no content/state write at all, <see cref="ApplyClaimCore"/>'s
    /// "No content/state claim" branch); if it WAS, the parent re-projects
    /// (Working if it was the last pending locus, still Blocked showing the
    /// next-latest question otherwise) using its EXISTING completed/executing
    /// steps -- never <see cref="Advance"/>d, so the parent's own step
    /// content is always byte-unchanged by a redirect (AC1).
    /// </summary>
    private static ClaimResult ClaimActivityParentLocusOnly(ClaimContext ctx, string agentId)
    {
        bool wasBlocked = ctx.Memory.BlockedLoci.Any(l => l.AgentId == agentId);
        var remaining = RemoveLocus(ctx.Memory.BlockedLoci, agentId);
        var memory = ctx.Memory with { BlockedLoci = remaining };

        if (!wasBlocked)
            return new ClaimResult(null, null, memory);

        var (completed, executing) = CurrentSteps(ctx);
        return ProjectAfterLocusChange(completed, executing, memory);
    }

    private static ClaimResult ClaimActivity(ClaimContext ctx, ActivityKind kind, string? label, string? agentId, string? name)
    {
        var (completed, executing) = CurrentSteps(ctx);
        string line = Rendering.BuildActivityLine(kind, label, name);
        (completed, executing) = Advance(completed, executing, line);

        var remaining = RemoveLocus(ctx.Memory.BlockedLoci, agentId);
        return ProjectAfterLocusChange(completed, executing, ctx.Memory with { BlockedLoci = remaining });
    }

    // ==== blocked ============================================================

    /// <summary>ERGO-31 §1's <c>blocked</c> row: platform-enforced literal question (ERGO-10: <c>NeedsAttention</c> requires <c>SetQuestion</c>). Records/refreshes <paramref name="agentId"/>'s locus (or the parent locus if absent) and always DISPLAYS the latest raised question -- LIFE-24's concurrent-block rule. ERGO-31 §5: structurally refused against a fan-out CHILD handle -- "a question always belongs to the session card" -- see <see cref="ApplyClaimCore"/>'s <c>refuseIfChild</c> check.</summary>
    public OperationOutcome Blocked(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, string question, string? agentId, DateTimeOffset now, bool unsafeBypass = false, IconToken iconToken = default, bool iconExplicit = true)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, ctx => ClaimBlocked(ctx, question, agentId, now), refuseIfChild: true, refusedVerbPhrase: "blocked", iconToken: iconToken, iconExplicit: iconExplicit);

    private static ClaimResult ClaimBlocked(ClaimContext ctx, string question, string? agentId, DateTimeOffset now)
    {
        var (completed, executing) = CurrentSteps(ctx);
        string normalizedQuestion = Normalizer.Normalize(question, FieldBudgets.Question);

        var remaining = RemoveLocus(ctx.Memory.BlockedLoci, agentId);
        List<BlockedLocus> updated = [.. remaining, new BlockedLocus(agentId, normalizedQuestion, now)];

        BlockedLocus latest = LatestLocus(updated);
        var content = (AppTaskContentDto)(new AppTaskContentDto.SequenceOfSteps(completed, executing) { Question = latest.Question });
        return new ClaimResult(content, AppTaskState.NeedsAttention, ctx.Memory with { BlockedLoci = updated, ReadyDecay = null, LastSummary = null });
    }

    // ==== ready ==============================================================

    /// <summary>ERGO-31 §1's <c>ready</c> row: bare preserves the current step content; <paramref name="summary"/> swaps to a <c>TextSummaryResult</c>. A turn-end event -- clears EVERY pending blocked locus (LIFE-24: turn-end events are never <c>--agent</c>-scoped). 15B: also the ONLY claim that ever (re)starts the LIFE-24 §6 Ready decay clock -- ONLY on a genuine transition INTO Ready (the card's prior live state was not already Completed); re-asserting an already-held Ready never restarts it (ERGO-31's idempotency rule, extended to the clock). Phase 19 Part C: the ONLY verb structurally refused while the addressed handle still has active agent loci (<see cref="ApplyClaimCore"/>'s <c>refuseIfActiveChildren</c>) -- a translator's turn-end `Stop -> ready` mapping can arrive mid-fan-out, before its subagents actually finish; see the phase-19 write-up for why <c>Broken</c> deliberately does not get the same treatment.</summary>
    public OperationOutcome Ready(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, string? summary, DateTimeOffset now, bool unsafeBypass = false, IconToken iconToken = default, bool iconExplicit = true)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, ctx => ClaimReady(ctx, summary, now), refuseIfActiveChildren: true, refusedVerbPhrase: "ready", iconToken: iconToken, iconExplicit: iconExplicit);

    /// <summary>
    /// Bug fix (2026-07-15 live dogfood): a bare re-affirmation used to fall
    /// straight to <see cref="AdvanceModel.NoStepsYetPlaceholder"/> whenever the
    /// live content was already a <c>TextSummaryResult</c> (a prior <c>ready
    /// --summary</c>) -- <c>AppTaskInfo</c> has no readback for that text at
    /// all, so <see cref="CurrentSteps"/> always comes back empty in that case.
    /// Now <see cref="EngineMemory.LastSummary"/> -- OUR OWN remembered copy of
    /// whatever text we last wrote -- is consulted first, so a card genuinely
    /// HOLDS its last message through repeat done-signals (<c>Stop</c> +
    /// <c>idle_prompt</c>) instead of resetting it. The placeholder remains the
    /// final fallback for when there is truly nothing remembered (a schema-&lt;4
    /// entry, or a card that reached Ready with real step content and never had
    /// a summary at all).
    /// </summary>
    private static ClaimResult ClaimReady(ClaimContext ctx, string? summary, DateTimeOffset now)
    {
        var (completed, executing) = CurrentSteps(ctx);
        string? normalizedSummary = summary is not null ? Normalizer.Normalize(summary, FieldBudgets.Summary) : null;
        string? rememberedSummary = ctx.Memory.LastSummary;

        AppTaskContentDto content;
        if (normalizedSummary is not null)
            content = new AppTaskContentDto.TextSummaryResult(normalizedSummary);
        else if (executing.Length == 0 && rememberedSummary is not null)
            content = new AppTaskContentDto.TextSummaryResult(rememberedSummary);
        else
            // The platform throws on a genuinely empty executing step (same
            // guard as ReadyDecay.DemoteToIdle) -- reached only when there is no
            // remembered summary to fall back on either.
            content = new AppTaskContentDto.SequenceOfSteps(completed, executing.Length > 0 ? executing : AdvanceModel.NoStepsYetPlaceholder);

        bool wasAlreadyReady = ctx.Live?.State == AppTaskState.Completed;
        ReadyDecayState decay = wasAlreadyReady && ctx.Memory.ReadyDecay is { } existing
            ? existing // re-asserting Ready never restarts the clock.
            : new ReadyDecayState(now, TimeSpan.Zero); // a genuine transition INTO Ready starts it fresh.

        var memory = ctx.Memory with { BlockedLoci = [], ReadyDecay = decay, LastSummary = normalizedSummary ?? rememberedSummary };
        return new ClaimResult(content, AppTaskState.Completed, memory);
    }

    // ==== broken =============================================================

    /// <summary>ERGO-31 §1/§3's <c>broken</c> row: ALWAYS a <c>TextSummaryResult</c> of the rendered reason (+ optional <paramref name="detail"/>) -- "CreateTextSummaryResult under Error renders fully with no question attached" (ERGO-31). A turn-end event -- clears every pending blocked locus. ERGO-31 §5: structurally refused against a fan-out CHILD handle, same as <see cref="Blocked"/> -- "children are scaffolding: Working/Completed only" is EXHAUSTIVE, not merely "never Blocked"; a child must never reach Error either -- see <see cref="ApplyClaimCore"/>'s <c>refuseIfChild</c> check.</summary>
    public OperationOutcome Broken(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, BrokenReasonToken reason, string? detail, DateTimeOffset now, bool unsafeBypass = false, IconToken iconToken = default, bool iconExplicit = true)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, ctx => ClaimBroken(ctx, reason, detail), refuseIfChild: true, refusedVerbPhrase: "broken", iconToken: iconToken, iconExplicit: iconExplicit);

    private static ClaimResult ClaimBroken(ClaimContext ctx, BrokenReasonToken reason, string? detail)
    {
        string text = BrokenReasons.Render(reason);
        if (detail is { Length: > 0 })
            text = $"{text}: {Normalizer.Normalize(detail, FieldBudgets.Summary)}";

        return new ClaimResult(new AppTaskContentDto.TextSummaryResult(text), AppTaskState.Error, ctx.Memory with { BlockedLoci = [], ReadyDecay = null, LastSummary = null });
    }

    // ==== agent-started / agent-stopped (15B: fan-out child-card mint/cascade) ===

    /// <summary>
    /// ERGO-31 §1's <c>agent-started</c> row: registers a child locus and,
    /// per ERGO-31 §5, mints a REAL child card at the 2nd concurrent
    /// registration (retroactively carding the 1st worker too -- both ids
    /// come back in the same <see cref="ClaimResult.NewlyCardedAgentIds"/>
    /// list, computed by <see cref="ClaimAgentStarted"/>). A name-only
    /// registration (no <paramref name="agentId"/>) mints nothing at all
    /// (mapping rule 5's degraded resolution: the translator is expected to
    /// surface it as a parent activity line via a separate <c>activity</c>
    /// call instead).
    ///
    /// Bug fix (2026-07-16, live dogfood): a REAL registration (has an
    /// <paramref name="agentId"/>) now ALSO advances the PARENT card's own
    /// step (<see cref="Rendering.BuildAgentStartedLine"/>) -- previously the
    /// transition table's target-state column was blank here, so the parent
    /// froze on whatever activity preceded the spawn for the ENTIRE fan-out
    /// window (confirmed live: the new child card(s) update fine via their
    /// own redirected <c>activity</c> claims, but the parent's own step never
    /// moved until something else, unrelated to the fan-out, happened to
    /// claim it). Routes through the same <see cref="ProjectAfterLocusChange"/>
    /// pipeline <see cref="ClaimActivity"/> uses, so a currently-Blocked
    /// parent keeps showing its pending question rather than losing it.
    /// <see cref="AgentStopped"/> deliberately does NOT get the same
    /// treatment (operator decision 2026-07-16) -- stop events arrive in a
    /// slow trickle well after the fact, and the child card retiring is
    /// signal enough on its own.
    ///
    /// Known non-blocking gap (15B review): a NESTED <c>agent-started</c>
    /// against a handle that is ITSELF an already-minted child is unguarded
    /// and untested -- <paramref name="handle"/> is passed straight to
    /// <see cref="ApplyClaim"/> like any other handle, so it would register a
    /// grandchild locus on the child's own EngineMemory rather than being
    /// refused the way <c>blocked</c>/<c>broken</c>/<c>session-ended --reason
    /// error</c> are. Left as-is; not exercised by any known translator.
    /// </summary>
    public OperationOutcome AgentStarted(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, string? agentId, string? name, DateTimeOffset now, bool unsafeBypass = false, IconToken iconToken = default, bool iconExplicit = true)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass,
            ctx => ClaimAgentStarted(ctx, agentId, name),
            afterWrite: result =>
            {
                foreach (string newlyCardedId in result.NewlyCardedAgentIds ?? [])
                    MintChildCard(handle, iconUri, deepLink, newlyCardedId, result.Memory.NameHintFor(newlyCardedId), now);
            },
            iconToken: iconToken, iconExplicit: iconExplicit);

    private static ClaimResult ClaimAgentStarted(ClaimContext ctx, string? agentId, string? name)
    {
        if (agentId is not { Length: > 0 })
            return new ClaimResult(null, null, ctx.Memory); // name-only: no locus id, no mint possible.

        var active = ctx.Memory.ActiveAgentLoci.Contains(agentId)
            ? ctx.Memory.ActiveAgentLoci
            : (IReadOnlyList<string>)[.. ctx.Memory.ActiveAgentLoci, agentId];

        var nameHints = UpsertNameHint(ctx.Memory.AgentNameHints, agentId, name);

        // "Carded" is sticky per locus (never re-derived from the live concurrency
        // count) so a child, once minted, is never silently un-minted just because
        // concurrency later drops back below 2 -- only its OWN agent-stopped retires
        // it. Minting therefore only ever ADDS to CardedAgentLoci, at the moment
        // concurrency FIRST reaches 2, for every currently-active-but-not-yet-carded
        // locus (which is exactly {1st, 2nd} the first time, and exactly {Nth} on
        // every subsequent start once fan-out is already established for this
        // session).
        IReadOnlyList<string> newlyCarded = [];
        IReadOnlyList<string> carded = ctx.Memory.CardedAgentLoci;
        if (active.Count >= 2)
        {
            string[] toMint = [.. active.Where(a => !ctx.Memory.CardedAgentLoci.Contains(a))];
            if (toMint.Length > 0)
            {
                carded = [.. ctx.Memory.CardedAgentLoci, .. toMint];
                newlyCarded = toMint;
            }
        }

        var memory = ctx.Memory with { ActiveAgentLoci = active, CardedAgentLoci = carded, AgentNameHints = nameHints };

        var (completed, executing) = CurrentSteps(ctx);
        string line = Rendering.BuildAgentStartedLine(name, agentId);
        (completed, executing) = Advance(completed, executing, line);

        var projected = ProjectAfterLocusChange(completed, executing, memory);
        return projected with { NewlyCardedAgentIds = newlyCarded };
    }

    private static IReadOnlyList<AgentNameHint> UpsertNameHint(IReadOnlyList<AgentNameHint> hints, string agentId, string? name)
    {
        if (name is not { Length: > 0 }) return hints; // nothing new to remember this call.
        return [.. hints.Where(h => h.AgentId != agentId), new AgentNameHint(agentId, name)];
    }

    /// <summary>
    /// ERGO-31 §1's <c>agent-stopped</c> row: retires the child locus
    /// (fan-in). LIFE-24: agent-stopped is one of the same-locus
    /// block-clearing trigger events -- if <paramref name="agentId"/> WAS a
    /// pending blocked locus, clearing it may re-project the card (Working,
    /// if it was the last one; otherwise still Blocked showing the
    /// next-latest question). If it wasn't blocking anything, this is pure
    /// bookkeeping (no content/state touch at all). ERGO-31 §5: if
    /// <paramref name="agentId"/> had a REAL minted child card, this also
    /// retires it (<see cref="ClaimResult.RetiredChildAgentId"/>, consumed by
    /// <see cref="RetireChildCard"/>) -- a child never un-retires itself just
    /// because concurrency drops; it only ever retires at its OWN
    /// agent-stopped.
    /// </summary>
    public OperationOutcome AgentStopped(string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, string? agentId, DateTimeOffset now, bool unsafeBypass = false, IconToken iconToken = default, bool iconExplicit = true)
        => ApplyClaim(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass,
            ctx => ClaimAgentStopped(ctx, agentId),
            afterWrite: result =>
            {
                if (result.RetiredChildAgentId is { } retiredId)
                    RetireChildCard(handle, retiredId, now);
            },
            iconToken: iconToken, iconExplicit: iconExplicit);

    private static ClaimResult ClaimAgentStopped(ClaimContext ctx, string? agentId)
    {
        var active = (IReadOnlyList<string>)[.. ctx.Memory.ActiveAgentLoci.Where(a => a != agentId)];
        bool wasCarded = agentId is not null && ctx.Memory.CardedAgentLoci.Contains(agentId);
        var carded = wasCarded
            ? (IReadOnlyList<string>)[.. ctx.Memory.CardedAgentLoci.Where(a => a != agentId)]
            : ctx.Memory.CardedAgentLoci;

        bool wasBlocked = ctx.Memory.BlockedLoci.Any(l => l.AgentId == agentId);
        var remaining = RemoveLocus(ctx.Memory.BlockedLoci, agentId);
        var memory = ctx.Memory with { ActiveAgentLoci = active, CardedAgentLoci = carded, BlockedLoci = remaining };
        string? retired = wasCarded ? agentId : null;

        if (!wasBlocked)
            return new ClaimResult(null, null, memory, RetiredChildAgentId: retired);

        var (completed, executing) = CurrentSteps(ctx);
        var projected = ProjectAfterLocusChange(completed, executing, memory);
        return projected with { RetiredChildAgentId = retired };
    }

    // ==== fan-out child-card mint/retire (ERGO-31 §5) ==========================

    /// <summary>
    /// Mints a REAL child card at the deterministic handle
    /// <c>&lt;parentHandle&gt;#&lt;agentId&gt;</c>, routed through the SAME
    /// <see cref="IAppTaskStore.Create"/> every other card in this engine
    /// uses (invariant #7: no second WinRT importer). <paramref name="iconUri"/>
    /// is the PARENT's own already-resolved icon URI for THIS call --
    /// reused BYTE-FOR-BYTE, never re-minted via <c>IconService.Place</c>
    /// (ERGO-13's icon-URI-keyed taskbar grouping would break under a
    /// per-child icon path). The child's own sidecar entry sets
    /// <see cref="EngineMemory.ParentHandle"/>, which is what makes it
    /// structurally a "child" for every later cascade/refusal check -- an
    /// otherwise perfectly ordinary handle, addressable by every existing
    /// handle-shaped verb (<c>list</c>/<c>remove</c>/further semantic verbs
    /// against the child handle itself) with no special-casing anywhere else.
    /// Idempotent no-op if the child handle is somehow already live
    /// (defensive -- <see cref="EngineMemory.CardedAgentLoci"/> is the source
    /// of truth for "already minted" and should make this unreachable).
    /// </summary>
    private void MintChildCard(string parentHandle, Uri iconUri, Uri deepLink, string agentId, string? name, DateTimeOffset now)
    {
        string childHandle = ChildHandle(parentHandle, agentId);
        if (_sidecar.Read(childHandle) is not null) return;

        string title = name is { Length: > 0 } ? name : agentId;
        var content = new AppTaskContentDto.SequenceOfSteps([], AdvanceModel.NoStepsYetPlaceholder);
        var created = _store.Create(title, "", deepLink, iconUri, content);
        var childMemory = EngineMemory.Empty with { ParentHandle = parentHandle };
        _sidecar.WriteWithMemory(childHandle, created.Id, now, childMemory);
        Log($"{parentHandle}: minted child card '{childHandle}' for agent '{agentId}' (id {created.Id}).");
    }

    /// <summary>Retires (removes the card + sidecar entry + icon for) a minted child at its OWN <c>agent-stopped</c> (fan-in) -- a clean no-op if the child isn't actually live for any reason (defensive).</summary>
    private void RetireChildCard(string parentHandle, string agentId, DateTimeOffset now)
    {
        string childHandle = ChildHandle(parentHandle, agentId);
        var entry = _sidecar.Read(childHandle);
        if (entry is null) return;

        _store.Remove(entry.Id);
        _sidecar.Delete(childHandle);
        _icons?.ReapLiveIcon(childHandle);
        Log($"{parentHandle}: retired child card '{childHandle}' (agent '{agentId}' stopped).");
    }

    /// <summary>ERGO-31 §5's deterministic child handle format -- the single place this string shape is built, so <c>list</c>/<c>remove</c> and every cascade path derive it identically.</summary>
    private static string ChildHandle(string parentHandle, string agentId) => $"{parentHandle}#{agentId}";

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

        // ERGO-31 §5: children are scaffolding (Working/Completed only, EXHAUSTIVE)
        // -- `session-ended --reason error` would otherwise land a child in Error,
        // a third state beyond the sanctioned two. Same refusal posture as the
        // `blocked`/`broken` guard in ApplyClaimCore (this method is a separate
        // code path that never routes through there, so it needs its own copy of
        // the check). `session-ended --reason error` end the session via the
        // parent instead. Note: `--reason finished` against a child is UNAFFECTED
        // -- it delegates to TaskOperations.Remove above, which already handles a
        // child handle correctly (removes just that one card, no new state).
        if (entry.EngineMemory?.ParentHandle is { } parentHandle)
        {
            string reason = ChildRefusalReason(handle, parentHandle, "session-ended --reason error");
            LogRefusal(handle, reason);
            return new OperationOutcome(OutcomeKind.RefusedInvalidArgument, handle, reason, live);
        }

        // ERGO-31 §5: session-ended cascades to every still-live child, exactly
        // like `remove` (Finished delegates straight to TaskOperations.Remove
        // above, which cascades on its own) -- the session is over either way, so
        // a leftover child card for a session that no longer exists would be an
        // orphan. Runs BEFORE the parent's own write so a mid-cascade failure
        // never leaves the parent transitioned with orphaned children still live.
        _ops.CascadeRemoveChildren(handle);

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
        Func<ClaimContext, ClaimResult> computeClaim,
        Action<ClaimResult>? afterWrite = null,
        bool refuseIfChild = false,
        string? refusedVerbPhrase = null,
        bool refuseIfActiveChildren = false,
        IconToken iconToken = default,
        bool iconExplicit = true)
    {
        ValidateHandle(handle);
        OperationOutcome? outcome = null;
        bool ran = _writeGate.TryRun(() =>
            outcome = ApplyClaimCore(handle, title, subtitle, iconUri, deepLink, now, unsafeBypass, computeClaim, afterWrite, refuseIfChild, refusedVerbPhrase, refuseIfActiveChildren, iconToken, iconExplicit));
        return ran ? outcome! : GateUnavailable(handle);
    }

    private OperationOutcome ApplyClaimCore(
        string handle, string? title, string? subtitle, Uri iconUri, Uri deepLink, DateTimeOffset now, bool unsafeBypass,
        Func<ClaimContext, ClaimResult> computeClaim, Action<ClaimResult>? afterWrite, bool refuseIfChild, string? refusedVerbPhrase,
        bool refuseIfActiveChildren,
        IconToken iconToken, bool iconExplicit)
    {
        var (entry, live) = ResolveLive(handle);

        // ERGO-31 §5: a fan-out CHILD card is scaffolding -- "Working/Completed
        // only" is EXHAUSTIVE (not merely "never Blocked"), so `blocked` AND
        // `broken` both structurally refuse here, before computeClaim runs at
        // all, against a handle we already know is a child purely from its OWN
        // sidecar entry's EngineMemory.ParentHandle (never a string-pattern guess
        // against the handle's spelling). `session-ended --reason error` gets the
        // equivalent guard in SessionEndedErrorCore (a separate code path that
        // never routes through here).
        if (refuseIfChild && entry?.EngineMemory?.ParentHandle is { } parentHandle)
        {
            string reason = ChildRefusalReason(handle, parentHandle, refusedVerbPhrase ?? "this verb");
            LogRefusal(handle, reason);
            return new OperationOutcome(OutcomeKind.RefusedInvalidArgument, handle, reason, live);
        }

        // Phase 19 Part C (found live, AC11's own dogfood): Claude Code's own
        // top-level turn ends (Stop fires) as soon as it dispatches Task-tool
        // subagent calls, without waiting for them to finish, so a translator's
        // unconditional Stop -> `ready` mapping would claim the ADDRESSED
        // handle into Completed while it still has real, delegated work
        // outstanding. Checked against the addressed handle's OWN
        // EngineMemory.ActiveAgentLoci (the FULL active set, resolved from
        // `entry` exactly like the refuseIfChild check above resolves
        // ParentHandle) -- deliberately NOT CardedAgentLoci: a lone,
        // not-yet-carded subagent's activity is designed to land on the parent
        // (decision point 4), so the parent legitimately still has real
        // outstanding work below the 2-concurrent carding threshold too. A
        // true no-op refusal, same shape as refuseIfChild above. `Ready` is the
        // only verb wired to this (operator decision 2026-07-15) -- `Broken`
        // is deliberately left untouched.
        if (refuseIfActiveChildren && entry?.EngineMemory?.ActiveAgentLoci is { Count: > 0 })
        {
            string reason = ActiveChildrenRefusalReason(handle, refusedVerbPhrase ?? "this verb");
            LogRefusal(handle, reason);
            return new OperationOutcome(OutcomeKind.RefusedInvalidArgument, handle, reason, live);
        }

        if (live is not null)
        {
            if (live.IconUri != iconUri)
                return ApplyIconForcedRecreate(handle, entry!.Id, title, subtitle, iconUri, deepLink, now, unsafeBypass, computeClaim, entry.EngineMemory ?? EngineMemory.Empty, live, afterWrite);

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
                afterWrite?.Invoke(result);

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
            afterWrite?.Invoke(result);
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

        // ERGO-30: repo-scoped presentation defaults apply HERE ONLY -- the
        // genuine "handle never seen before" creation path -- and NOWHERE else
        // (never the `live is not null` branch above, never
        // ApplyIconForcedRecreate's icon-token-changed recreate, which is an
        // already-established session, not a new one). This is the ONE call
        // site that ever touches `_discoverRepo` (AC3).
        (string effectiveTitle, string effectiveSubtitle, Uri effectiveIconUri) =
            ApplyRepoDefaults(handle, title, subtitle, iconUri, iconToken, iconExplicit);

        // Create() tolerates an empty title/subtitle fine (unlike UpdateTitles on an
        // already-live card, see ApplyIdentityIfClaimed's remarks) -- an absent
        // --title/--subtitle (and no repo title-template) on a brand-new handle
        // just creates with "".
        var created = _store.Create(effectiveTitle, effectiveSubtitle, deepLink, effectiveIconUri, finalContent);
        if (finalState != AppTaskState.Running)
            _store.Update(created.Id, finalState, finalContent);

        _sidecar.WriteWithMemory(handle, created.Id, now, freshResult.Memory);
        afterWrite?.Invoke(freshResult);
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
        Func<ClaimContext, ClaimResult> computeClaim, EngineMemory existingMemory, AppTaskView oldLive, Action<ClaimResult>? afterWrite)
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
        afterWrite?.Invoke(result);
        string reason = "Icon token changed -- forced Remove+Create; step history lost.";
        Log($"{handle}: {reason} (old id {oldId}, new id {recreated.Id})");
        return new OperationOutcome(OutcomeKind.Accepted, handle, reason, _store.Find(recreated.Id)!, IconChanged: true);
    }

    // ==== ERGO-30: repo-scoped presentation defaults (create-only) =============

    /// <summary>
    /// The sole entry point into ERGO-30's repo-scoped defaults, called ONLY
    /// from <see cref="ApplyClaimCore"/>'s genuine-creation branch. Resolves,
    /// per key, <c>flag &gt; env &gt; repo-file &gt; user-file &gt; built-in
    /// default</c> (<see cref="SettingsLoader.ResolvePresentationKey"/> for
    /// the first four layers; ERGO-33 adds the fifth): the caller's own
    /// explicit value always wins outright (<paramref name="title"/>/
    /// <paramref name="subtitle"/> are already <see langword="null"/> unless
    /// explicitly claimed; icon/icon-file via <paramref name="iconExplicit"/>).
    /// When <see cref="_discoverRepo"/> is <see langword="null"/> (a caller
    /// that never wired repo support -- most existing tests), this degrades
    /// straight to today's pre-phase-17 behavior with zero repo-file access
    /// (ERGO-33 does not touch this branch -- unreachable in the shipped CLI,
    /// see <see cref="BuiltInDefaultTitle"/>'s remarks).
    /// </summary>
    private (string Title, string Subtitle, Uri IconUri) ApplyRepoDefaults(
        string handle, string? title, string? subtitle, Uri fallbackIconUri, IconToken iconToken, bool iconExplicit)
    {
        if (_discoverRepo is null)
            return (title ?? "", subtitle ?? "", fallbackIconUri);

        RepoDiscoveryResult discovery = _discoverRepo();
        LogRepoConfigIssues(discovery);

        string? titleRaw = SettingsLoader.ResolvePresentationKey(RepoSettings.KeyTitleTemplate, title, _presentationEnv, discovery.AllowedValues, _presentationUserFile);
        string expandedTitle = titleRaw is null
            ? ""
            : title is not null
                ? titleRaw // the caller's own explicit --title: always verbatim, never templated.
                : RepoSettings.ExpandTemplate(titleRaw, discovery.RepoName, discovery.Branch);
        // ERGO-33: an empty result at this point -- no layer supplied anything,
        // OR a layer's own template expanded to empty (e.g. a repo file's
        // "{repo}" with no discovered .git root, ERGO-30's token-drop rule) --
        // falls through to the built-in default. Empty is never a final title.
        string effectiveTitle = expandedTitle.Length > 0 ? expandedTitle : BuiltInDefaultTitle(discovery);

        string? subtitleRaw = SettingsLoader.ResolvePresentationKey(RepoSettings.KeySubtitle, subtitle, _presentationEnv, discovery.AllowedValues, _presentationUserFile);
        // ERGO-33: the built-in subtitle default (branch, or "" with no git
        // root) is ONLY the terminus for a fully-absent chain (subtitleRaw
        // null) -- unlike title, an explicitly-empty subtitle from a layer
        // above is left alone; only the title has a "never blank" invariant.
        string effectiveSubtitle = subtitleRaw ?? BuiltInDefaultSubtitle(discovery);

        Uri effectiveIconUri = ResolveCreateTimeIcon(handle, fallbackIconUri, iconToken, iconExplicit, discovery);

        return (effectiveTitle, effectiveSubtitle, effectiveIconUri);
    }

    /// <summary>
    /// ERGO-33's built-in title default -- the chain's final terminus,
    /// reached only when <c>--title &gt; env &gt; repo template &gt; user
    /// file</c> are all absent or resolve to empty. Never blank:
    /// <c>&lt;anchor-folder&gt;</c> alone, or <c>&lt;anchor-folder&gt;
    /// (&lt;repo-folder&gt;)</c> when a <c>.git</c> root resolves to a
    /// DIFFERENTLY-NAMED folder than the anchor (suppressed when the two
    /// names coincide -- e.g. the anchor IS the repo root -- so a card is
    /// never titled <c>AppTaskInfoCli (AppTaskInfoCli)</c>). An anchor with
    /// no last path segment (a drive root, <c>C:\</c>) floors out at
    /// <see cref="Branding.DisplayName"/> (ERGO-18 -- derived, never re-literal).
    /// Only ever consulted from <see cref="ApplyRepoDefaults"/>'s
    /// <see cref="_discoverRepo"/>-wired branch -- <c>CompositionRoot</c>
    /// wires it unconditionally in the shipped CLI (falling back to
    /// <see cref="Environment.CurrentDirectory"/>), so the no-discoverRepo
    /// branch above (still "" today) is unreachable in production.
    /// </summary>
    private static string BuiltInDefaultTitle(RepoDiscoveryResult discovery)
    {
        string anchorName;
        try { anchorName = AnchorFolderName(discovery.AnchorPath); }
        catch (Exception) { anchorName = ""; } // A malformed anchor must never throw (FAIL-1) -- floors out below.

        if (anchorName.Length == 0)
            return Branding.DisplayName;

        if (discovery.RepoName is { Length: > 0 } repoName && !string.Equals(anchorName, repoName, StringComparison.OrdinalIgnoreCase))
            return $"{anchorName} ({repoName})";

        return anchorName;
    }

    /// <summary>ERGO-33's built-in subtitle default: the branch when a <c>.git</c> root resolved, else empty (subtitle has no "never blank" invariant -- unlike title, empty is a legitimate final subtitle).</summary>
    private static string BuiltInDefaultSubtitle(RepoDiscoveryResult discovery) => discovery.Branch ?? "";

    /// <summary>
    /// The anchor path's own last path segment, robust to a trailing
    /// separator and to a bare drive root (<c>C:\</c>, which
    /// <see cref="Path.GetFileName(string)"/> alone would NOT report as
    /// segment-less once naively trimmed -- <c>"C:"</c> has no separator
    /// character left for it to split on). Returns <c>""</c> for a drive (or
    /// UNC share) root, which <see cref="BuiltInDefaultTitle"/> reads as "no
    /// last path segment" -- the brand-name floor case.
    /// </summary>
    private static string AnchorFolderName(string anchorPath)
    {
        string normalized;
        try { normalized = Path.GetFullPath(anchorPath); }
        catch (Exception) { normalized = anchorPath; }

        string trimmedPath = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? root = SafeGetPathRoot(normalized);
        string trimmedRoot = (root ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (trimmedRoot.Length > 0 && string.Equals(trimmedPath, trimmedRoot, StringComparison.OrdinalIgnoreCase))
            return "";

        return Path.GetFileName(trimmedPath);
    }

    private static string? SafeGetPathRoot(string path) { try { return Path.GetPathRoot(path); } catch (Exception) { return null; } }

    /// <summary>
    /// Resolves the CREATE-time icon <see cref="Uri"/>: an env/repo-file/
    /// user-file icon/icon-file override (only consulted when
    /// <paramref name="iconExplicit"/> is <see langword="false"/> -- the
    /// caller's own explicit <c>--icon</c>/<c>--icon-file</c> always wins
    /// outright, never overridden by repo config), THEN the grouping-intent
    /// key (ERGO-14/ERGO-13): when active, every card created while the SAME
    /// repo root is discovered shares one exact <see cref="Uri"/> -- the
    /// first live card in the repo becomes the "owner" (an ordinary per-handle
    /// <see cref="IconService.Place"/>, so its own reap-on-remove lifecycle is
    /// completely unaffected); every LATER creation in the same repo, as long
    /// as that owner is still live, reuses its <see cref="AppTaskView.IconUri"/>
    /// byte-for-byte -- the SAME physics phase 15B's fan-out child-card mint
    /// uses (never a second <see cref="IconService.Place"/> call for a glommed
    /// card). If the recorded owner is no longer live (its session ended),
    /// ownership silently transfers to THIS call -- self-healing, no orphan
    /// bookkeeping required.
    /// </summary>
    private Uri ResolveCreateTimeIcon(string handle, Uri fallbackIconUri, IconToken iconToken, bool iconExplicit, RepoDiscoveryResult discovery)
    {
        if (_icons is null) return fallbackIconUri;

        IconToken effectiveToken = iconToken;
        bool haveOverride = false;
        if (!iconExplicit)
        {
            // icon-file beats icon when a SINGLE resolved layer supplies both --
            // mirrors the CLI's own --icon/--icon-file mutual exclusivity, but a
            // config file isn't hard-validated the way argv is; picking one
            // deterministically beats refusing the whole create over a config
            // author's mistake (this phase's non-disruptive posture).
            string? iconFileRaw = SettingsLoader.ResolvePresentationKey(RepoSettings.KeyIconFile, null, _presentationEnv, discovery.AllowedValues, _presentationUserFile);
            string? iconRaw = SettingsLoader.ResolvePresentationKey(RepoSettings.KeyIcon, null, _presentationEnv, discovery.AllowedValues, _presentationUserFile);
            if (iconFileRaw is { Length: > 0 })
            {
                effectiveToken = IconToken.RawPath(iconFileRaw);
                haveOverride = true;
            }
            else if (iconRaw is { Length: > 0 } && IconTokens.TryParse(iconRaw, out IconToken parsed, out _))
            {
                effectiveToken = parsed;
                haveOverride = true;
            }
        }

        if (!ResolveGroupEnabled(discovery))
            return haveOverride ? _icons.Place(handle, effectiveToken) : fallbackIconUri;

        if (_groupRegistry is null || discovery.RepoRootDir is null)
        {
            // Grouping intent set but no stable repo root was found to group by
            // (or no registry wired) -- documented degradation (mirrors AC6's
            // "missing git info degrades gracefully"): fall back to ordinary
            // per-handle placement, never a throw or a refusal.
            Log($"{handle}: repo grouping requested but no .git boundary was found (searched up to '{discovery.SearchedUpTo}') -- placing a per-handle icon instead.");
            return haveOverride ? _icons.Place(handle, effectiveToken) : fallbackIconUri;
        }

        string groupKey = discovery.RepoRootDir;
        string? ownerHandle = _groupRegistry.ReadOwnerHandle(groupKey);
        if (ownerHandle is not null)
        {
            var ownerEntry = _sidecar.Read(ownerHandle);
            var ownerLive = ownerEntry is not null ? _store.Find(ownerEntry.Id) : null;
            if (ownerLive is not null)
            {
                Log($"{handle}: repo grouping -- reusing '{ownerHandle}'s icon URI byte-for-byte.");
                return ownerLive.IconUri;
            }
        }

        Uri placed = _icons.Place(handle, effectiveToken);
        _groupRegistry.WriteOwnerHandle(groupKey, handle);
        Log($"{handle}: repo grouping -- became the icon owner for repo '{discovery.RepoName}'.");
        return placed;
    }

    private bool ResolveGroupEnabled(RepoDiscoveryResult discovery)
    {
        string? raw = SettingsLoader.ResolvePresentationKey(RepoSettings.KeyGroup, null, _presentationEnv, discovery.AllowedValues, _presentationUserFile);
        return raw is not null && raw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>ERGO-30's non-disruptive-posture logging for the two failure/observability modes AC1/AC4 call for: a malformed/oversized <c>.atv.json</c> (ignored, logged, exit 0) and non-allowlisted keys present in one (ignored AND logged, never silently dropped).</summary>
    private void LogRepoConfigIssues(RepoDiscoveryResult discovery)
    {
        if (discovery.ParseStatus is RepoConfigParseStatus.Malformed or RepoConfigParseStatus.TooLarge)
            Log($"repo config '{discovery.ConfigPath}': {discovery.ParseStatus} -- ignored, using defaults for every key it would have supplied.");

        if (discovery.DisallowedKeys.Count > 0)
            Log($"repo config '{discovery.ConfigPath}': ignored non-allowlisted key(s) {string.Join(", ", discovery.DisallowedKeys)} -- only {string.Join("/", RepoSettings.AllowlistKeys)} are repo-settable (ERGO-30).");
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
        // Neither outcome here is ever Ready (Running or NeedsAttention only),
        // so the decay clock -- and the remembered ready --summary text it
        // gates, LastSummary -- are always cleared, matching ClaimWorking/
        // ClaimBlocked/ClaimBroken's own "leaving Ready clears the clock" rule.
        memory = memory with { ReadyDecay = null, LastSummary = null };

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

    /// <summary>ERGO-31 §5's single reason-string builder for every "this verb is not valid against a fan-out child" refusal (<c>blocked</c>/<c>broken</c> via <see cref="ApplyClaimCore"/>'s <c>refuseIfChild</c> check, <c>session-ended --reason error</c> via <see cref="SessionEndedErrorCore"/>) -- one phrasing, so the three refusal messages never drift from each other.</summary>
    private static string ChildRefusalReason(string handle, string parentHandle, string verbPhrase)
        => $"'{handle}' is a fan-out child card (parent '{parentHandle}') -- {verbPhrase} is not valid against a child; direct it to the parent handle instead (ERGO-31 §5).";

    /// <summary>Phase 19 Part C's reason-string builder for the <c>refuseIfActiveChildren</c> refusal (<see cref="ApplyClaimCore"/>) -- parallel to <see cref="ChildRefusalReason"/>, but for a different structural reason: the addressed handle still has active/delegated agent work outstanding, so claiming Completed now would be premature (found live, AC11's own dogfood, 2026-07-15).</summary>
    private static string ActiveChildrenRefusalReason(string handle, string verbPhrase)
        => $"'{handle}' still has active/delegated agent work outstanding -- {verbPhrase} would prematurely claim Completed while at least one agent has not yet reported agent-stopped; wait for every active agent to stop first.";

    private void LogRefusal(string handle, string reason) => Log($"{handle}: refused -- {reason}");

    private void Log(string message) => _log(message);
}
