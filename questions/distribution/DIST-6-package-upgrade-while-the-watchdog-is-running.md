# DIST-6: Package upgrade while the watchdog is running
**Status:** DECIDED
**Decision:** No upgrade-specific handling in the watchdog -- decision unchanged; reasoning
CORRECTED 2026-07-05 with researched servicing behavior (the original force-kill-vs-block
framing below was speculative, and its block branch misstated LIFE-19, "Watchdog shutdown
conditions" -- the watchdog exits on an EMPTY supervised set, not a general idle timer).

Researched behavior (2026-07-05, Sonnet research agent; MS Learn deployment docs + the
winget-cli source, InstallFlow.cpp / Deployment.cpp):
- Default MSIX deployment does NOT force-kill running package processes; it fails with
  `ERROR_PACKAGES_IN_USE` (`0x80073D02`). No Restart Manager integration, no auto-restart.
- winget (our channel, DIST-1, "The end-user distribution vehicle") never passes the
  force-shutdown options. On `ERROR_PACKAGES_IN_USE` it falls back to
  `StagePackageAsync` + best-effort register; if registration is still blocked it reports
  "registration deferred" and EXITS SUCCESSFULLY. The version swap then completes
  transparently once the package's processes have exited.

Why the decision stands, on the corrected reasoning:
- `winget upgrade` with a live watchdog neither kills it nor fails: new files stage,
  registration defers. The old-version watchdog keeps supervising (stateless-over-disk,
  LIFE-16, "Watchdog granularity / scope"); when the supervised set empties it self-exits
  (LIFE-19), the pending registration flips, and the next write-path `atv` spawns the
  new-version watchdog (LIFE-17, "Watchdog spawn mechanics"). No retry, no corruption, no
  unsupervised window.
- Residual: a busy session postpones the version swap until its tasks quiesce (hours, if a
  NeedsAttention card is holding its LIFE-22 idle period) -- accepted; update-detection
  self-exit stays the known lever if pending-update latency ever matters. Update-while-
  running is a small-scale scenario in the first place (operator, 2026-07-05).
- Research aside for that lever: non-UWP processes are NOT auto-restarted after a forced
  shutdown (they'd need `RegisterApplicationRestart`) -- irrelevant here because respawn is
  LIFE-17's ensure-spawn, not an OS restart.
- The deferred-registration behavior gets a free confirmation at the first real packaged
  upgrade (same boundary as DIST-8, "The joined release-leg spike") -- no standalone spike.
  The dev inner loop hits the same locked-exe class but that is INFRA-18/20 tooling, not
  this.

`winget upgrade` (DIST-1's channel) while a watchdog from the old package version is
still running: does the running packaged process block MSIX servicing, or does
servicing force-terminate it mid-supervision -- leaving tasks unsupervised until
something respawns it (LIFE-20, "Logoff/reboot recovery", is the adjacent gap)?
Surfaced while expanding LIFE-6 ("Watchdog lifecycle mechanics"). The dev inner loop
hits the same locked-exe class (INFRA-18, "Handling 'Watchdog' background process
during active development & inner loop").
