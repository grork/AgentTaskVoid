# LIFE-24: The host-event → task-state integration semantics (the mapping layer)
**Status:** OPEN

## Question
How should a host's lifecycle *events* and *states* (Claude Code's, and by extension every
host's) be **modeled** onto the AppTaskInfo card — its state, its step/summary content, and
even how many cards a single session produces — as a deliberate *integration semantic layer*,
rather than the current largely literal, near-1:1 event→verb mapping? This is NOT a single
field addition or a state-enum split; it is a layer of integration that needs its own design
pass, probably grounded in the **union / common patterns across hosts** (Claude Code, Copilot
CLI, Codex) rather than any one host's event names.

Additionally, in trying to address this it's unclear if the solution should be something that
rolls directly into this app -- atv -- directly, of if as part of building the integration
into hosts there should be an additional scripts as part of the hook configuration that is
unique to that host and does the minimal translation into the atv verbs etc.

## Why this surfaced
The phase-13 Claude Code integration dogfood (2026-07-10, first real end-to-end hook run).
The shipped `integrations/claude-code/` mapping (LIFE-10, "Host-agnostic CLI abstraction hook
events map onto") works mechanically — every hook fires, the card starts/steps/attention/
done/removes — but driving it against a live session made it clear the *semantics* are
loose: the mapping was authored event-by-event, not from a model of what an agent session's
states actually *mean*. Several distinct nuances all pointed at the same missing layer.

## The threads to review (operator, 2026-07-10) — the whole event set, not piecemeal
1. **`running` is wrong at session start.** `SessionStart → start` currently lands the card in
   `Running` before the user has even sent a message. A just-opened session that is waiting
   for its first prompt is not "working" — that is a different state (idle / awaiting-input)
   than mid-turn `Running`.
2. **"Idle" is overloaded.** When a session is sitting idle there are genuinely different
   situations the current model collapses: (a) it *completed a turn* and is waiting for the
   next prompt; (b) it is idle/paused with nothing pending (maybe `Paused`?); (c) a *tool* is
   requesting permission; (d) a *question* has been raised. Today (a)–(b) both look like
   `Completed`-then-idle and (c)–(d) both look like `NeedsAttention`, flattening real
   distinctions the user would want to tell apart at a glance.
3. **Review ALL the events.** This wants a systematic pass over the full host event vocabulary
   (SessionStart/PostToolUse/Notification/Stop/SessionEnd and their siblings — and the
   equivalents on other hosts), asking what each *means* for the card, not just wiring each to
   the nearest existing verb.
4. **Subagents may deserve their own cards.** Subagent start/stop events could spawn *new*
   AppTaskInfo cards with the *same icon* but *different sessions/handles*, so they **glom
   together on the taskbar** as one icon group while still being individually visible in the
   flyout — leaning on the icon-keyed grouping (ERGO-13, "Empirical: is grouping keyed on the
   exact icon URI string?"; ERGO-4/ERGO-15 grouping model). This is a concrete idea to weigh,
   not a decision.
5. **Step content is a raw JSON pass-through.** The current `step` text is the host payload
   more-or-less verbatim (e.g. `PowerShell: {"command":"Get-ChildItem","description":"…"}`).
   There is likely a lot of nuance in what a step *should* convey (human-readable, host-
   normalized, which tool, at what altitude) — and doing it well again points at the **union /
   common patterns across sessions and hosts**, not per-host string formatting.
6. **Two-way communication looms but is NOT being reopened now.** This modeling review makes
   the deferred interaction round-trip (INTER-1/2/3) feel more load-bearing — the operator is
   "starting to wonder if we need to revisit two-way," but is explicitly **not ready to rip
   that bandaid off yet**. Noted here as an adjacent pressure, deliberately left deferred.

## What makes it non-trivial (constraints)
- **The platform bounds what is expressible.** AppTaskInfo has its *own* state enum, and the
  ERGO-10 ("Guarding unsupported state × content × mutator combinations") crash matrix limits
  which (content, state) combinations actually render (some silently mis-render, some crash
  `explorer.exe`). A richer semantic model can only use states/combos the Shell will actually
  paint — "running **and** needs-attention at once," for instance, may simply not be a legal
  card state. Any new model must be reconciled against `SafeCombinationMatrix.cs`.
- **Hosts differ; the value is in the union.** Claude Code, Copilot CLI, and Codex have
  different event vocabularies and payloads (LIFE-12/13/14 inventories). A per-host mapping
  that is semantically sharp for one host but ad hoc across hosts misses the point — the
  design should find the common lifecycle model the host adapters each project onto, keeping
  `atv` itself host-agnostic (LIFE-2, "Covering hook behavior across different agent hosts").
- **Immutability + grouping costs.** Icon is set only at Create and grouping is icon-URI-keyed
  (ERGO-13); the subagent-cards idea and any icon/state rework interact with the per-handle
  icon model (ERGO-22) and the separate-by-session default (ERGO-15).
- **Content model is whole-replacement.** `step`/summary replace content wholesale (ERGO-8),
  and the safe content shapes are constrained (ERGO-3/ERGO-9/ERGO-11) — normalizing step text
  richly is bounded by what content shapes are safe to emit.

## Not A or B (explicitly)
Earlier framing offered (A) surface the attention reason as a field in `list --json`, or
(B) split `state` into orthogonal progress + solicitation fields. The operator rejected both
as too small: "I don't think it's really A or B. I think there's a layer of *integration* that
we need to think a little bit more about." The deliverable of this question is that integration
layer / semantic model — of which exposing-the-reason and any state re-shape would be
downstream consequences, not the thing itself.

## Scope note
Per the operator (2026-07-10): file OPEN for later discovery/answering; this is a **big** one
that likely needs its own thinking about the union / common patterns across hosts. It does
**not** change the current v1 build plan (phase 13 ships the working, if literal, Claude Code
mapping as-is). Two-way communication (INTER-*) stays deferred and is explicitly **not** being
reopened by this question yet. Parked to be taken up after the current build wraps.
