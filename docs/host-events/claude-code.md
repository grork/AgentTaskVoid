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

## Findings

**Verified against:** Claude Code `2.1.207` (installed on the capture machine,
`claude --version`) — captured `2026-07-12`/`2026-07-13`, sessions
`cc-20260712-212159` (scripted, `-p`), `cc-interactive-1`, `cc-interactive-2`,
`cc-interactive-3` (supervised interactive). These are the recorder
*capture-file* ids (`HOSTREC_SESSION`); each maps to one Claude-Code host
`session_id` inside the payloads (e.g. `cc-20260712-212159` →
`1d17eebc-3060-4494-8238-a22f4ac7bacb`, `cc-interactive-1` →
`4b8f7eec-9c33-4929-bdd3-1df2b2ec17d0`).

Four captures, split by what each surface can reach:

- **`cc-20260712-212159` (scripted, `driver-scripted.ps1` → `claude -p … --permission-mode bypassPermissions`, 23 records):**
  fresh start → first prompt → tool calls (`Bash`, `Read`) → parallel subagent
  fan-out (2 concurrent, via the `Agent` tool) → clean exit. Bypass mode, so no
  permission dialog.
- **`cc-interactive-1` (supervised interactive, 50 records):** first prompt → a
  real main-thread permission prompt (a `Bash` command) → a **subagent-originated**
  permission prompt (a subagent `Write`) → a user interrupt *during text
  generation* → `/exit`.
- **`cc-interactive-2` (supervised interactive, 9 records):** the interrupt beat
  redone to land *during a tool call* — `bash slow.sh` (a 25 s sleep) interrupted
  mid-execution. (Its idle window was invalidated by the operator — the session
  sat open overnight — so only its interrupt datum is used.)
- **`cc-interactive-3` (supervised interactive, 7 records):** a clean idle test —
  one prompt, turn completes, then a controlled idle wait (focused, then
  unfocused) → `/exit`.

### Per-event capture results

Result ∈ **Fired** (observed) · **Did not fire** (a beat targeted it, absent) ·
**Not exercised** (no beat reached its preconditions this capture). In the
Session column, **"both"** = the two comprehensive captures (`cc-20260712-212159`
scripted + `cc-interactive-1`); `cc-interactive-2`/`-3` were narrow single-purpose
runs (interrupt-during-tool, clean idle) cited explicitly where they contribute.

