# ERGO-19: Should `update` invocations also trigger the user-hidden GC sweep?
**Status:** DECIDED
**Plan:** phase-08
**Decision:** No. `update` (the hot path -- every `step`) does not sweep. Rely on
the create/remove sweeps (ERGO-2) plus the watchdog, which polls tasks.json and
catches user-hidden tasks during a long-running session. Keeps the most frequent
operation lean.

ERGO-2 sweeps user-hidden tasks on `create`/`remove`. Should `update` also
sweep? Create/remove are natural, infrequent sweep points; sweeping on every
`update` adds a `FindAll()` (+ possible `Remove()`) to the hot update path,
which touches per-invocation latency (INFRA-12) and update races (INFRA-6).
Decide: sweep on update, throttled/periodic sweep, or never.

Decision detail (2026-07-02): adding a FindAll (+ possible Remove) to every step
would tax the highest-frequency verb for a case the watchdog already covers.
Hidden tasks get cleaned on the next create/remove or by the watchdog sweep.
