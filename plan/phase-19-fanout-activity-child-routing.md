# Phase 19: Route a carded subagent's own `activity` to its child card, in `atv` itself

**Status:** NOT STARTED — filed 2026-07-14/15 from the phase-18 live dogfood.
**Depends on:** phase 15 (`ERGO-31` §5's fan-out addressing, `EngineMemory.CardedAgentLoci`),
phase 18 (the Claude Code translator that surfaced the gap live).
**Unblocks:** nothing downstream in this plan; makes phase 18's Claude Code plugin (and any
future host translator built the same way) render subagent activity correctly.

## Goal

`docs/integration-api.md` §5 already DECIDED the fan-out addressing rule: "once minted, a
subagent's own further activity should target the CHILD handle directly ... not the parent."
That rule is not implemented. This phase implements it, **inside `atv`'s own engine** — not in
any translator.

## Root cause (found live, phase-18 AC5 dogfood, 2026-07-14/15)

`integrations/claude-code/plugins/atv-integration/translate.ps1` already does the right thing:
on a subagent's own `PreToolUse`/`PostToolUse`, it calls
`atv activity <session> --kind <k> --label - --agent <agentId> [--name <n>]` — exactly the call
shape `docs/integration-api.md`'s verb table documents (`activity <h> | ... | --agent <id> ...`).
**The information is already being passed correctly.**

But `SemanticEngine.ClaimActivity` (`src/Atv/Semantics/SemanticEngine.cs:151-159`) only ever
uses `agentId` for one thing — clearing that locus's pending block (`RemoveLocus(ctx.Memory.
BlockedLoci, agentId)`, §5.1's same-locus-clearing rule). It never checks whether `agentId` is
already a carded child (`ctx.Memory.CardedAgentLoci.Contains(agentId)`) to redirect the content
claim to the child's own card (`<handle>#<agentId>`). Every subagent tool-call activity line
lands on the **parent's** own `executing` step instead, regardless of which agent produced it.

**Not a translator bug.** `translate.ps1` is deliberately stateless (prior decision — no local
bookkeeping in the translator). It doesn't need to be: `EngineMemory.CardedAgentLoci` already
lives on the PARENT handle's own sidecar entry and already answers "does this agent id have a
minted child card right now?" precisely. Asking the translator to duplicate that bookkeeping in
a second, translator-owned state file would be redundant and a source of drift against the
engine's own source of truth. The fix belongs entirely in `atv`'s claim-processing path.

## Observed symptom (live)

A real 2-subagent fan-out (`26ccd262-...` parent, two `general-purpose` children): both child
cards sat at `"Not started yet."` for their entire life — every one of their real tool calls
(`Bash`/`Glob`/`Read`, confirmed via a `claude --debug-file` capture) rendered as the **parent's**
activity line instead, including one capture where the leaked text was recognizably an internal
task-completion-notification payload (its `task-id` matched one of the live child agent ids).
Full timeline (mint/retire correct, only content-routing wrong) is in `progress.md`'s phase-18
write-up.

## What needs deciding (not yet decided — scope of this phase)

1. **Which verbs redirect.** Per the table, only `activity` and `blocked` accept `--agent`.
   `blocked` is explicitly parent-targeted-only by design (§5.1's same-locus clearing model —
   a child can never legally be `NeedsAttention` at all, ERGO-31 §5's "Working/Completed only").
   So redirection applies to `activity` alone; `blocked --agent` keeps its current
   locus-bookkeeping-only behavior.
2. **Redirection mechanics.** When `ClaimActivity` sees a carded `agentId`, does it fully
   substitute the target (re-run the claim against `<handle>#<agentId>` and leave the parent's
   own `executing`/`completed` untouched), or write both? Docs say "not the parent," implying
   full substitution of CONTENT — but the locus-clearing side effect (`RemoveLocus` on
   `BlockedLoci`) is parent-side bookkeeping and must still happen on the parent regardless of
   where the content lands.
3. **Uncarded agent ids.** If `agentId` is present but not (yet) in `CardedAgentLoci` — a lone,
   not-yet-2nd-concurrent worker, or a translator-side typo/unknown id — falls back to today's
   behavior (content lands on the addressed handle, i.e. the parent). This matches the existing
   "name-only host" degraded-fallback language in §5, extended to this case.
4. **Child-card invariants must still hold** after a content-only update: the byte-identical
   icon-URI-reuse rule (§5), the child's Working/Completed-only reachable range, and its own
   `ready`-decay/removal lifecycle (phase 15B) are unaffected by *how* a claim reached the card,
   so a redirected `activity` must go through the exact same `ApplyClaim` path a direct
   `atv activity <child-handle> ...` call would.

## Likely touch points (not yet scoped in detail)

- `src/Atv/Semantics/SemanticEngine.cs` (`ClaimActivity`, possibly `ApplyClaim`'s shape if a
  claim needs to name a different handle than the one it was invoked against)
- `tests/Atv.LogicTests/Semantics/SemanticEngineFanOutTests.cs` (extend: a carded agent's
  `activity` call lands on the child's `executing` step, parent's own content is untouched,
  locus-clearing still happens on the parent, uncarded-agent fallback still targets the
  addressed handle)
- `docs/integration-api.md` §5 (currently describes the INTENDED behavior only as translator
  guidance — "a translator... should target the CHILD handle directly" — needs to also state
  plainly that a carded agent's activity redirects even when a caller addresses the PARENT
  handle with `--agent`, since that's what the shipped Claude Code translator actually does and
  will keep doing)

**Not touched:** `integrations/claude-code/plugins/atv-integration/translate.ps1` — it already
sends the correct call shape; nothing about this phase requires a translator change.

## Why this is its own phase, not a quick patch

This redirects a claim to a different handle than the one it was invoked against — new territory
for `ApplyClaim`'s architecture, which has always resolved exactly one handle per call. Needs its
own design pass (item 2 above) rather than an inline fix, unlike the same dogfood's other,
genuinely one-line `ClaimReady` empty-`executingStep` fix (already applied directly, see
`progress.md`'s phase-18 section).
