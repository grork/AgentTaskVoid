# Phase 05: Task operations — validator, advance model, upsert & resurrection

**Depends on:** phase 02 (seam/fake/DTOs), phase 04 (WriteGate, sidecar, recycle bin)
**Unblocks:** phase 08 (verbs are thin wrappers over these operations)

## Goal

Implement the verb-independent operation core: the safe-combination validator, the
step "advance" model, state transitions, `start` upsert/adopt semantics, and the
recycle-bin resurrection miss path. All pure logic above the seam, exhaustively
unit-tested against the fake.

## Decisions implemented

### Validator (ERGO-10, "Guarding unsupported state × content × mutator combinations")

- The CLI only ever emits combinations from the documented safe set and hard-rejects
  anything outside it (refuse + log, non-disruptive per FAIL-1; exit 4 under
  `--strict`). Motivation: `SetQuestion` on the wrong shape/state is a real
  explorer.exe stack-overflow crash — crashing the Shell is unacceptable.
- The matrix lives AS DATA IN CODE, one source; the doc
  (`docs/windows-ui-shell-tasks/state-content-compatibility.md`) references it.
  Encode the safe set from that doc.
- `--unsafe` bypasses the validator (off by default, experimentation only,
  meaningful only on content-emitting write verbs).
- INFRA-10 ("Testing behavior only observable in Shell rendering"): the validator is
  our own logic — covered by the standard suite. A data-driven test exhaustively
  walks the state × content × mutator space and asserts every documented-safe cell
  accepted, every unsafe cell refused. The fake must NOT model crash-on-bad-combo.
  Whether the matrix still holds on new Windows builds is the INFRA-13 manual
  checklist (phase 13), not automation.

### Advance model (ERGO-8, "Update verbs for ergonomic revision")

- `step <h> "X"`: archive the previous executing step into `completedSteps`, set the
  new executing step. `completedSteps` is a FIFO capped at 10 (oldest drops).
- `step` PRESERVES the card's current state (ratified 2026-07-07): the RMW re-sends
  the state it read alongside the advanced content. Where that lands outside the
  safe set (NeedsAttention — steps content without a question), the validator
  refuses it (standard non-disruptive no-op; exit 4 strict). Consequence: after
  `attention`, `step` is refused until a state-changing verb runs.
- The CLI owns the read-modify-write, run against the LIVE API through the store
  (read current steps → advance + cap → write whole content back), never against the
  sidecar. Always inside `WriteGate`.
- Deferred (do not build): upsert-on-step (general auto-create), per-step pass/fail.

### State transitions (ERGO-9, ERGO-27)

- `state` accepts `running|paused` ONLY (C7 — done/completed/failed via their verbs).
- `done`/`fail` → Completed/Failed; bare keeps `SequenceOfSteps`, `--summary` swaps
  content to `TextSummaryResult`.
- `attention` → NeedsAttention + `SetQuestion`, the one documented-safe question
  cell. Display-only in v1.
- State-changing verbs rebuild content into the target-safe shape: leaving
  NeedsAttention (`state running|paused`, `done`, `fail`) DROPS the question
  mutator — otherwise the safe set would make NeedsAttention inescapable.
- All transitions route through the validator.

### `start` upsert (ERGO-25, "`start` on an already-live handle")

- `start` is upsert/idempotent — never errors on a live handle. It adopts the card,
  re-applies title/subtitle/state (state=Running, `SequenceOfSteps`), PRESERVES step
  history. `--reset` (or explicit `remove` then `start`) forces a clean slate
  (clears steps, initial state).
- Icon caveat: `IconUri` is immutable per task (set only at Create; it is the
  grouping key). A re-`start` specifying a DIFFERENT icon token than the live card
  forces platform Remove+Create and loses step history — unavoidable, accepted.
- `start`-on-live-handle is VALID in the validator.

### Resurrection miss path (LIFE-15, "Handling tasks that get 'resurrected'"; LIFE-21)

- Any update-class operation (`step`/`state`/`done`/`fail`/`attention`/`start`)
  whose handle is absent from the live sidecar checks the recycle bin:
  - Found within TTL → re-create the card from the record (title, subtitle, icon-ref,
    deepLink — core info intact), move the entry back to live (phase 07 wires the
    co-located icon's move-back into this path), apply the update.
    Steps/state restart fresh (nothing mutable was stored). This is a SCOPED upsert:
    only previously-live tombstoned handles resurrect.
  - Not found (past TTL / after reboot / never seen) → for the FIVE update-class
    verbs (`step`/`state`/`done`/`fail`/`attention`), a clean unknown-handle no-op
    (log; `--strict` exit nonzero; `--json` `{"ok":false,"reason":…}`). `start` is
    the exception: it always carries fully-resolved create fields, so a total miss is
    just its ordinary create path — `start` never no-ops (ERGO-25/ERGO-27
    create-or-adopt). Ratified 2026-07-08.
- A resurrecting `start` uses the fields `start` itself carries (title, subtitle,
  icon, deepLink — always fully resolved by the CLI's default layer), with a fresh
  step sequence, consistent with the live-handle adopt path that re-applies the
  caller's fields. It does NOT read the tombstone's stored core info (that is what
  the five update-class verbs resurrect from, since they carry no create fields).
  ERGO-25 recycle-bin caveat, clarified 2026-07-08.

## Files affected

```
src/Atv/Operations/SafeCombinationMatrix.cs   # the matrix as data
src/Atv/Operations/Validator.cs
src/Atv/Operations/TaskOperations.cs          # start/step/state/done/fail/attention/remove cores; each = WriteGate → reconcile (per-handle on update-class verbs, full pass on start/remove — phase 04 scoping) → miss-path check → validate → store write → sidecar stamp
src/Atv/Operations/AdvanceModel.cs
src/Atv/Operations/Resurrection.cs
tests/Atv.LogicTests/Operations/*             # see below
```

## Acceptance criteria (fake-backed, written first)

1. Exhaustive matrix walk: every safe cell accepted, every unsafe cell refused
   (refusal = no store write occurred + logged); `--unsafe` emits anyway.
2. Advance model: N sequential steps yield executing=Nth, completedSteps=last ≤10 in
   order; the RMW read comes from the store, not any cache. `step` re-sends the
   state it read (Paused stays Paused); `step` on a NeedsAttention card is refused
   with no store write.
3. `state` rejects anything but running/paused as invalid args.
4. `done --summary` produces TextSummaryResult; bare `done` keeps SequenceOfSteps;
   both reach Completed. Same for `fail`/Failed. `attention` sets the question on
   the safe cell only.
5. Upsert: `start` on live handle preserves steps and re-applies fields; `--reset`
   clears; different icon token → Remove+Create observed through the fake (new Id,
   steps gone).
6. Resurrection: a within-TTL tombstoned-handle update re-creates with stored core
   info (including subtitle + deepLink), moves the entry live, applies the update;
   past TTL → clean no-op; never-seen handle → clean no-op FOR THE FIVE update-class
   verbs (`step`/`state`/`done`/`fail`/`attention`). `start` instead CREATES on a
   never-seen handle (ERGO-25/ERGO-27 create-or-adopt; ratified 2026-07-08). Recycle
   bin is only read on the miss path.
7. Every operation acquires `WriteGate` exactly once around its full RMW (assert via
   the fake's interleave hook: concurrent ops through operations code never lose
   writes).

## Out of scope

Argument parsing / verb surface (phase 08), sweeps triggering (phase 08 wires
ERGO-2 sweep points), icon rendering (phase 07 — operations consume an
`iconUri` the pipeline hands them).
