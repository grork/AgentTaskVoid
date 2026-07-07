# LIFE-23: Entryless-orphan reaping and the mass-deletion guard
**Status:** DECIDED
**Parent:** LIFE-7
**Decision:** Reap ALL entryless tasks, unconditionally -- NO mass-deletion guard. Entryless
tasks are scoped to our own package identity (per-identity `FindAll`; DIST-3, "Dev vs release
identity (PFN) divergence", isolation means dev/test can never touch release), so reaping
only ever removes OUR cards. Leaving them is worse: orphaned cards nobody can address. A
wiped/corrupt sidecar therefore sweeps all our live cards by design -- a visible wipe we can
diagnose is preferred to a taskbar of unaddressable turds. This explicitly SUPERSEDES
ERGO-21's ("The sidecar store design") "LIFE-7 MUST carry a guard" constraint.

- Audible, not silent: the watchdog LOGS entryless reaps (with a count) to the FAIL-3
  ("Diagnosability when nothing shows on the taskbar") durable log, so a bulk reap leaves a
  breadcrumb -- "we want to hear it". The log is a breadcrumb, never a gate; it does not
  block the reap.
- Safe against the create race: ERGO-21's mutex ordering means a legit in-progress create is
  never seen as entryless -- the API-created-but-sidecar-not-yet-written state exists only
  while the create invocation holds the INFRA-6 mutex, which excludes the watchdog's reap
  tick. So immediate reap cannot nuke a fresh card; only genuine orphans (crashed sidecar
  write, dev `probe` / pre-sidecar leftovers) are ever entryless.

The ERGO-21 ("The sidecar store design") handoff, extracted whole: does the watchdog
reap live API tasks that have NO sidecar entry (the rare "create landed, sidecar
write crashed" orphan, dev `probe` runs, pre-sidecar leftovers) at all? If yes, the
mass-deletion guard is mandatory: an entryless task is indistinguishable from a live
task whose entry was lost, so a wiped/corrupt sidecar (tasks.json surviving) would
otherwise nuke every live card in one sweep. Guard shape to decide: refuse to reap
when the sidecar dir is absent/empty or the unknown fraction is suspiciously high --
log instead (FAIL-3, "Diagnosability when nothing shows on the taskbar").
