# INFRA-31: Recorder legs for the not-yet-testable hosts (Copilot / Codex / pi)
**Status:** DEFERRED
**Deferred:** Processed 2026-07-13. No new design remains to decide: INFRA-30 already set the
rollout policy (each host leg is its own future phase, gated on that host being installed and
testable on the working box) and INFRA-24..29 decided the host-general design a leg merely
instantiates (INFRA-29's verbatim core admits a new host with zero change, so waiting costs
nothing). The one remaining gate — Copilot / Codex / pi being runnable here — is an external
environment condition, not a decision, and all three are un-testable on this box today (the same
gate that deferred the phase-13 Copilot/Codex integration legs). There will *always* be a host we
want but cannot yet run locally, so this stays a **single deferred umbrella** rather than being
forced to a premature build or split into empty per-host questions. Pre-building any leg doc-first
is actively discouraged: a doc-derived mapping is an unverified hypothesis (INFRA-23 premise,
LIFE-24 rule 7) and Codex's hooks are still churning. **Un-defer per host:** when a host becomes
installed + testable, INFRA-30's standing policy plans its recorder leg directly as a new phase —
no reopening of this question needed (mirrors the deferred phase-13 integration legs). **Scope
refinement (LIFE-8):** Copilot + Codex are committed v1 targets, deferred-until-testable; **pi is
NOT a v1 host** (LIFE-8 names only Claude Code / Copilot / Codex), so a pi recorder leg is out of
v1 scope entirely — it arises only if pi is later adopted under LIFE-8's add-a-host criterion, and
would need a different, non-spawn capture mechanism (noted in `docs/host-events/README.md`, not
designed). The reusable recipe each of these legs will follow when built is **INFRA-32** (the
host-onboarding playbook — itself DEFERRED until the first few adapters are built by hand).
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

## Disposition (processed 2026-07-13): DEFERRED-until-testable, single umbrella
The answer process was run over this on 2026-07-13 (the disposition INFRA-30 punted here). Outcome
= deferred-until-testable, as INFRA-30 anticipated but did not pre-decide. The reasoning is in the
`**Deferred:**` header above; the per-host context that informed it:
- Copilot (LIFE-13 hook-surface inventory), Codex (LIFE-14), pi's in-process conduit (pinned in
  INFRA-27). A recorder leg for a host is the precursor that verifies that host's mapping (LIFE-24
  rule 7) before that host's deferred phase-13 integration leg.
- **Single umbrella, not expanded.** Expanding into per-host questions was considered and
  rejected: all three are equally un-buildable now and share INFRA-30's model, so three children
  each reading "deferred until testable" would be empty ceremony. Re-expand only if a specific host
  is adopted *and* its rollout genuinely diverges.
- **pi carved out by scope** (LIFE-8): out of v1 entirely, not merely deferred-with-the-others —
  see the header's scope refinement.

## Not in scope of phase-14
Phase-14 builds the core + the Claude Code leg only (INFRA-30). No Copilot / Codex / pi
artifacts are produced there; this question is where that remaining work is tracked.
