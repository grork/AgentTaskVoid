# FAIL-1: Failure posture toward the host caller
**Status:** DECIDED
**Decision:** Three-tier. Default = non-disruptive: on any failure the CLI
no-ops and exits 0 so it can never break the host caller. A durable failure log
is always written even on the silent path, for post-hoc analysis after the hook
process is gone. `--strict` (or equivalent) additionally surfaces errors to
stderr + a nonzero exit for live debugging.

The API can be unavailable at runtime (gradual rollout,
CLASS_E_CLASSNOTAVAILABLE), identity can be unregistered, individual calls can
fail. A hook that exits nonzero or hangs can disrupt the agent session it
serves -- and hooks are the driving scenario (operator, 2026-07-02). Should
the CLI be guaranteed-non-disruptive by default (no-op + success exit when the
taskbar can't be driven), with a strict mode for debugging?

Decision detail (2026-07-02): Non-disruptive is the default because hooks are
the driving scenario and a nonzero/hung hook disrupts the agent. But silent
failure is undebuggable, so the always-on durable log is a hard requirement of
this decision, not optional -- its mechanics (location, format, rotation) are
FAIL-3. `--strict` is for interactive debugging where the caller wants to know
immediately. The exit-code vocabulary and machine-readable output are FAIL-2.
