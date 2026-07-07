# ERGO-2: Garbage collection of orphaned / user-hidden AppTaskInfo entries
**Status:** DECIDED
**Decision:** User-hidden (`HiddenByUser`) tasks are swept and `Remove()`'d on
every `create` and `remove` invocation; truly-orphaned/dead-session cleanup is
the watchdog's job (LIFE-4).

How / can we / should we handle 'garbage collection' of orphaned AppTaskInfo
from us -- should we enumerate on every invocation and clean up the ones that
have been hidden by the user (seems sensible)?

Decision detail (2026-07-02): Two distinct cases, split by how they are detected:
- Deterministic (user-hidden): `HiddenByUser` is a reliable flag on `FindAll()`.
  Sweep and `Remove()` these on every `create`/`remove` invocation -- cheap and
  natural at those points. Whether `update` should also sweep is ERGO-19.
- Inferred (truly orphaned / dead session): no reliable flag; requires liveness
  inference -> owned by the idle-expiry watchdog (LIFE-4), not this sweep.
