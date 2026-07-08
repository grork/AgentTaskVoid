# Phase 04: Persistence — global write mutex, sidecar store, recycle bin

**Depends on:** phase 02 (seam + fake)
**Unblocks:** phases 05, 07, 08, 09, 10

## Goal

Build the three cross-invocation state mechanisms: the global per-identity write
mutex that serializes every read-modify-write, the per-handle sidecar index
(handle → Id + liveness stamp), and the cold recycle-bin store that expiry and
resurrection use. All are plain, unit-testable code above the seam.

## Decisions implemented

### Write mutex (INFRA-6, "Whether CLI read-modify-write sequences need cross-process serialization")

- Mandatory GLOBAL (per-identity) lock — INFRA-5 proved the platform does not
  serialize cross-process writes and silently loses ~91% under contention; a
  per-handle lock is insufficient (contention is on the whole `tasks.json`).
- One system-wide named mutex `Local\<brand>-<PFN>-tasks-write` (PFN derived at
  runtime, brand from the ERGO-18 constant). `Local\` is the ratified scope; the
  cross-TS-session write overlap is deliberately accepted — do not "fix" it.
- Held across the whole read-modify-write: the API write AND the sidecar write run
  as one critical section.
- Mutex-per-invocation (no queue/background broker — that breaks the synchronous
  "did it work?" contract). The watchdog SHARES this mutex as a supervisor; it must
  never become a write broker.
- Bounded wait: block only up to a small budget (default ~2 s; make it a tunable
  consumed from phase 06 config). On timeout: non-disruptive mode logs + skips
  (FAIL-1); strict surfaces it. On `AbandonedMutexException` (holder crashed
  mid-write): proceed (no corruption ever observed) + log.
- Not abstracted (INFRA-8): write paths receive a `System.Threading.Mutex` from the
  composition root — production named, tests unnamed/unique (an unnamed mutex can
  still be abandoned in-proc for testing).

### Sidecar store (ERGO-21, "The sidecar store design"; ERGO-7)

- An INDEX, not an allowlist: authoritative for NOTHING about task existence (the
  API stays source of truth).
- Topology: a directory of PER-HANDLE files in package app-data
  (`ApplicationData.Current.LocalFolder`), each `handle → {id, lastUpdate,
  schemaVersion}` and nothing more (no cached content, no group/owner/cwd). Written
  by atomic temp-file + rename. Filenames use a REVERSIBLE percent-encoding of the
  caller-supplied handle (ratified 2026-07-07) — never the raw string, never a
  one-way hash: `list` (phase 10) and the watchdog's recycle-bin records (phase 09)
  must recover the handle from an entry.
- `lastUpdate` is stamped on EVERY write, under the mutex — it is the liveness
  heartbeat the watchdog polls (LIFE-5, "Heartbeat channel"). Wall-clock time.
- Testing seam: inject a directory path (prod = LocalFolder subdir, test = temp
  dir). No interface.
- Reconciliation against `FindAll()` — under the mutex, NON-DESTRUCTIVE. Scoped
  (ratified 2026-07-07): the FULL four-rule pass runs only on `start`/`remove`/
  `clear` and watchdog ticks; update-class verbs (`step`/`state`/`done`/`fail`/
  `attention`) resolve ONLY their own handle — rules 1–2 applied to that one handle,
  no `FindAll()`, no sweep (keeps the hot path lean per ERGO-19):
  1. entry present, API knows `id`, not hidden → keep;
  2. entry present, API no longer knows `id` → drop entry (our stale mapping);
  3. API `id` is `HiddenByUser` → `Remove()` + drop entry (the ERGO-2 sweep);
  4. API `id` with NO entry (entryless) → leave alone on the invocation hot path —
     watchdog territory (LIFE-23, phase 09).
  Consequence: an update whose entry was dropped is a clean unknown-handle
  no-op/fail (no upsert-on-step; the recycle-bin miss path in phase 05 is the only
  exception).
- `create` ordering: API-first (need the returned `Id` before writing the file). No
  journaling; the "API create landed, sidecar write crashed" divergence is an
  entryless orphan handled by the watchdog.
- Entry-drop and tombstone paths built here are the designated extension points
  phase 07 attaches per-handle icon cleanup to (ERGO-23 twinning); this phase ships
  them icon-unaware.

### Recycle bin (LIFE-21, "What expiry does" — the store; expiry itself is phase 09)

- A cold folder in package app-data. NEVER enumerated on the hot path; consulted
  only on the miss path (update whose handle is absent from the live sidecar).
- Record = `{handle, title, subtitle, icon-ref, deepLink, whenTombstoned}` — nothing
  mutable (steps/state restart fresh). Icon assets co-locate with the record when
  tombstoned (single-owner move model, ERGO-23 — the move mechanics land with the
  icon pipeline, phase 07; define the folder contract here).
- TTL ~1 day (tunable via config, phase 06). Purge = opportunistic scavenge folded
  into existing sweeps (drop records older than TTL, deleting the co-located icon
  with the record). Provide the scavenge helper here; phases 08/09 call it.

## Files affected

```
src/Atv/Persistence/WriteGate.cs        # acquire/bounded-wait/abandoned/release, written once
src/Atv/Persistence/SidecarStore.cs     # per-handle files, atomic replace, enumeration
src/Atv/Persistence/SidecarEntry.cs     # {id, lastUpdate, schemaVersion}
src/Atv/Persistence/Reconciler.cs       # the four rules, returns what it kept/dropped/swept
src/Atv/Persistence/RecycleBin.cs       # record read/write/move-to-live, TTL scavenge
src/Atv/Persistence/AppPaths.cs         # runtime-derived app-data paths (sidecar dir, recycle dir, icons, log, config) — brand/PFN parameterized
tests/Atv.LogicTests/Persistence/*      # see below
```

## Acceptance criteria (fake-backed logic tests, written first)

1. Mutex: with the fake's interleave hook, an unprotected concurrent
   read-modify-write shows deterministic loss; the same sequence through `WriteGate`
   shows none. Abandoned-mutex path proceeds and logs. Timeout path skips
   non-disruptively (assert no exception escapes) and logs.
2. Sidecar: write/read round-trip; atomic replace never yields a torn file; handle
   encoding round-trips hostile handles (path separators, unicode, long strings);
   `lastUpdate` refreshes on every write.
3. Reconciliation: a data-driven test covers all four rules using the fake's drift
   hooks (vanish, HiddenByUser, seed-unknown), asserting entryless tasks are LEFT
   ALONE by reconciliation. Per-handle resolution (the update-class scope) performs
   no `FindAll()` and never sweeps — assert it.
4. Recycle bin: record round-trip; miss-path lookup finds within TTL and misses past
   it; scavenge deletes only expired records (+ co-located assets); hot-path code
   never touches the folder (assert by construction/tests).
5. All tests run with temp-dir injection, parallel-safe, no identity required.

## Out of scope

Expiry decisions and entryless reaping (phase 09), resurrection logic (phase 05),
icon move mechanics (phase 07).
