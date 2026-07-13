# INFRA-31: Recorder legs for the not-yet-testable hosts (Copilot / Codex / pi)
**Status:** OPEN
**Parent:** INFRA-23

## Question
INFRA-30 sets the recorder's rollout policy: the host-agnostic core is built once (phase-14,
validated by the Claude Code integration), and each additional host's leg is its own future
phase, gated on that host being installed and testable on the working box. Copilot CLI,
Codex, and pi are not testable here today.

This question owns the remaining legs — whether and how each of the Copilot / Codex / pi
recorder legs gets built: conduit config, safe/skip matrix (INFRA-26), observer posture
(INFRA-27), driver harness + cue script (INFRA-28), live capture, and stamped findings
(INFRA-29), instantiating that already-decided design for the host.

## Status: OPEN — not yet processed
The answer process has deliberately NOT been run over this. It is surfaced now (during
phase-14 planning) so the remaining rollout is catalogued and cannot be silently dropped —
an OPEN question keeps the set formally incomplete — but its disposition is for a future
session, not assumed here:
- A **deferred-until-testable** outcome is likely — a host that can't be run can't be
  captured, mirroring the deferred phase-13 Copilot/Codex integration legs — but that is a
  scope call made by running the process, not a foregone conclusion.
- Per-host context to fold in when it is processed: Copilot (LIFE-13 hook-surface
  inventory), Codex (LIFE-14), pi's in-process conduit (pinned in INFRA-27). A recorder leg
  for a host is the precursor that verifies that host's mapping (LIFE-24 rule 7) before that
  host's deferred phase-13 integration leg.
- May warrant expansion into per-host questions if the hosts' rollouts diverge; a single
  umbrella suffices while they share INFRA-30's model.

## Not in scope of phase-14
Phase-14 builds the core + the Claude Code leg only (INFRA-30). No Copilot / Codex / pi
artifacts are produced there; this question is where that remaining work is tracked.