| Event | Result | Session(s) | Notes |
|---|---|---|---|
| `SessionStart` | Fired | both | `source:"startup"`. Carries `session_id`, `transcript_path`, `cwd`. |
| `Setup` | Not exercised | — | CI `--init-only`/`--maintenance` flow, no beat. |
| `UserPromptSubmit` | Fired | both | Carries `prompt`, `permission_mode`. |
| `UserPromptExpansion` | Not exercised | — | No slash-command beat. |
| `PreToolUse` | Fired | both | Carries `tool_name`/`tool_input`/`tool_use_id`; **`agent_id` present only when the tool call is subagent-scoped** (empty on main thread). A tool call **interrupted mid-execution** fires `PreToolUse` with **no matching `PostToolUse`/`PostToolUseFailure`** — it is orphaned (see finding 6). |
| `PermissionRequest` | Fired | `cc-interactive-1` | Fired for both the main-thread `Bash` prompt (no `agent_id` key) and the subagent `Write` prompt (**`agent_id` + `agent_type` present**, matching the raising subagent). Carries `permission_suggestions`, `tool_input`, `permission_mode`. **This — not `Notification` — is the event that attributes a permission prompt to a specific subagent.** |
| `PermissionDenied` | Not exercised | — | Requires `permission_mode:auto`. |
| `PostToolUse` | Fired | both | Carries `tool_response`, `duration_ms`; `agent_id` when subagent-scoped. |
| `PostToolUseFailure` | Fired | `cc-interactive-1` | Fires when a tool **fails on its own** — observed once, a subagent's auto-approved `git log` exiting 128 in the empty scratch repo. Carries `error`, `tool_name`, `agent_id`, `is_interrupt` (`false` here). It does **NOT** fire on a user interrupt: in `cc-interactive-2` a `bash slow.sh` call interrupted mid-run produced no `PostToolUseFailure` at all (finding 6), so `is_interrupt:true` was never observed. |
| `PostToolBatch` | Fired | both | Fires after each resolved tool batch; carries `tool_calls`; `agent_id` when subagent-scoped. |
| `Notification` | Fired | `cc-interactive-1`, `cc-interactive-3` | Two subtypes observed. `permission_prompt` (message `"Claude needs your permission"`, `cc-interactive-1`) — **carries NO `agent_id`**, even for a subagent-originated prompt (attribution is on `PermissionRequest` instead). `idle_prompt` (message `"Claude is waiting for your input"`, `cc-interactive-3`) — fired ~60 s after the turn completed (see item 3). Both are the two types the shipped phase-13 integration maps to `attention`. |
| `MessageDisplay` | Fired | both | Fires per streamed assistant message; carries `message_id`, `turn_id`, `delta`, `final`. Not targeted by a beat — fires incidentally every turn. |
| `SubagentStart` | Fired | both | Carries `agent_id` + `agent_type` (`general-purpose`). One per spawned subagent; **`agent_id` distinct per parallel spawn** (see item 2). |
| `SubagentStop` | Fired | both | `agent_id` matches its `SubagentStart`; adds `agent_transcript_path`, `last_assistant_message`. |
| `TaskCreated` | Did not fire | both | Subagent fan-out surfaced as `SubagentStart`/`SubagentStop` + `Agent`-tool `Pre/PostToolUse`, **not** `TaskCreated`/`TaskCompleted`. Those two appear to belong to a different "task" concept (not the subagent/`Agent`-tool lifecycle) and never fired here. |
| `TaskCompleted` | Did not fire | both | Same as `TaskCreated`. |
| `Stop` | Fired | both | Once per turn; `last_assistant_message`, `stop_hook_active:false`. **No `Stop` carried any interrupt/cancel marker** (see interrupt finding). |
| `StopFailure` | Not exercised | — | Requires an API error; not induced. |
| `TeammateIdle` | Not exercised | — | Agent-team feature. |
| `InstructionsLoaded` | Did not fire | both | Scratch project had no `CLAUDE.md`/`.claude/rules/*` to load. |
| `ConfigChange` | Not exercised | — | No config-edit beat. |
| `CwdChanged` | Not exercised | — | No `cd` beat. |
| `FileChanged` | Not exercised | — | Matcher is `.env|.envrc`; the subagent `Write` created `cat.txt`, which the matcher doesn't cover. |
| `WorktreeCreate` | N/A (skipped) | — | Not camped (replacement class). |
| `WorktreeRemove` | Not exercised | — | No `--worktree` beat. |
| `PreCompact` | Not exercised | — | No `/compact` beat. |
| `PostCompact` | Not exercised | — | Same. |
| `Elicitation` | Not exercised | — | No MCP-elicitation beat — the "lower confidence" safe classification stays unconfirmed. |
| `ElicitationResult` | Not exercised | — | Same. |
| `SessionEnd` | **Fired** | both | **The sync-at-teardown proof.** Captured on BOTH exits — `reason:"other"` on the `-p` one-shot exit, `reason:"prompt_input_exit"` on the interactive `/exit`. Two corrections it forces below. |

### Key structural findings

1. **`SessionEnd` is captured — the synchronous posture works.** It landed as the
   final record of **every** captured session (all four). The phase-13 async-loss
   bug (an async teardown hook killed before it completes) does not recur with
   `async:false` + `timeout:10`. This is the single most important row: the
   recorder exists partly to catch this regressing again.
2. **`SessionEnd`'s field is `reason`, not `exit_reason`.** This contradicts the
   phase-13 executor note recorded in `integrations/claude-code/README.md`
   ("SessionEnd carries `exit_reason` not `reason`"). Live payloads on 2.1.207 use
   **`reason`** (values seen: `other`, `prompt_input_exit`). *(Not editing the
   shipped phase-13 doc from this phase — the atv hook reads only `session_id`, so
   its behavior is unaffected; flagged here for whoever revises that doc.)*
3. **`-p` DOES fire `SessionEnd`.** `driver-scripted.ps1`'s header assumed the
   one-shot `-p` lifecycle "never reaches a `SessionEnd`" — the scripted capture
   disproves it (`reason:"other"`). Harmless (the scripted run got a bonus
   teardown proof); the header comment overstates the limitation.
4. **`agent_id` is the subagent tag across a subagent's whole tool lifecycle.**
   The events that carry a non-empty `agent_id` when subagent-scoped:
   `SubagentStart`, `SubagentStop`, `PreToolUse`, `PostToolUse`, `PostToolBatch`,
   `PostToolUseFailure`, **and `PermissionRequest`**. Main-thread instances of the
   same events carry an empty/absent `agent_id`. So "is this event from a subagent,
   and which one?" is answerable on all of them.
