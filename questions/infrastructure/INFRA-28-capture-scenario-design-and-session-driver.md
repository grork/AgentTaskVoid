# INFRA-28: Capture scenario design & session driver
**Status:** DECIDED
**Plan:** phase-14
**Parent:** INFRA-23
**Decision:** Decided at the approach level (concrete driver scripts are plan-phase work):
ratify LIFE-24's scenario-beat list as the host-agnostic corpus (with per-host
subtractions); drive it with a per-host thin harness over each host's own scripting
surface, doing the two interactive-only beats (interrupt, live permission) supervised; no
universal driver framework.

## Question
Design the scripted session(s), per host, that exercise the interesting event territory,
and the mechanism that drives them.

Operator direction (2026-07-11, discovery): **semi-automated**, not a purely human-driven
runbook — tooling that feeds prompts/inputs into the host CLI to hit each event more
deterministically and repeatably than a human following a checklist by hand.

## Decision (operator + Claude Code, answer session, 2026-07-12)
Scope: this question is settled at the **approach** level; the concrete per-host driver
scripts are built in the recorder's plan phase (phase 14), consistent with how other
questions fix the shape and leave implementation to phases. (Amended 2026-07-12,
phase-14 planning, operator: phase 14 builds only the Claude Code driver — the sole
testable host on this box; each other host's driver is built by the future pass that
can test that host.)

1. **Scenario beats — the shared, host-agnostic corpus** (from LIFE-24, ratified here):
   fresh start → first prompt → tool calls → parallel subagent fan-out (≥2 concurrent) →
   a real permission prompt → a user interrupt → an idle wait past the notification
   threshold → clean exit. **Per-host subtractions** apply where a surface doesn't exist:
   Codex has no session-end signal via hooks; pi has no built-in permission system and no
   subagent events; Copilot's subagent resolution is name-only. The *what* (beats) is
   shared; only the *how* (injection) is per-host.
2. **Driver mechanism — reach the state for real, never fake the signal under test.** The
   recorder captures hooks out-of-band, so the driver's only job is to make the host
   genuinely reach each state:
   - Most beats: a scripted prompt sequence through the host's own scripting surface.
   - Permission prompt: a prompt requiring a tool the session isn't pre-authorized for →
     a *real* `permission_prompt`, not a synthesized one.
   - Idle wait: literally wait past the threshold and capture whether/when the idle signal
     fires (also LIFE-24 empirical item 3).
   - Subagent fan-out: a prompt explicitly requesting ≥2 parallel subagents.
   - **The two interactive-only beats — user interrupt and the live permission dialog —
     are done supervised** (a human control-char / approval at the scripted moment) for
     v1, rather than building PTY automation.
3. **Per-host driver logic is acceptable; no universal framework.** The four hosts are
   structurally different (3 spawn-per-event TUIs + pi's in-process TS) — a per-host thin
   harness over each host's own scripting surface (Claude Code `-p`/print or SDK, Codex
   `exec --json`/app-server, pi `--mode json`/rpc, Copilot's mode) is the right grain,
   the same way INFRA-24 keeps the conduit per-host. A single universal driver for four
   different hosts is over-engineering.
4. **Relationship to INFRA-26.** The driver only exercises events already cleared by
   INFRA-26's safe-coverage pass; it never drives a skip-classified event.
