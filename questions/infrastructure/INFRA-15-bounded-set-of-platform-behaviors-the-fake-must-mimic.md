# INFRA-15: The bounded set of platform behaviors the fake must mimic
**Status:** DECIDED
**Plan:** phase-02
**Decision:** A TIGHT list, gated by "a specific logic test would test the wrong thing
without it." The fake MUST mimic exactly four: (1) non-atomic whole-store clobber /
last-writer-wins with a deterministic interleave hook (INFRA-5, "Empirical behavior of
concurrent API calls from separate processes"); (2) `HiddenByUser` surfacing (ERGO-2,
"Garbage collection of orphaned / user-hidden entries"); (3) out-of-band task
disappearance + direct-seed of a task the logic didn't create, with unknown-Id ops
returning a clean not-found (ERGO-21, "The sidecar store design"); (4) platform mints
an opaque `Id` on create. It MUST NOT mimic the crash matrix, Shell rendering /
grouping, the file-watcher, or timing/latency. Confirmation that the real platform
still exhibits each is folded into the INFRA-13 ("Windows build compatibility strategy")
new-build checklist -- some automated in the real-adapter suite, some manual -- never a
routine gate.

Decision detail (2026-07-03):
- MUST mimic, and the logic test each one makes meaningful:
  1. Non-atomic whole-store clobber (flagship, INFRA-5) -- fake is deliberately
     non-atomic with an injectable "another writer committed between your read and your
     write" hook. Unprotected write -> deterministic loss; mutex-wrapped -> no loss.
     The only thing that proves the INFRA-6 ("Whether CLI read-modify-write sequences
     need cross-process serialization") mutex mitigation works.
  2. `HiddenByUser` surfacing (ERGO-2) -- a per-task flag a test sets, surfaced through
     enumerate. Proves the create/remove sweep AND the ERGO-21 reconciliation drop.
  3. Out-of-band disappearance + seed-unknown-Id (ERGO-21) -- drift hooks to delete a
     task behind the logic's back and to seed a task the logic never created (the "API
     knows it, sidecar doesn't" entryless state); ops on a vanished Id return not-found,
     never crash. Exercises the reconciliation matrix (keep / drop / sweep / leave-alone).
  4. Platform mints an opaque `Id` on create -- so the handle->Id mapping can't secretly
     assume an id format.
- MUST NOT mimic (anti-overshoot guardrails):
  - crash-on-bad-combo / state x content matrix -- INFRA-10 ("Testing behavior only
    observable in Shell rendering") already decided the fake must not model this;
  - Shell rendering / grouping-by-IconUri -- ERGO-13 ("Empirical: is grouping keyed on
    the exact icon URI string?");
  - explorer.exe file-watcher coalescing / live re-render -- INFRA-7 ("Explorer
    file-watcher behavior under rapid successive updates");
  - latency / timing / exact COMException codes / full parity -- latency is INFRA-12
    ("Per-invocation / per-operation latency baseline & budget"), measured on the real
    thing.
- Negative-only obligation (not a mimic): whole-content replacement on write is
  structural -- the INFRA-8 ("The seam between CLI logic and the WinRT API") seam is
  DTO-in/DTO-out, so replacement is free (ERGO-8, "Update verbs for ergonomic revision
  given whole-content replacement"). The fake must simply NOT add a convenience
  merge/append.
- Explicitly NOT included for now: a generic error-injection mode. It is test
  infrastructure for the FAIL-1 ("Failure posture toward the host caller")
  non-disruptive path, not a platform behavior to mimic; revisit at test-build time.
- Confirmation the real platform still does each (periodic, not a gate; folded into the
  INFRA-13 new-build checklist -- when a check fails, fix BOTH the fake's mimic and this
  list / the INFRA-10 matrix data):
  - Automated in the real-adapter suite (INFRA-9, "Integration-test harness over
    tasks.json"; only when API present): unknown-Id / removed-Id behavior, and the
    negative whole-content-replacement check.
  - Manual "dark matter" (INFRA-13, low-pri): the demoted 4x100 multi-process clobber
    run, and the `HiddenByUser` SETTER-on-gesture (only the real user X-gesture fires
    the real setter; the READ path -- our code surfacing a flag present in tasks.json --
    is automatable via hand-authored fixtures, INFRA-9).
- Lives as a documented "fidelity promises" contract on `FakeAppTaskStore`, each item
  tagged with its confirming check -- one source of truth, referenced by the INFRA-13
  checklist.

The fake is NOT a stand-in for real API behavior (the real adapter suite, INFRA-9,
proves that directly), so full parity is not the goal. What remains: the fake must
reproduce the SPECIFIC platform behaviors a logic test depends on, and we must confirm
the real platform still exhibits them. Flagship: non-atomic whole-store clobber /
last-writer-wins (INFRA-5) -- the mutex mitigation regresses against it. Others to
enumerate: `HiddenByUser` surfacing (sweep, ERGO-2), and task-existence/reconcile
semantics a logic test leans on (ERGO-21).

Open to answer: (1) the concrete list of behaviors the fake promises to mimic; (2) how
each is confirmed still-true on the real platform -- a periodic reality-check tied to
the INFRA-13 new-build verification (the demoted 4x100 multi-process run is one such
check), NOT a routine gate.

Operator note (2026-07-03): don't overshoot -- goal is "enough fidelity that the logic
tests mean something," not provable equivalence; weigh against the guard's cost.
Spawned by INFRA-11.