5. **A permission prompt's subagent origin is on `PermissionRequest`, not
   `Notification`.** `Notification:"permission_prompt"` never carries `agent_id`;
   the paired `PermissionRequest` does. Any future atv work that wants to attribute
   a permission prompt to a specific subagent card (the LIFE-24 "subagents → own
   cards" idea) must read `PermissionRequest`, not the `Notification` the shipped
   phase-13 integration currently maps.
6. **A user interrupt fires no distinguishing hook event — tested both ways.**
   (a) Interrupting *during text generation* (`cc-interactive-1`, no tool in
   flight) produced no `PostToolUseFailure`, no `StopFailure`, and no
   interrupt/cancel flag on any `Stop` — indistinguishable from an ordinary turn
   end. (b) Interrupting *during a tool call* (`cc-interactive-2`, `bash slow.sh`
   mid-sleep) fired the `PreToolUse` but then **nothing** — no `PostToolUse` and,
   crucially, **no `PostToolUseFailure`**. The tool call is simply *orphaned* at
   the hook layer (a `PreToolUse` with no completion event of any kind). So the
   `is_interrupt` field on `PostToolUseFailure` belongs to tools that fail on their
   own, **not** to user interrupts — an interrupt raises no event a hook can key
   off. (Implication for atv: a `run`-style wrapper cannot detect a user interrupt
   of a tool via hooks; only the missing `PostToolUse` — an absence — signals it.)

### LIFE-24 empirical item 2 — ANSWERED

*Which event types fire inside subagents; `agent_id` uniqueness across parallel
spawns; whether a subagent-triggered `Notification:permission_prompt` carries the
`agent_id`.*

- **Events fired for/inside subagents:** `SubagentStart`, `SubagentStop`, and the
  subagent's own `PreToolUse`/`PostToolUse`/`PostToolBatch`/`PostToolUseFailure`/
  `PermissionRequest` — all carrying that subagent's `agent_id` + `agent_type`
  (finding 4). `TaskCreated`/`TaskCompleted` do **not** fire for subagents.
- **`agent_id` uniqueness across PARALLEL spawns: yes.** Scripted fan-out →
  `a98cf7c791c5ca991` + `abec8786ede5ae2dc`; interactive → `a72aee33467652aa4` +
  `a7f043e61ee0d6fa0`. All four distinct; each threads consistently from its
  `SubagentStart` through its tool calls to its `SubagentStop`, and the
  subagent-originated `PermissionRequest` (`a7f043e61ee0d6fa0`) matches its
  `SubagentStart`.
- **Does `Notification:permission_prompt` carry `agent_id`? No** (finding 5). The
  attribution lives on `PermissionRequest` instead.

### LIFE-24 empirical item 3 — ANSWERED

*Does `idle_prompt` fire after a user interrupt, and on what timing/repetition?*

- **`idle_prompt` fires ~60 s after a turn completes, once.** In the clean idle
  test (`cc-interactive-3`): `Stop` at `07:53:00` → `Notification`
  `notification_type:"idle_prompt"` (message `"Claude is waiting for your input"`)
  at `07:54:00`, i.e. **exactly 60 s** after the turn ended. It fired **once** and
  did **not** repeat across the ~39 minutes the session then sat idle before
  `/exit`.
- **It is NOT focus-gated.** The `idle_prompt` fired while the terminal was still
  **focused** (the operator's focused-then-unfocused window straddled the 60 s
  mark and the notification landed in the focused portion). This **corrects** the
  earlier inference from the phase-13 dogfood that idle depended on
  unfocusing/backgrounding — that was coincidental timing, not a causal gate.
- **The "after an interrupt" sub-question stays only weakly answered.** In
  `cc-interactive-1` (interrupt then idle) no `idle_prompt` fired — but that
  session's longest post-`Stop` idle window was **47.6 s** (under the ~60 s
  threshold), and its one longer (~151 s) window was immediately pre-`/exit`. So
  the absence there is consistent with "never idle long enough after a clean
  turn", not proven interrupt-suppression. A clean interrupt-then-wait-past-60 s
  test was not isolated; recorded as attempted, per INFRA-29's organic-recapture
  posture. The firm result is the baseline: **~60 s after a normal turn end, once,
  focus-independent.**
