# ERGO-8: Update verbs for ergonomic revision given whole-content replacement
**Status:** DECIDED
**Plan:** phase-05
**Decision:** The "advance" model: `step <h> "X"` archives the previous executing
step into `completedSteps` and sets the new executing step -- the caller never
manages the array. `completedSteps` is a FIFO capped at 10 (oldest drops off).
The CLI owns the read-modify-write (RMW = read-modify-write), run against the
live API (`GetCompletedSteps()`/`GetExecutingStep()` -> advance -> `Update()`).
[AMENDED 2026-07-07: `step` PRESERVES the card's current state -- the RMW re-sends
the state it read with the advanced content; combinations outside the ERGO-10 safe
set (NeedsAttention without a question) are refused by the validator. State changes
belong to the dedicated verbs, which drop the question when leaving NeedsAttention.]
**Parent:** ERGO-1

The API replaces content wholesale on `Update` (e.g. `CreateSequenceOfSteps`
takes the full completedSteps array every time). What verbs make revision
ergonomic for scripts (add-step, set-state, set-title, ...), and which side
owns the read-modify-write those verbs imply? (See INFRA-6 for the race
implications of that read-modify-write.)

Decision detail (2026-07-02):
- "Advance" beats explicit array management: agent hooks fire one event at a time
  and each means "now doing X", so `step` maps 1:1 to a hook fire with zero glue.
  The 10-deep FIFO stops a long session (hundreds of tool calls) growing the card
  unbounded.
- RMW = read current steps, modify (advance + cap), write back; the API has no
  append primitive. Run against the API (source of truth for content), never the
  sidecar -- ERGO-7's sidecar holds only handle->Id + metadata. Two interleaved
  RMWs can still lose a step -> serialization is INFRA-6.
- Verb set / shape is ERGO-9. Deferred out of v1: upsert-on-`step` (auto-create
  when no task exists) and per-step pass/fail status.
