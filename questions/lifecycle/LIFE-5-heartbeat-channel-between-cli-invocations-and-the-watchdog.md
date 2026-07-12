# LIFE-5: Heartbeat channel between CLI invocations and the watchdog
**Status:** DECIDED
**Plan:** phase-04
**Decision:** No IPC. The watchdog is a poller of files the CLI already writes:
the CLI-owned sidecar (ERGO-7) for per-handle liveness (a lastUpdate timestamp
stamped on every write, under the mutex) to drive idle expiry, and tasks.json
(HiddenByUser) for the shut-down-on-hide signal. [AMENDED 2026-07-07: the
task-state/hide side is read through the store seam's `FindAll()` -- no production
raw tasks.json reader; raw reading stays test-only (INFRA-9 adapter suite).]
Simplest, most resilient, most
debuggable -- no pipe/channel to build or keep alive. Exact poll interval and
timer mechanics are part of the LIFE-6/7 watchdog-design expansion. The sidecar's
own design (the `lastUpdate` field, schema, atomicity) is ERGO-21; the poll
interval is a build-phase tuning value.
**Parent:** LIFE-1

If a watchdog exists (LIFE-4), how do CLI invocations feed it liveness signals:
real IPC (e.g. named pipe), a watch directory of files the CLI touches, or
reading timestamps/state already present in the API's own `tasks.json`? Weigh
resilience, debuggability, and implementation simplicity.

Operator direction (2026-07-02): the channel must carry two things, not a bare
heartbeat: (1) update/state-change pings that postpone the watchdog's idle expiry
(LIFE-7), and (2) the user-hide signal so the watchdog can shut down when the
user X's a task (LIFE-6). Both `HiddenByUser` and update timestamps are already
readable from `tasks.json`/`FindAll()`, which tilts toward the watchdog polling
`tasks.json` over a dedicated named pipe -- but weigh that against
resilience/debuggability per the question. Still OPEN.

Supporting evidence (Sonnet host research, 2026-07-02): drive the heartbeat off
pre/post-tool events (reliable on all three hosts), not session-end (unreliable
on Claude Code, absent on Codex -- LIFE-12/13/14). Every host also stamps a
session_id into each event, so the ping can carry the handle directly (ERGO-6).
