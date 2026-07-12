# LIFE-20: Logoff/reboot recovery
**Status:** DECIDED
**Plan:** phase-09
**Decision:** No task is valid across a reboot -- the premise is surfacing what a LIVE agent
harness/script is doing, and nothing that could own a task survives a reboot. So recovery is
not per-task reasoning, it is an unconditional clear. A self-disabling boot-recovery startup
item (enabled while a watchdog has live work, disabled on the watchdog's clean exit, LIFE-19,
"Watchdog shutdown conditions") runs at most once on a boot that followed a
reboot/crash-with-pending-work: it does a flat `atv clear` (Remove every task + delete every
sidecar and icon), disables itself, and exits. (Noted 2026-07-05: this boot clear also wipes
the LIFE-21, "What expiry does", recycle bin -- the internal, non-interactive exception to
the ERGO-16/ERGO-27 `clear` scope, which excludes the recycle bin by default.) No boot-time-age watermark (rejected as
over-engineering -- everything present at boot is orphaned by definition) and NO
shutdown-signal handler (the enabled-flag covers clean-shutdown and hard-crash identically, so
catching `CTRL_SHUTDOWN` buys nothing). Packaged mechanism = enable/disable a declared MSIX
StartupTask (`RequestEnableAsync`/`Disable`), user-disableable via Task Manager -> degrades to
"accept the gap", non-disruptive (FAIL-1, "Failure posture toward the host caller"). Accepted
edge: a session that starts and creates a task within the brief recovery window can have it
cleared, self-healing on its next `start`; if that ever bites, the rejected watermark is the
known lever. The OS-launched item may briefly flash a console window (INFRA-22) -- accepted for
this rare path in v1.
**Parent:** LIFE-6

Tasks persist across reboot; the watchdog does not. What re-establishes supervision
or cleans boot-stale tasks? Respawn-and-sweep on the next CLI invocation has the same
hole that motivated LIFE-4 ("Whether a persistent background watchdog process is
required"): a session that never invokes again leaves its card forever. Alternatives:
an MSIX StartupTask (heavier, and an install-time surface, DIST-1), or accept the
gap. Also: is boot-stale even distinguishable from long-idle by sidecar `lastUpdate`
alone (a laptop asleep overnight looks identical)? Cross-ref LIFE-15 ("Handling tasks
that have timed out, but get 'resurrected'").
