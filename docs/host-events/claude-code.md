# Claude Code host events

Part B of phase-14 (`plan/phase-14-host-event-recorder.md`) — the Claude Code leg
that proves the recorder core (INFRA-30). Conventions, pinned plumbing names, and
the per-host artifact pattern are in `docs/host-events/README.md`; this file is
the Claude-Code-specific matrix and findings the pattern produces.

Conduit template, stage step, driver harness, and cue script live at
`tools/host-event-recorder/hosts/claude-code/`.

## Safe/skip matrix (INFRA-26)

**Derived from the Claude Code hooks reference** (fetched 2026-07-12,
<https://code.claude.com/docs/en/hooks> — 30 hook events, corroborating LIFE-24's
2026-07-11 grounding note of "~30 events now vs the 9 in LIFE-12"), **before any
capture runs**. One classification axis: does camping this event (a passive
recorder that logs stdin verbatim and exits 0, emitting no decision output)
suppress or replace a default host action?

- **Safe** — a passive log-and-exit-0 hook changes nothing, even on
  decision-capable events: declining to emit a decision (no `hookSpecificOutput`,
  no `decision` field) is indistinguishable from no hook being registered at all
  for every event below except `WorktreeCreate`. The recorder never emits a
  decision — it only ever logs and exits 0.
- **Skip (v1)** — the replacement class: an event where the host's documented
  default, once a hook is registered, is *the hook performs the work* (no
  hook-absent fallback). Camping without doing that work breaks the host action
  it fires for. Per INFRA-26, camp-with-care collapses into skip for v1.

