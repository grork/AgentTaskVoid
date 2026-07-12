# LIFE-22: Idle-period defaults per state, and configurability
**Status:** DECIDED
**Plan:** phase-09
**Decision:** Per-state idle periods (relative-expiry from `lastUpdate`), not one number --
concrete starting defaults, all build-phase tunable and layered per ERGO-17 ("Configuration
surface for recurring defaults") -- env/config only, NO per-task flag [AMENDED 2026-07-05, ERGO-27
("The consolidated v1 command surface"): the per-task `--idle`/`--linger` override is DROPPED -- a
per-task value has nowhere to persist for the stateless-over-disk watchdog (the ERGO-21 sidecar
schema is `{id,lastUpdate,schemaVersion}`); the per-state defaults below stand]:
- **Running** ~30 min -- must outlast the longest realistic SINGLE tool call (a big build as
  one hook fire leaves no writes mid-run); wrapper mode (ERGO-5, "Providing a wrapper that runs
  another script/tool") self-heartbeats so it never hits this. A rare over-long call
  false-expires, but LIFE-21's ("What expiry does") recycle bin resurrects it on the next event.
- **Paused** and **NeedsAttention** ~several hours (starting ~4h), the SAME value for now --
  both are alive-but-quiet (a held session / an away user). They diverge only if a two-way
  answer round-trip (INTER-*) ever lands, which would warrant a longer/never NeedsAttention
  period again.
- **Completed / done / fail** ~10 min linger -> Remove -- visible long enough to notice
  completion, then gone.
**Parent:** LIFE-7

The default idle period, per-task override, and ERGO-17 ("Configuration surface for
recurring defaults") layering. Key wrinkle (discovery, 2026-07-04): the idle period
is measured only from writes, and two legitimately-alive situations produce long write
silence -- (a) a long single generation fires no hooks between prompt-submit and
Stop, and (b) a `NeedsAttention` card waiting on an away user, which is precisely the
card expiry must not eat. Points at per-state idle periods (NeedsAttention very
long/never, Completed = the extended linger already seeded in LIFE-7, Running
moderate) rather than one number.

Note (2026-07-05, from LIFE-21, "What expiry does"): the recycle bin handles EVENT-DRIVEN
resurrection only. Nothing resurrects a NeedsAttention card (a returning user is not a CLI
event, and v1 has no answer round-trip), so the per-state visible-idle periods here still
stand -- NeedsAttention keeps its longer-than-Running period (~4h per the decision above; it
DOES expire at that threshold -- the stale "very-long/never" phrasing was corrected 2026-07-07)
so the live card stays up for the away user. The recycle bin is complementary, not a replacement.
