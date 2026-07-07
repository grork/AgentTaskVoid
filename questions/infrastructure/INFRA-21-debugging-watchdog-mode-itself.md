# INFRA-21: Debugging watchdog mode itself
**Status:** DECIDED
**Parent:** INFRA-18
**Decision:** The watchdog is ONE shared logic core behind a thin hosting seam, so
spawned-process, in-proc-thread, and test hosts all run identical logic:
- `WatchdogLoop` -- the supervision logic (poll -> derive liveness from disk -> expire/reap
  -> decide-shutdown). Stateless-over-disk (LIFE-16, "Watchdog granularity / scope"), so a
  tick is a pure function of (disk state, clock). ONE implementation.
- `EnsureWatchdog` -- the decide-to-run logic (mode + `OpenMutex` liveness -> spawn |
  in-proc | nothing). ONE implementation, host-agnostic.
- `IWatchdogHost` -- the ACT of spawning, the only thing that differs by INFRA-19
  ("Inner-loop watchdog suppression") mode: `ProcessHost` (LIFE-17, "Watchdog spawn
  mechanics", detached process), `InProcThreadHost` (background thread running the same
  `WatchdogLoop`), `FakeHost` (tests). The LIFE-18 ("Watchdog single-instance enforcement")
  acquire-or-exit lives in the shared loop, not the host, so `inproc` and `spawn` are
  behaviorally identical.

Debuggability = two launchSettings.json profiles: a "watchdog (foreground)" profile whose
command runs `ProcessHost`'s entry (`atv watchdog`) so the loop is a direct F5 target with
breakpoints; and an "app + spawn" profile (`WATCHDOG_MODE=spawn`) to exercise the real
detached spawn path on demand. The default profile sets `WATCHDOG_MODE=off` (INFRA-19 /
INFRA-20). Attach story for a watchdog spawned by a real invocation: an opt-in
`--wait-for-debugger` env/flag makes `ProcessHost`'s child spin at startup until attached.

Testing (closes the coverage gap default-off opens):
- `EnsureWatchdog` and `WatchdogLoop` get strong UNIT coverage via `FakeHost` + fake clock +
  fake API + injected dir -- validating decide-to-spawn and every tick behavior with NO real
  process (INFRA-11, "Test strategy for machines where the API is unavailable").
- Only the thin `ProcessHost` seam is left unexercised by unit tests, so exactly ONE
  real-adapter integration test (INFRA-9 / INFRA-16, isolated by per-worktree identity)
  drives it end-to-end: a real detached process spawns, acquires the single-instance mutex,
  survives parent exit, and self-exits when the supervised set empties. REQUIRED test --
  without it the process mechanics rot silently, since the dev inner loop runs `off`.

The buried second question, extracted: launch profiles / launchSettings.json so the
watchdog is directly F5-able as its own debug target, plus the attach story for a
watchdog spawned by a real CLI invocation (LIFE-17, "Watchdog spawn mechanics").
