# ERGO-7: Whether the CLI keeps persistent state of its own
**Status:** DECIDED
**Plan:** phase-04
**Decision:** Yes -- a small persisted `handle -> AppTaskInfo.Id` (+ metadata)
sidecar, reconciled against `FindAll()` each invocation; the API remains source
of truth for existence. Details below.
**Parent:** ERGO-1

A caller-chosen handle (ERGO-6) needs a handle->task mapping somewhere. Can the
CLI stay stateless over the API (`FindAll()` + `Id`), or does it need its own
persisted mapping -- and if so, where does that live and what keeps it in sync
with the tasks the API actually knows about?

Operator clarification (2026-07-02): the brief's 'mostly stateless' describes
the API, not a design constraint -- CLI-side persisted state is acceptable
where it earns its keep.

**Decided (2026-07-02):** Yes -- the CLI keeps a small persisted
`handle -> AppTaskInfo.Id` (+ metadata: group key, owner, cwd) sidecar,
reconciled against `FindAll()` on each invocation. The API stays the source of
truth for task existence; the sidecar only adds what the API cannot store.
- Location: a sidecar file in the package's local app data (near the API's own
  `tasks.json` under the package identity). Directly inspectable for debugging,
  matching the API's own file-as-interface style.
- Sync: on each invocation, reconcile against `FindAll()` -- drop entries whose
  `Id` is gone, prune user-hidden ones (ERGO-2). The sidecar never caches content
  (titles/steps/state); that lives in the API (ERGO-8).
- The sidecar is shared mutable cross-process state, so its read-modify-write
  needs serialization -- owned by INFRA-6.
- The concrete sidecar design -- schema, exact reconciliation rules, and atomicity
  with the API write -- is ERGO-21 (extracted so it isn't buried here).
