namespace Atv.Persistence;

/// <summary>
/// The sidecar's per-handle payload (ERGO-21, "The sidecar store design",
/// decision detail DP2): an INDEX (<c>handle -&gt; Id</c> plus a liveness
/// stamp) PLUS, as of phase 15 (LIFE-24, "The host-event → task-state
/// integration semantics"), the v2 engine's own per-handle memory
/// (<see cref="EngineMemory"/>) -- goal, blocked-on loci, known agent loci.
/// Still deliberately nothing else beyond that: no cached title/content (the
/// API itself is source of truth for those, ERGO-8's whole-content-replacement
/// model), no group/owner/cwd (group by ERGO-14, owner by ERGO-16, cwd
/// deferred with INTER-4). <see cref="EngineMemory"/> is nullable so a
/// pre-phase-15 (schema v1) entry on disk -- which never wrote this property
/// -- deserializes cleanly as <see langword="null"/>, not a throw; every
/// consumer treats <see langword="null"/> the same as <see cref="Atv.Persistence.EngineMemory.Empty"/>.
/// </summary>
public sealed record SidecarEntry(string Id, DateTimeOffset LastUpdate, int SchemaVersion, EngineMemory? EngineMemory = null)
{
    /// <summary>Bump when this shape changes; keeps forward-compat cheap (ERGO-21 DP2). Bumped 1-&gt;2 for phase 15A's <see cref="EngineMemory"/> addition, 2-&gt;3 for phase 15B's decay/fan-out fields, 3-&gt;4 for <see cref="Atv.Persistence.EngineMemory.LastSummary"/> (the bug-fix "remember the platform's write-only <c>TextSummaryResult</c> text ourselves" addition, 2026-07-15).</summary>
    public const int CurrentSchemaVersion = 4;
}

/// <summary>
/// One pending "blocked on you" claim the engine is tracking for a card
/// (LIFE-24's same-locus clearing / concurrent-block rules, ERGO-31 §1's
/// <c>blocked</c> row): <see cref="AgentId"/> is <see langword="null"/> for
/// the parent/main-thread locus, or a subagent's id; <see cref="Question"/>
/// is the normalized text this locus most recently raised;
/// <see cref="WhenBlocked"/> orders concurrent loci so the engine can always
/// answer "which question is latest" (re-asserting the SAME locus refreshes
/// this timestamp, even though Blocked itself never decays -- LIFE-24: "none
/// -- never decays" is about the CARD's clock, not this ordering key).
/// </summary>
public sealed record BlockedLocus(string? AgentId, string Question, DateTimeOffset WhenBlocked);

/// <summary>
/// LIFE-24's Ready-&gt;Idle presence-gated decay bookkeeping (15B), meaningful
/// only while the owning card is currently semantically Ready
/// (<c>AppTaskState.Completed</c>). <see cref="AccruedPresentTime"/> is
/// elapsed WALL-CLOCK time that has passed while the user was present,
/// summed incrementally one <see cref="Atv.Watchdog.WatchdogLoop"/> decay-pass
/// tick at a time (the process is not continuously running, so this can never
/// be a live in-memory timer -- LIFE-16's "stateless-over-disk"
/// precedent). <see cref="LastSampledAt"/> is the wall-clock anchor for the
/// NEXT tick's delta -- set to the moment of the qualifying Ready transition
/// when the clock starts, and advanced every decay-pass tick regardless of
/// whether that tick's delta actually counted toward the accrual (presence
/// gates what gets COUNTED, never whether the anchor advances, so a long
/// absent stretch doesn't get retroactively counted the moment presence
/// returns). Deliberately does NOT reuse <see cref="SidecarEntry.LastUpdate"/>
/// for this anchor -- that field is the UNRELATED hygiene-reap clock
/// (LIFE-22/invariant #6); a decay-only bookkeeping write must never bump it,
/// or the two clocks would be conflated (LIFE-24's explicit "never
/// conflated" rule) by making an idle, un-acted-on card look artificially
/// fresh to the hygiene reap forever.
/// </summary>
public sealed record ReadyDecayState(DateTimeOffset LastSampledAt, TimeSpan AccruedPresentTime);

/// <summary>ERGO-31 §5's fan-out addressing: the last-known human-readable name a locus supplied via <c>agent-started --name</c>, remembered so a LATER retroactive child-card mint (the "2nd concurrent start also cards the 1st worker" rule) can still give the first worker's card a sensible title even though its own <c>agent-started</c> call happened on an earlier, separate process invocation.</summary>
public sealed record AgentNameHint(string AgentId, string? Name);