Every row below is **first derived from docs**, to be **confirmed by the capture**
(see Findings, pending the operator's supervised run).

| Event | Class | Reason (the one axis) | Posture | Driver coverage (v1) |
|---|---|---|---|---|
| `SessionStart` | Safe | Context-injection only (`additionalContext`, `sessionTitle`, …); no context injected = session starts exactly as it would with no hook. | Async | Scripted — fresh start |
| `Setup` | Safe | Context-injection only, CI-flag-gated (`--init-only`/`--maintenance`); silence = no injected context. | Async | Not exercised v1 — CI-only flow, outside beat corpus |
| `UserPromptSubmit` | Safe | Decision-capable (`decision:"block"` rejects+erases the prompt) but declining to decide leaves the prompt processed normally — the canonical "declining to decide changes nothing" case. | Async | Scripted — first prompt |
| `UserPromptExpansion` | Safe | Decision-capable (can block a slash-command expansion); declining lets the expansion proceed unmodified. | Async | Not exercised v1 — no slash-command beat in corpus |
| `PreToolUse` | Safe | Decision-capable (`permissionDecision` allow/deny/ask/defer, `updatedInput`); declining (no `hookSpecificOutput`) is the INFRA-26 decision text's own worked example — the default permission flow proceeds untouched. | Async | Scripted — tool calls (+ subagent-internal, `agent_id`) |
| `PermissionRequest` | Safe | Decision-capable (`decision.behavior` allow/deny) fired when the permission dialog would appear; declining leaves the normal dialog/flow in place. | Async | Supervised — real permission prompt beat (+ added subagent-permission beat) |
| `PermissionDenied` | Safe | Only a `retry:true` signal after an **auto-mode** classifier denial; declining leaves the denial standing. | Async | Not exercised v1 — requires `permission_mode:auto`, outside corpus |
| `PostToolUse` | Safe | Decision-capable (`decision:"block"`, `updatedToolOutput`) per the *current* docs fetch; declining leaves the tool's real output unmodified. **Flag:** this conflicts with `integrations/claude-code/README.md`'s phase-13 note that `PostToolUse` "cannot block; already ran" — worth re-confirming which is current during the capture (see Findings). | Async | Scripted — tool calls |
| `PostToolUseFailure` | Safe | Decision-capable (`decision:"block"`); declining leaves the failure surfaced normally. | Async | Incidental only — fires only if a scripted tool call happens to fail |
| `PostToolBatch` | Safe | Decision-capable (`decision:"block"` after a parallel-tool-call batch resolves); declining lets the loop continue normally. | Async | Incidental — only if the scripted prompt causes genuinely parallel (non-subagent) tool calls in one turn |
| `Notification` | Safe | Purely observational per the docs ("no blocking"); the textbook safe case. | Async | Scripted (incidental) + Supervised — `permission_prompt` (real + subagent-originated beats), `idle_prompt` (idle-wait beat) |
| `MessageDisplay` | Safe | Can replace *on-screen* text only (`displayContent`); transcript/model-visible text is untouched either way, and declining leaves the streamed text as-is. | Async | Not exercised v1 — no beat targets this |
| `SubagentStart` | Safe | Context-injection only; declining starts the subagent unmodified. | Async | Scripted — fan-out beat (≥2 concurrent) |
| `SubagentStop` | Safe | Decision-capable (`decision:"block"` keeps the subagent going); declining lets it stop normally — same "declining to decide" case as `Stop`. | Async | Scripted — fan-out beat |
| `TaskCreated` | Safe | Decision-capable (block/rollback task creation); declining lets creation proceed. | Async | Not exercised v1 — no task-tool beat in corpus |
| `TaskCompleted` | Safe | Decision-capable (block marking complete); declining lets completion proceed. | Async | Not exercised v1 |
| `Stop` | Safe | Decision-capable (`decision:"block"` keeps the turn going); declining ends the turn normally — the shipped phase-13 integration already relies on exactly this. | Async | Scripted — end of the driven turn |
| `StopFailure` | Safe | Explicitly "output and exit code ignored" per the docs — cannot influence host behavior even in principle. | Async | Not exercised v1 — requires an API error, not induced |
| `TeammateIdle` | Safe | Decision-capable (exit 2 / `continue:false` prevents idle); declining lets the teammate idle normally. | Async | Not exercised v1 — agent-team feature, outside corpus |
| `InstructionsLoaded` | Safe | Purely observational (exit code ignored). | Async | Incidental — only if the scratch project has a `CLAUDE.md`/`.claude/rules/*.md` to load |
| `ConfigChange` | Safe | Decision-capable (block, except `policy_settings`); declining lets the config change apply. | Async | Not exercised v1 — no config-edit beat |
| `CwdChanged` | Safe | Purely observational. | Async | Not exercised v1 — no `cd` beat |
| `FileChanged` | Safe | Purely observational, async by the docs' own description. | Async | Not exercised v1 — needs an explicit literal-filename matcher and a matching file edit, neither in the corpus |
| `WorktreeCreate` | **Skip (v1)** | **The replacement class.** Docs: "Replaces default git behavior… any non-zero exit code causes creation to fail… missing path or hook failure fails creation." A passive logger that never prints a path *breaks* worktree creation outright — camping here without doing the real work is exactly what INFRA-26 rules out. | N/A — not camped | N/A — never driven |
| `WorktreeRemove` | Safe | Observational; docs say failures are "logged in debug mode only" (not fatal) — unlike its `Create` counterpart, there is no hook-must-supply-the-work contract here. | Async | Not exercised v1 — no `--worktree` beat |
| `PreCompact` | Safe | Decision-capable (block compaction); declining lets compaction proceed. | Async | Not exercised v1 — no `/compact` beat |
| `PostCompact` | Safe | Purely observational. | Async | Not exercised v1 |
| `Elicitation` | Safe (lower confidence) | Decision-capable (`action` accept/decline/cancel) for an MCP elicitation dialog. Docs don't state an explicit no-hook-absent-fallback contract the way `WorktreeCreate` does, so this is classified by the general "declining leaves the dialog to the normal flow" pattern rather than an explicit docs statement — **flagged for extra scrutiny if it ever fires during a capture.** | Async | Not exercised v1 — no MCP-elicitation beat |
| `ElicitationResult` | Safe (lower confidence) | Same reasoning and same flag as `Elicitation`. | Async | Not exercised v1 |
| `SessionEnd` | Safe | Purely observational (cleanup/logging only); no default action is suppressed by camping. Its **synchronous** posture below is an INFRA-27 teardown-race concern, not a safety concern — it is safe to camp either way. | **Sync** (`timeout: 10`) — see Observer posture | Supervised — clean-exit beat (`-p` is one-shot and never reaches a `SessionEnd`-bearing lifecycle; see `driver-scripted.ps1`'s header comment) |

**Only `WorktreeCreate` is skipped.** Every other candidate event is safe to camp
passively, so the conduit template (`tools/host-event-recorder/hosts/claude-code/settings.hooks.template.json`)
registers all 29 safe rows and omits `WorktreeCreate` entirely.

An unsafe camp found mid-capture (INFRA-26 rule 4): pull/downgrade that event's
row here and record the observed reason — the config is version-controlled and
capture runs are supervised, so no heavier process applies.

## Conduit implementation notes

- **Exec form, no shell.** Every hook line uses Claude Code's `args`-set exec
  form (`command` = the recorder exe's absolute path, `args` = `["--host",
  "claude-code", "--event", "<Name>"]`), confirmed against the current CLI
  hooks reference: "exec form runs when `args` is present… Claude Code
  resolves `command`… and spawns it directly with `args` as the argument
  vector. There is no shell." `shell` is meaningless here (docs: "Ignored when
  `args` is set") and is omitted. This is the byte-faithful option the plan
  called for — no PowerShell text pipeline sits between the host's stdin bytes
  and the recorder, which is the entire reason the recorder is compiled C# and
  not another one-liner.
- **Absolute-path `command`, not a PATH-resolved name.** The docs' summary
  phrase ("resolves `command` as an executable on PATH") describes the general
  case; the docs' own schema example substitutes a path placeholder
  (`${CLAUDE_PLUGIN_ROOT}/…/block-rm.sh`) directly into `command` under exec
  form, i.e. an absolute/resolved path is an accepted shape, not just a bare
  PATH-searched name. Standard exec conventions (Windows `CreateProcess`,
  Node's `child_process.spawn` without `shell:true`) treat a
  separator-containing command string as a path and skip PATH search
  entirely. **Flagged for a first-capture sanity check anyway**: if the very
  first staged capture comes back empty, check this assumption first.
- **A real double-escaping bug was caught and fixed here** (2026-07-12,
  mechanical validation): the stage step's placeholder substitution first
  used `-replace '\\', '\\\\'` to JSON-escape the exe's Windows path, which is
  wrong — .NET's `-replace` replacement string has no backslash-escaping
  semantics of its own (only `$`-group references are special), so a
  4-backslash replacement literal inserted 4 raw backslashes per matched
  single backslash instead of 2, double-escaping the path. `Test-Path` on the
  resulting (wrong) parsed string still returned `True` (Windows tolerates
  doubled internal separators), so this silently produced a *working but
  incorrect* path — worth remembering for any future host leg's stage script
  that does the same kind of path-into-JSON-string substitution. Fixed to
  `-replace '\\', '\\'` (pattern and literal replacement are both the same
  2-backslash string) and re-verified byte-for-byte against the real exe path.

## Observer posture (INFRA-27)

Async everywhere (spawn cost ~0 added to the host's event loop) **except
`SessionEnd`**, which is synchronous under `timeout: 10` — the shipped
phase-13 precedent (`integrations/claude-code/README.md`, "Why `SessionEnd` is
synchronous"): an async teardown hook loses the process-exit race and the
cleanup/log line never lands. Because `WorktreeCreate` (the only event where the
recorder would need to do real work) is skipped, the teardown race is the only
reason any event blocks.

## Findings (pending supervised live capture)

**Nothing below is filled in yet.** AC5/AC6 — real Claude Code captures, the
did-not-fire results, and the LIFE-24 empirical answers — come from an
operator-supervised run using `tools/host-event-recorder/hosts/claude-code/cue-script.ps1`
(interactive beats) and `driver-scripted.ps1` (the `-p` beats), staged by
`stage.ps1`. This section is the scaffold that run fills in; do not treat
anything here as confirmed until a stamp and session id are attached.

### Stamp format

```
**Verified against:** Claude Code `<version>` (installed on the capture
machine, `claude --version`) — captured `<yyyy-MM-dd>`, session(s)
`<session-id, session-id, …>`.
```
(Reuses `integrations/claude-code/README.md`'s "Verified against:" convention —
INFRA-29.)

### Per-event capture results — UNFILLED

| Event | Result | Session id(s) | Notes |
|---|---|---|---|
| *(one row per event in the matrix above)* | *(Confirmed fired / Did not fire / Not exercised this capture)* | | |

Every expected-but-absent event must land here as an explicit **did-not-fire**
finding citing its session id — including `SessionEnd`, which is the row that
proves the sync-at-teardown posture actually works (the phase-13 bug this
recorder exists to catch again if it regresses).

### LIFE-24 open empirical item 2 — UNANSWERED

*Which event types actually fire inside subagents; `agent_id` uniqueness across
PARALLEL spawns; whether a subagent-triggered `Notification: permission_prompt`
carries the `agent_id`.*

Targeted by the fan-out beat (scripted, ≥2 concurrent subagents) and the added
subagent-permission beat (supervised — a subagent task requiring a
not-pre-authorized tool). Pending capture.

### LIFE-24 open empirical item 3 — UNANSWERED

*Does `idle_prompt` fire after a user interrupt, and on what timing/repetition?*

Targeted by the supervised interrupt beat followed immediately by the idle-wait
beat. Pending capture.
