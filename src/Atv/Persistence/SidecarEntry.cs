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
    /// <summary>Bump when this shape changes; keeps forward-compat cheap (ERGO-21 DP2). Bumped 1-&gt;2 for phase 15's <see cref="EngineMemory"/> addition.</summary>
    public const int CurrentSchemaVersion = 2;
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
/// The v2 engine's own per-handle memory (LIFE-24: "if it needs memory or a
/// clock, it is engine") -- everything a semantic-verb claim needs to
/// remember that the platform's <c>AppTaskInfo</c> itself has no slot for.
/// Deliberately does NOT duplicate <c>SemanticState</c> itself: which of the
/// five states a card is in is always read back off the live card's own
/// <c>AppTaskState</c> (<see cref="Atv.Semantics.SemanticStateMapping"/>), so
/// there is exactly one source of truth for "what state is this card in."
///
/// 15A scope: <see cref="Goal"/> (the last <c>working --goal</c> claim,
/// remembered even though its RENDERING currently just flows into the step
/// stream like any other activity line -- ERGO-31's parked pinning note is
/// not yet built), <see cref="BlockedLoci"/> (LIFE-24's concurrent-block
/// bookkeeping), and <see cref="ActiveAgentLoci"/> (a 15B on-ramp: which
/// agent ids <c>agent-started</c> has registered and <c>agent-stopped</c>
/// hasn't yet retired -- 15A never acts on the count, it just keeps the
/// bookkeeping so 15B's "mint a child card at the 2nd concurrent start" has
/// something to read). NOT yet present (15B's job): decay-accrual bookkeeping
/// for the Ready-&gt;Idle clock, and the fan-out child-card registry itself.
/// </summary>
public sealed record EngineMemory(
    string? Goal,
    IReadOnlyList<BlockedLocus> BlockedLoci,
    IReadOnlyList<string> ActiveAgentLoci)
{
    public static readonly EngineMemory Empty = new(null, [], []);
}
