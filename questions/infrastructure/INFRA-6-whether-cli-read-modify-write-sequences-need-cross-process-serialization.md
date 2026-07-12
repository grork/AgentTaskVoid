# INFRA-6: Whether CLI read-modify-write sequences need cross-process serialization
**Status:** DECIDED
**Plan:** all-phases
**Decision:** Yes, and the lock must be GLOBAL (per-identity), not per-handle.
INFRA-5 proved contention is file-wide -- concurrent writes to *different* tasks
already lose data -- so a per-handle lock is insufficient. Every CLI write
(Create/Update/UpdateState/Remove) acquires one system-wide named mutex scoped to
the package identity, held across the read-modify-write. Proven: 4x100 concurrent
creates go from 37/400 (no lock) to 400/400 (global mutex).
**Parent:** INFRA-1

Incremental update verbs (see ERGO-8) imply a read-modify-write spanning API
calls: e.g. `GetCompletedSteps` -> append -> `Update`. Two interleaved CLI
invocations can lose steps even if each individual API call is safe. Does the
CLI need its own cross-process serialization (named mutex, lock file), and
which mechanism stays simple and debuggable?

Operator direction (2026-07-02): prefer NO lock -- avoid it by design if we can.
Same-handle concurrency is rare (a session's hooks are mostly serialized), the
lost datum is a single ephemeral step (capped 10 FIFO anyway), so last-writer-wins
is acceptable *provided the API itself does not corrupt* under concurrency. That
proviso is exactly INFRA-5 -> BLOCKED on it: if INFRA-5 shows atomic/safe writes
(worst case = last-writer-wins), go lock-free; if it shows corruption, revisit a
minimal per-handle lock.

Decision detail (2026-07-02): INFRA-5 disproved the "lock-free is fine" proviso --
last-writer-wins here silently eats *other sessions'* tasks (37/400 survived),
not just a rare same-handle step, which breaks the multi-consumer core scenario.
Hence a mandatory GLOBAL lock. Bonuses: the global lock lets the API write + the
ERGO-7 sidecar update run as one critical section (atomic together), and the
watchdog's Remove (LIFE-5) must take the same lock. Contention latency is
INFRA-12 (negligible at real hook rates). The seam that encapsulates
"take lock -> read-modify-write -> release" is INFRA-8.

Lock design (2026-07-02):
- Mutex-per-invocation chosen over a queue + background worker. The queue-worker
  makes invocations non-blocking but breaks the synchronous "did it work?"
  contract (FAIL-2/3 -- you return ok before the write lands, and the caller can
  never learn it was lost) and is throughput-overkill at real write rates
  (~1-2/sec). The mutex stays simple, debuggable, and honest.
- The watchdog (LIFE-4) SHARES this mutex as a supervisor; it must NOT become a
  write-broker that invocations funnel through (that reintroduces the
  queue-worker's broken contract).
- Bounded wait: block the host only up to a small budget; on timeout,
  non-disruptive mode logs + skips (FAIL-1), strict surfaces it -- a wedged writer
  can never hang a hook. Handle AbandonedMutexException (holder crashed mid-write):
  proceed (no corruption ever observed) + log.
- Open specifics (build-phase / tracked): the exact bounded-wait timeout value,
  and true atomicity of the API write + sidecar write (ERGO-21).
- Name: session-scoped `Local\`, namespaced to the unique package family name +
  purpose, e.g. `Local\<brand>-<PFN>-tasks-write`. No meaningful security exposure:
  all writers are same-user / same-session / mediumIL, so the classic
  higher-vs-lower-privilege named-object squat does not apply, and a mutex carries
  no data path. Worst case is a same-user squatter holding it -> "status stops
  updating" (cosmetic), never compromise. Prefer `Local\` over `Global\` (narrower
  exposure, and all writers share one session anyway).
  Ratified 2026-07-05 (review pass): `Local\` is per-TERMINAL-SERVICES-SESSION, not
  per-user, while tasks.json/app-data are per-user -- so a same-user second TS session
  (e.g. console processes left running + a fresh RDP session) would write outside this
  mutex, reopening the INFRA-5 loss window cross-session. Accepted deliberately:
  interactive logins adopt the existing session on reconnect, making concurrent
  same-user sessions rare at our scale; and the platform's own unserialized
  whole-file last-writer-wins behavior caps what our serialization could guarantee
  cross-session anyway. Known lever if wide adoption ever surfaces it:
  `Global\<brand>-<PFN>-<user-SID>-...` naming. LIFE-18 ("Watchdog single-instance
  enforcement") inherits this scope.
