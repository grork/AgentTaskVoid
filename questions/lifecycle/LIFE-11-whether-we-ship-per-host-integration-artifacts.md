# LIFE-11: Whether we ship per-host integration artifacts
**Status:** DECIDED
**Plan:** phase-13
**Decision:** Yes -- v1 ships per-host integration artifacts (ready-made hook
configs/plugins that translate each host's events into `atv` verbs via the LIFE-10
mapping), not docs-only. Claude Code + Copilot prioritized, Codex lowest (LIFE-8).
The concrete artifact per host is build-phase work (write + test against installed
versions, per the LIFE-12/13/14 verify-caveat).
**Parent:** LIFE-2

Does the product include ready-made hook scripts/configs per host (a Claude
Code hooks file, a Copilot CLI config, ...), or only the host-agnostic CLI
plus documentation showing how to wire each host up?

Operator direction (2026-07-02): stays OPEN, but the direction is set -- we will
ship per-host plugin/skill packaging (ready-made integration artifacts), not
docs-only. The open part is the concrete form/coverage per host; Codex lowest
priority (LIFE-8).
