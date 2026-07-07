# LIFE-6: Watchdog lifecycle mechanics
**Status:** EXPANDED
**Expanded into:** LIFE-16, LIFE-17, LIFE-18, LIFE-19, LIFE-20 (expansion also
surfaced DIST-6, "Package upgrade while the watchdog is running" -- adjacent new
question, not a child)
**Parent:** LIFE-1

How is the watchdog started (auto-spawned by the first CLI invocation?), how is
a single instance enforced, when does it exit, and what happens across
logoff/reboot -- tasks persist across reboots but the watchdog process will
not?

Expansion seeds (operator, 2026-07-02) -- LIFE-4 decided an idle-expiry watchdog
exists; the lifecycle grew too complex to answer in one shot:
- Spawn trigger: on task creation (first create for a given scope?).
- Shutdown conditions -- any of: task reaches completion/terminal state; the idle
  timeout elapses with no updates (LIFE-7); the user hides the task
  (HiddenByUser observed -> user stopped caring).
- Granularity: one watchdog per task, or one per group / session / package
  identity? (interacts with grouping, ERGO-4.)
- Single-instance enforcement mechanism.
- Logoff/reboot: tasks persist, the watchdog does not -- what re-establishes
  supervision (or cleans up) after a reboot with tasks still present?
- Write coordination: the watchdog shares the global write mutex (INFRA-6) as a
  supervisor; it must not become a write-broker all invocations funnel through
  (that reintroduces the queue-worker's broken sync contract).
