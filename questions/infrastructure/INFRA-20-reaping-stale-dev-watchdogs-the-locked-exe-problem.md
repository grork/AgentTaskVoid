# INFRA-20: Reaping stale dev watchdogs / the locked-exe problem
**Status:** DECIDED
**Parent:** INFRA-18
**Decision:** Two layers. (1) The INFRA-19 ("Inner-loop watchdog suppression") default
launch profile sets `WATCHDOG_MODE=off`, so IDE launches (F5 / Ctrl+F5) and `dotnet run`
spawn NO detached watchdog -- removing the dominant source of locked exes entirely. (2)
Residual = a watchdog spawned outside a suppressing profile (a terminal `dotnet run` in
`spawn` mode, a crash, or a shift-F5'd spawn profile): covered by LIFE-19 ("Watchdog
shutdown conditions") natural self-exit (exits when the supervised set empties) as the
passive backstop, PLUS a pre-build MSBuild reap target that kills THIS worktree's watchdog
(by its INFRA-16, "Test-time identity provisioning and deep isolation", per-worktree PFN
mutex) before compile, so a locked exe never blocks a rebuild. INFRA-16's per-worktree
identities already bound the blast radius to one worktree. The hosted/reaped process is the
INFRA-21 ("Debugging watchdog mode itself") `ProcessHost` seam.

Shift-F5 and crashed runs leave watchdogs holding exes that block rebuilds. What
cleans them up: a build target, or the watchdog self-exiting when its scope naturally
vanishes (the "lean on natural shutdown" seed -- sidecar removed, loose-layout
replaced beneath it). Note INFRA-16's ("Test-time identity provisioning and deep
isolation") per-worktree identities already bound the blast radius to one worktree.
