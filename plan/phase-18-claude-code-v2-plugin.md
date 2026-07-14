# Phase 18: Claude Code v2 integration — translator + plugin delivery

**Depends on:** phase 15 (the semantic verbs + `docs/integration-api.md`), phase 17
(`--cwd` exists to forward), phase 16 soft (default card visuals in the dogfood).
Consumes the phase-14 capture findings (`docs/host-events/claude-code.md`).
**Unblocks:** nothing downstream in this plan — but it is the *pattern* the future
Copilot CLI / Codex / pi legs instantiate (INFRA-31, DEFERRED-until-testable; recipe
INFRA-32).

## Goal

Ship the Claude Code integration on the v2 line: a logic-free conduit + a real
translator script + an extraction table, delivered and wired as a **native Claude
Code plugin** — superseding the shipped phase-13 one-liner artifact. Live-dogfood
verified end to end; doc-only verification is explicitly insufficient (both phase-13
live bugs were invisible on paper; LIFE-24 mapping rule 7).

## Decisions implemented

### Conduit form (LIFE-25, "Should host hooks invoke the exe directly vs via a shell?")

- No direct exec of `atv` — something must parse the host payload, and it can never
  be `atv` (LIFE-10's no-host-specifics invariant). Each hook line is a **plain
  program+args invocation of the translator file**, passing the event name:
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <plugin-root>/translate.ps1
  -Event <name>` — parses identically under bash/cmd/PowerShell, so the phase-13
  `"shell": "powershell"` selection footgun and the embedded-one-liner escaping layer
  are both gone. Per-event process churn is accepted (async hides it).
- **Async posture (INFRA-27 precedent):** async on every in-turn event; `SessionEnd`
  ALONE synchronous with `timeout: 10` — the phase-13 teardown-race lesson, re-proven
  by the phase-14 capture.

### The artifact shape + translator disciplines (LIFE-24, "The host-event → task-state integration semantics")

- `integrations/claude-code/` = the conduit hook declarations + **`translate.ps1`** +
  **`map.json`** (event→verb routing, tool→canonical-kind, label property paths,
  value maps). `map.json` is a first-party convention, never a public schema — atv
  never reads it; the structured verbs remain the only API. Structural quirks stay
  as code in the script (e.g. composing the `(n/m)` plan label); the table must
  never grow into a language.
- Translator tech: Windows PowerShell **5.1-compatible subset** (the only in-box
  JSON-capable runtime — DIST-4's paste-and-go posture); the conduit MAY prefer
  `pwsh` when present.
- **The four disciplines** (each mapped to a live bug class): (1) a real `-File`
  script, never embedded one-liners; (2) arbitrary text reaches atv via stdin
  (`--flag -`), never argv; (3) explicit UTF-8 at both ends (stdin via a UTF-8
  StreamReader, `$OutputEncoding` pinned); (4) never re-serialize payload fragments.
- **Forward the anchor:** every upserting call passes `--cwd ${CLAUDE_PROJECT_DIR}`
  (ERGO-30's translator discipline — Claude Code substitutes the project root before
  spawning; no JSON parse, no escaping trap). Identity flags
  (`--title`/`--subtitle`/`--icon`) ride argv on every upserting call (ERGO-25 makes
  re-application idempotent).
- Exit-0 always; never `--strict` (FAIL-1 — fail-closed hosts must never see nonzero).

### The mapping (ERGO-31 + the phase-14 capture, `docs/host-events/claude-code.md`, stamped 2.1.207)

Normative sources: ERGO-31 §1–§4 and the capture findings; this table is the
routing skeleton `map.json` + `translate.ps1` implement:

| Claude Code event | → verb |
|---|---|
| `UserPromptSubmit` | `working <sid> --goal -` (prompt text) |
| `PreToolUse`/`PostToolUse` | `activity <sid> --kind <map> --label -` (+ `--agent <agent_id> --name <agent_type>` when payload carries them) |
| `TodoWrite` (via tool events) | `activity --kind plan`, label `(n/m) <item>` composed in-script |
| `PermissionRequest` | `blocked <sid> --question -` (+ `--agent <agent_id>` when present — attribution keys off `PermissionRequest`, NOT `Notification`, per the capture) |
| `Notification: permission_prompt` | nothing (no `agent_id`; `PermissionRequest` owns Blocked) |
| `Notification: idle_prompt` | `ready <sid>` (fires ~60 s post-turn, once, focus-independent; harmless duplicate of `Stop` — kept as the only done-signal after an interrupt) |
| `Stop` | `ready <sid> --summary -` (`last_assistant_message`) |
| `StopFailure` | `broken <sid> --reason <map: rate_limit→rate-limit, overloaded→overloaded, …→fatal> --detail -` |
| `SubagentStart` / `SubagentStop` | `agent-started` / `agent-stopped <sid> --agent <agent_id> --name <agent_type>` |
| `SessionEnd` | `session-ended <sid> --reason finished` (`reason` field — not `exit_reason`; both observed values `other`/`prompt_input_exit` map to `finished`) |
| `SessionStart` | nothing (no session-start verb; optional row: `source=compact` → `activity --kind compacting`) |

Tool→kind rows per ERGO-31 §2 (`Read`→`read`, `Edit`/`NotebookEdit`→`edit`,
`Write`→`write`, `Grep`/`Glob`→`search`, `Bash`→`shell`, `WebFetch`→`fetch`,
`WebSearch`→`web-search`, unmapped→`tool`). A user interrupt raises no hook event —
the card reads Working until the next event (accepted, LIFE-24).

### Plugin delivery (DIST-11, "How the integration artifact is delivered, placed, and wired")

- Ship as a **Claude Code plugin**: it bundles `translate.ps1` + `map.json` AND
  declares its own hooks, so installing the plugin **delivers the files and wires
  the hooks in one step** — no hand-edit of `settings.json`. Hook lines reference
  **`${CLAUDE_PLUGIN_ROOT}`** (host-substituted, refreshed on plugin update), which
  dodges the versioned-MSIX-path landmine — the translator files never ride in the
  MSIX. Two-vehicle split: MSIX = engine only (DIST-10, deferred); plugin = the
  integration. The plugin invokes `atv` by alias.
- **Verify the plugin format against the INSTALLED Claude Code version + a live doc
  fetch** — the phase-13 lesson (hallucinated matcher, moved doc URLs): manifest
  layout, hooks-declaration file name, and `${CLAUDE_PLUGIN_ROOT}` semantics are
  authored from current primary docs, not memory.
- **Supersede the phase-13 artifact:** the v1 `settings.hooks.json` one-liner
  fragment and its README are replaced by the plugin; no v1 lifecycle verb
  references survive anywhere in `integrations/` or docs. README/docs updated
  (install = add the plugin; `doctor` remains the diagnosis path).
- **Marketplace publication is OUT of scope** (DIST-11's "not committed now") —
  local/path-based plugin install is the delivery this phase proves.

## Files affected

```
integrations/claude-code/…                    # REPLACED: plugin manifest + hooks declaration,
                                              #   translate.ps1, map.json, README (install/uninstall)
tests/Atv.LogicTests/Integrations/*           # artifact-shape tests (replace the phase-13 set)
docs/integration-api.md                       # cross-link only (normative doc is phase 15's)
docs/host-events/claude-code.md               # staleness re-stamp if re-captured
README.md, docs/configuration.md              # plugin install flow supersedes the fragment paste
```

## Acceptance criteria (written first)

1. **Artifact-shape tests** (logic suite, replacing the phase-13 set): hooks
   declaration is well-formed; every hook line matches the LIFE-25 program+args form
   (no embedded one-liners, no `shell` selection, `${CLAUDE_PLUGIN_ROOT}`-rooted);
   only ERGO-31 verbs invoked; no `--strict` anywhere; `SessionEnd` is the sole
   synchronous hook (`timeout: 10`); upserting calls carry the identity flags and
   `--cwd ${CLAUDE_PROJECT_DIR}`.
2. **Translator tests offline:** drive `translate.ps1` under Windows PowerShell 5.1
   with recorded payload shapes from the phase-14 captures (against a stub `atv` that
   records argv+stdin): every routing row above produces the right verb, flags, and
   stdin bytes; a UTF-8 torture payload (non-ASCII, quotes, newlines) reaches the
   stub byte-intact (discipline 3); an unmapped tool falls to `--kind tool --label
   <tool_name>`; payload fragments are never re-serialized (discipline 4 — label
   bytes match the plucked source).
3. **Capture staleness gate:** the installed Claude Code version is checked against
   the `docs/host-events/claude-code.md` stamp (2.1.207) BEFORE the dogfood; if the
   host has moved materially, re-run the phase-14 recorder leg first (INFRA-29
   organic re-capture) and reconcile the routing table.
4. **Plugin installs and wires:** on the installed Claude Code, adding the plugin
   (local install) wires all hooks with zero `settings.json` hand-edits;
   `${CLAUDE_PLUGIN_ROOT}` resolves; removing/disabling the plugin leaves no firing
   hooks (uninstall symmetry, documented).
5. **LIVE dogfood (operator-supervised — the phase-13/14 pattern; not subagent-able):**
   a real session drives the card through **Working** (goal from the real prompt +
   kind-rendered activity lines), **Blocked** (a real permission prompt shows the
   question; clears on approval via same-locus attribution), **Ready** (turn end
   shows the summary), **fan-out** (≥2 parallel subagents mint glomming child cards
   that retire at fan-in), and **removal** on `/exit` (sync teardown holds). Broken
   is best-effort (a real `StopFailure` may not be triggerable on demand —
   documented-as-expected if not). The session itself is unperturbed (no hook errors,
   latency imperceptible).
6. **Repo branding live:** a `.atv.json` in the dogfood repo brands the card through
   the forwarded `--cwd` (phase 17 proven through the real conduit path).
7. **Supersession clean:** no v1 lifecycle verb reference remains in
   `integrations/` or docs; README's integration section describes the plugin flow;
   the retired fragment is gone.

## Out of scope

Marketplace/registry publication (DIST-11 "not committed now"); the Copilot CLI,
Codex, and pi legs — translators, plugins, captures, and mapping tables (INFRA-31,
DEFERRED-until-testable; onboarding recipe INFRA-32); two-way interaction (INTER-*,
DEFERRED); any raw card-control tier (ERGO-32, DEFERRED); `atv install-hooks` (the
DIST-11 fallback for plugin-less hosts — none in scope here).
