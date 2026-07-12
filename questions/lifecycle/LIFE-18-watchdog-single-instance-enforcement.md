# LIFE-18: Watchdog single-instance enforcement
**Status:** DECIDED
**Plan:** phase-09
**Decision:** A named mutex `Local\<brand>-<PFN>-watchdog` (the INFRA-6, "Whether CLI
read-modify-write sequences need cross-process serialization", naming scheme), held for the
watchdog's whole lifetime. Startup race: two first-creates may both spawn; both watchdogs try
to acquire on startup and the loser exits cleanly without disturbing the winner. `Local\` (not
`Global\`), consistent with INFRA-6 -- a multi-session same-user box (RDP / fast-user-switch)
therefore gets one watchdog per session over the shared `tasks.json`. Reaps are idempotent, and
the residual cross-session write overlap is accepted per INFRA-6's ratified `Local\` scope
(2026-07-05: concurrent same-user TS sessions are rare -- interactive logins adopt the existing
session on reconnect -- and the platform's own unserialized whole-file writes cap what
serialization could guarantee cross-session anyway).
**Parent:** LIFE-6

What guarantees exactly one watchdog per LIFE-16 scope -- presumably a named mutex
following the INFRA-6 ("Whether CLI read-modify-write sequences need cross-process
serialization") naming scheme (e.g. `Local\<brand>-<PFN>-watchdog`), held for the
watchdog's lifetime -- and the startup race: two concurrent first-creates both decide
to spawn; the loser must detect and exit cleanly without disturbing the winner.
