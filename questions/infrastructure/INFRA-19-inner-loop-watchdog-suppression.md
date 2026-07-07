# INFRA-19: Inner-loop watchdog suppression
**Status:** DECIDED
**Parent:** INFRA-18
**Decision:** A single `watchdog-mode` setting -- `spawn` | `inproc` | `off` -- resolved
through ERGO-17 ("Configuration surface for recurring defaults") precedence (flags > env >
config > default). Values:
- `spawn` (prod default) -- LIFE-17's ("Watchdog spawn mechanics") detached windowless
  process.
- `inproc` -- the watchdog loop on a background thread bound to the invoking process's
  lifetime. NOT a production supervision equivalent (a 50ms `atv step` gives 50ms of
  supervision); it is a dev/debug hosting mode whose point is no detached child to lock the
  exe or sprawl. Dies with the process, so shift-F5 takes it too.
- `off` -- no watchdog at all (also for INFRA-9/16 real-adapter test runs that don't want a
  supervisor perturbing assertions).

No implicit debugger-sniffing (amended): the CODE default is `spawn` and suppression is
always EXPLICIT. The dev inner loop is turned off by the default launch profile's
`WATCHDOG_MODE` env var (INFRA-21, "Debugging watchdog mode itself"), which covers F5,
Ctrl+F5, and `dotnet run` (it honors the profile) -- NOT by the code detecting a debugger.
So behavior depends only on explicit config, never on ambient state. The mode gate sits at
the invoker BEFORE LIFE-17's ("Watchdog spawn mechanics") `OpenMutex` -> spawn check, and
selects the INFRA-21 host seam (`ProcessHost` / `InProcThreadHost` / none).

Forced-short idle-expiry is secondary, not a new mechanism: dev profiles just set a short
idle-timeout via the existing ERGO-17 config.

What keeps dev runs from spawning background watchdogs: a disable flag/env/config
(slotting into ERGO-17's ("Configuration surface for recurring defaults") precedence
layers), debugger-attached detection, a forced-short idle-expiry -- and whether an
"in-proc" watchdog mode exists at all (that variant interacts with LIFE-17, "Watchdog
spawn mechanics").
