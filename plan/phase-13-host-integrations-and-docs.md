# Phase 13: Per-host integration artifacts + documentation

**Depends on:** phases 09/10 (stable verb surface + doctor); phase 12 for install
instructions (docs can draft against it in parallel).
**Unblocks:** nothing — final phase.

## Goal

Ship the catalysing scenario: ready-made integration artifacts that wire Claude
Code, GitHub Copilot CLI, and Codex hooks to `atv` verbs, plus the user-facing
documentation and the maintenance checklist for the experimental-API surface.

## Decisions implemented

### Scope & shape (LIFE-8, LIFE-11, LIFE-10)

- v1 targets exactly three hosts: Claude Code and Copilot CLI prioritized, Codex
  lowest. Criterion for later additions: a usable hook surface mappable without
  host-specific CLI logic, plus demand.
- We ship per-host artifacts (ready-made hook configs), not docs-only. The CLI
  itself carries NO host-specific branching — each artifact translates its host's
  events into the host-agnostic verb set: `start` / `step` / `state` / `done` /
  `fail` / `attention` / `remove`.
- All artifacts MUST be safe to re-fire (`start` is upsert, ERGO-25) — resumed
  sessions re-fire session-start with the same id on Copilot/Codex.
- Recurring per-host defaults (e.g. a host-specific icon token) go in the shipped
  artifact via flags or a config stanza (ERGO-17), not new CLI features.

### Per-host mappings (from the LIFE-12/13/14 inventories; verify against INSTALLED versions — the surfaces move)

Cross-host caveat (phase 05, ratified 2026-07-07): `step` PRESERVES card state, so
after an `attention` a plain `step` is refused until the state resets — every
artifact must issue a state reset when work resumes (e.g. map the host's
prompt-submit/tool-start event to `atv state <id> running` ahead of the step, or
chain it in the step hook line). Verify per host.

**Claude Code** (settings.json `hooks` block; JSON on stdin; every payload carries
`session_id`):
- SessionStart → `atv start <session_id> --title …`
- PreToolUse (or PostToolUse) → `atv step <session_id> "<tool_name>…"`
- Notification (permission_prompt / idle_prompt) → `atv attention <session_id> "…"`
- Stop → `atv done <session_id>` (turn-done; NOT fired on interrupt)
- SessionEnd → `atv remove <session_id>` (best-effort — clean exits only; the
  watchdog covers unclean death)
- Caveats: `session_id` changes across `--resume` (accepted for v1; the old card is
  reaped by idle expiry). Hooks must be `async` or fast — an `atv` call is
  fire-and-fast either way (non-disruptive posture guarantees no hang/nonzero).

**GitHub Copilot CLI** (13-event hook system; command hooks, stdin JSON;
`sessionId`/`session_id` in every payload, survives resume):
- sessionStart → start; preToolUse/postToolUse → step; permissionRequest +
  notification → attention; agentStop → done; sessionEnd (reason
  complete|error|abort|timeout|user_exit) → done/fail/remove by reason.
- Caveat: preToolUse command hooks fail-CLOSED on crash/nonzero — atv's exit-0
  default posture is load-bearing here; never ship a `--strict` hook line.

**Codex** (lowest priority; modern hooks framework, stdin JSON snake_case,
`session_id` survives resume; NO session-end event):
- SessionStart → start; PreToolUse/PostToolUse → step; PermissionRequest →
  attention; Stop → done. Cleanup relies wholly on the watchdog.

### Documentation

- README rewrite: what it is (persistent taskbar task cards — NOT jump lists),
  install (`winget install <id>`), quickstart per verb, the hook artifacts, config
  reference (precedence, env names, per-state idle periods), `doctor`
  troubleshooting flow, cross-consumer guidance (`clear` is identity-global — use
  targeted `remove`; ERGO-16), soft floor statement (Win11 26100+, AppTaskContract
  v2 — expectation-setting only; runtime detection is authoritative, INFRA-13).
- INFRA-13 manual re-verification checklist (`docs/maintenance/new-build-checklist.md`):
  the "dark matter" list re-checked on new Windows builds — the state × content
  crash matrix (ERGO-10 data), grouping-keyed-on-icon-URI (ERGO-13), the fake's
  four fidelity promises (link `docs/testing/fake-fidelity-promises.md`; the
  4×100 clobber run and the HiddenByUser real-gesture check are the manual items).
  Low priority by design; shifts update the matrix data and the fake together.
- Update `docs/windows-ui-shell-tasks/` with anything new learned during the build
  (standing CLAUDE.md instruction).

## Files affected

```
integrations/claude-code/…      # hooks settings fragment + install note
integrations/copilot-cli/…      # hook config + install note
integrations/codex/…            # hooks.json/config.toml fragment + install note
README.md                       # rewritten
docs/maintenance/new-build-checklist.md
docs/…                          # config reference, troubleshooting
```

## Acceptance criteria

1. Each artifact is tested against the INSTALLED host version (the inventories are
   from 2026-07-02 research; verify event names/payloads before shipping — this is
   the LIFE-12/13/14 verify-caveat, an explicit work item, not a footnote).
2. Live dogfood: a real Claude Code session with the shipped hooks shows a card
   that starts, steps through tool calls, raises attention on a permission prompt,
   and completes/disappears — without perturbing the session (hook latency
   imperceptible, no hook errors in the host).
3. Same for Copilot CLI; Codex best-effort (lowest priority — ship the artifact,
   verify if installable).
4. Re-fire safety demonstrated: resuming a Copilot/Codex session re-fires
   session-start and the card is adopted, not duplicated or reset.
5. Docs review pass: a newcomer can install via winget, wire one host, and
   diagnose a blank taskbar using only the README + doctor.
6. The new-build checklist exists and links the matrix data and fidelity-promises
   doc as the things to update when a check fails.

## Out of scope

Additional hosts (LIFE-8 criterion documented instead), any two-way answer
round-trip (INTER-* deferred — `attention` stays display-only), wire-transport
fallbacks for hookless hosts (LIFE-3 deferred).