/// <summary>
/// The v2 engine's own per-handle memory (LIFE-24: "if it needs memory or a
/// clock, it is engine") -- everything a semantic-verb claim needs to
/// remember that the platform's <c>AppTaskInfo</c> itself has no slot for.
/// Deliberately does NOT duplicate <c>SemanticState</c> itself: which of the
/// five states a card is in is always read back off the live card's own
/// <c>AppTaskState</c> (<see cref="Atv.Semantics.SemanticStateMapping"/>), so
/// there is exactly one source of truth for "what state is this card in."
///
/// 15A fields: <see cref="Goal"/> (the last <c>working --goal</c> claim,
/// remembered even though its RENDERING currently just flows into the step
/// stream like any other activity line -- ERGO-31's parked pinning note is
/// not yet built) and <see cref="BlockedLoci"/> (LIFE-24's concurrent-block
/// bookkeeping).
///
/// 15B fields (ERGO-31 §5/§6): <see cref="ActiveAgentLoci"/> (which agent ids
/// <c>agent-started</c> has registered and <c>agent-stopped</c> hasn't yet
/// retired), <see cref="CardedAgentLoci"/> (the SUBSET of
/// <see cref="ActiveAgentLoci"/>'s current-and-former members that already
/// have a real minted child card -- "carded" is sticky per locus even after
/// that locus later drops out of <see cref="ActiveAgentLoci"/>, since a
/// minted child only retires at its OWN <c>agent-stopped</c>, never merely
/// because concurrency dropped back below 2), <see cref="AgentNameHints"/>,
/// <see cref="ParentHandle"/> (non-null ONLY on a minted CHILD card's own
/// sidecar entry -- <see langword="null"/> for every ordinary/parent/session
/// handle; this is what makes a handle "a child" structurally, rather than by
/// pattern-matching its `#` separator), <see cref="ReadyDecay"/> (the
/// Ready-&gt;Idle clock; <see langword="null"/> whenever the card is not
/// currently accruing -- never in Ready, or Ready but not yet claimed via
/// <c>ready</c> since the 15B upgrade), and <see cref="LastSummary"/> (bug fix,
/// 2026-07-15): the text most recently written via <c>ready --summary</c>,
/// kept here because <c>AppTaskInfo</c> has no readback for a
/// <c>TextSummaryResult</c>'s text at all -- once written, the platform itself
/// can never answer "what was that text" (see <see cref="Atv.Store.AppTaskView"/>'s
/// own remarks on this asymmetry). Without this, a bare re-affirming
/// <c>ready</c> (no <c>--summary</c> -- e.g. Claude Code's <c>idle_prompt</c>
/// following a <c>Stop</c> that DID carry one) or the <see cref="ReadyDecay"/>
/// demotion to Paused had no way to preserve the summary and silently
/// replaced it with the "nothing yet" placeholder. Always <see langword="null"/>
/// while not Ready-with-a-summary; cleared wherever <see cref="ReadyDecay"/>
/// is cleared -- "leaving Ready" retires both together.
/// </summary>
public sealed record EngineMemory(
    string? Goal,
    IReadOnlyList<BlockedLocus> BlockedLoci,
    IReadOnlyList<string> ActiveAgentLoci,
    IReadOnlyList<string> CardedAgentLoci,
    IReadOnlyList<AgentNameHint> AgentNameHints,
    string? ParentHandle,
    ReadyDecayState? ReadyDecay,
    string? LastSummary = null)
{
    public static readonly EngineMemory Empty = new(null, [], [], [], [], null, null, null);

    /// <summary>
    /// Defensive normalization for a schema-&lt;3 entry deserialized against
    /// this newer shape: STJ leaves a missing constructor-parameter's value
    /// at the CLR default (<see langword="null"/> for every list field here,
    /// not <c>[]</c>) rather than throwing (ERGO-21's graceful-degradation
    /// precedent). Every reader routes through this rather than trusting the
    /// raw deserialized shape.
    /// </summary>
    public EngineMemory Coalesced() => this with
    {
        BlockedLoci = BlockedLoci ?? [],
        ActiveAgentLoci = ActiveAgentLoci ?? [],
        CardedAgentLoci = CardedAgentLoci ?? [],
        AgentNameHints = AgentNameHints ?? [],
    };

    /// <summary>The most recently supplied <c>--name</c> for <paramref name="agentId"/>, or <see langword="null"/> if none was ever given (a retroactively-carded child then falls back to its bare agent id as its title).</summary>
    public string? NameHintFor(string agentId) => AgentNameHints.FirstOrDefault(h => h.AgentId == agentId)?.Name;
}
