# INFRA-27: Observer posture vs teardown blocking budget
**Status:** DECIDED
**Plan:** phase-14
**Parent:** INFRA-23
> **Rollout:** phase-14 instantiates this for **Claude Code only** — the integration that
> validates the shared core (INFRA-30, recorder rollout & harness integration). Building the
> Copilot / Codex / pi legs is a future per-host phase each, not yet run through the process
> (INFRA-31, OPEN); the design here is not re-decided per host.

**Decision:** Async by default; synchronous only on teardown-adjacent events, mirroring
the shipped phase-13 `SessionEnd`-sync finding. Per host: Claude Code/Copilot async with
their session-end equivalent sync; Codex all-async (no session-end hook to protect); pi
in-process synchronous (no spawn cost, no teardown race).

## Question
A pure observer wants async/never-block everywhere — but phase-13 proved async
`SessionEnd` hooks get killed mid-flight at host teardown (the shipped Claude Code
integration's async cleanup hook lost the race against process exit; fixed there by
making that hook synchronous). A fully-async recorder would silently lose exactly the
teardown events it exists to observe — the highest-value, hardest-to-reproduce moments.

## Decision (operator + Claude Code, answer session, 2026-07-12)
1. **Where blocking is accepted: teardown-adjacent events only.** Everywhere else, async.
   Rationale of magnitude: a spawn-per-event costs ~100ms, so making every in-turn event
   (e.g. `PostToolUse`) synchronous is real perturbation of the host's event loop — the
   opposite of a faithful observer. Async events add ~0 to the loop (fire-and-forget
   spawn). But async loses the teardown events to the process-exit race (the exact
   phase-13 bug), so those — and only those — block. This is precisely the rule the
   shipped `integrations/claude-code/` integration already arrived at.
2. **Perturbation budget.** Async events: must not block the loop at all (they don't).
   Sync teardown events: a single small append well under a tight ceiling (the shipped
   integration used `timeout: 10`; the recorder's append is faster). The observer's job is
   to record reality, not to change it by camping on it.
3. **The answer differs per host:**
   - **Claude Code / Copilot CLI** (spawn-per-event): async in-turn; the session-end
     equivalent (`SessionEnd` / `sessionEnd`) synchronous.
   - **Codex CLI**: all-async — it has **no** session-end hook, so there is no teardown
     event to protect (a quiet death degrades gracefully; LIFE-24).
   - **pi** (in-process TS extension): synchronous in-process — no spawn cost worth
     optimizing and no "async hook killed at teardown" failure mode, since `session_shutdown`
     is delivered in-process.
4. **Coherence with INFRA-26.** Because INFRA-26 skips the replacement class entirely, the
   recorder never has to *do work* inside a hook to stay safe — the only reason blocking is
   ever accepted here is the teardown race, nothing else.

## Amendment (2026-07-12, phase-14 planning)
pi's "no spawn cost" phrasing above described hook DELIVERY (in-process), not the recorder
invocation: INFRA-24/25's one-shared-write-path rule means pi's extension still spawns the
recorder exe per event rather than appending JSONL itself (TS has no named-mutex
primitive). Posture is therefore the same as every other host — fire-and-forget for
in-turn events, awaited only on `session_shutdown`. The teardown-only blocking rule is
unchanged. For pi, "verbatim" means the conduit's single serialization of the in-process
event object at delivery (no host-produced bytes exist); it is never re-serialized after.
