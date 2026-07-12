# LIFE-16: Watchdog granularity / scope
**Status:** DECIDED
**Plan:** phase-09
**Decision:** One watchdog per package identity (PFN), not per handle -- a single,
stateless-over-disk poller. Each tick it re-derives all liveness state from disk (per-handle
`lastUpdate` in the sidecar directory + `HiddenByUser` in `tasks.json`) and holds no
in-memory timer state, so a respawn reconstructs everything. Poll only, no filesystem
watcher: the watchdog is an absence detector (idle expiry) and there is no FS event for
"nothing happened"; the sole edge signal (`HiddenByUser`) is latency-insensitive and already
swept opportunistically (ERGO-2, "Garbage collection of orphaned / user-hidden AppTaskInfo
entries"). This gives LIFE-18 ("Watchdog single-instance enforcement") one mutex and LIFE-17
("Watchdog spawn mechanics") one spawn check, and keeps the dev-loop blast radius minimal
(INFRA-19/20). Per-handle rejected: N processes / spawn-checks / instance-mutexes and N x
dev-loop noise, bought only trivially-scoped shutdown.
**Parent:** LIFE-6

One watchdog per package identity, or one per handle/session? Per-identity: a single
process supervising every session's tasks (one spawn check, one poller, but it must
track N idle-expiry timers and outlive any one session). Per-handle: N processes with
trivially-scoped shutdown, but N spawn/instance checks and more dev-loop noise
(INFRA-18, "Handling 'Watchdog' background process during active development").
Decide first -- LIFE-17/18/19 are parameterized by this scope. Note: INFRA-16
("Test-time identity provisioning and deep isolation") already gives per-worktree
identities, so a per-identity watchdog is isolated across concurrent test runs for
free.
