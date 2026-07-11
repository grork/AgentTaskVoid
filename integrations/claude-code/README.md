# Claude Code integration

Wires a [Claude Code](https://code.claude.com/docs/en/hooks) session to `atv`
(brand **Agentaskvoid**) taskbar task cards: one card per Claude Code session,
appearing/updating/disappearing as the session starts, works, needs your
input, finishes a turn, and ends.

This artifact is host-agnostic on the `atv` side — every hook line below
calls only the documented verb set (`start`/`step`/`state`/`attention`/`done`/
`remove`). All Claude-Code-specific logic (event names, payload field names,
matchers) lives here, in this integration, never in the `atv` binary itself.

**Verified against:** Claude Code **2.1.207** (installed on the build
machine, `claude --version`) and the official hooks reference at
<https://code.claude.com/docs/en/hooks> (fetched 2026-07-10 — note this URL is
where `docs.claude.com/en/docs/claude-code/hooks` now redirects; the docs site
moved since the phase's original 2026-07-02 research). Every field the hook
commands actually read (`session_id`, `cwd`, `tool_name`, `tool_input`,
`message`) plus the `Notification` matcher key (`notification_type`) is quoted
from that fetch, not carried over from memory. Note: `Stop` exposes **no**
`stop_reason` field and supports **no** matcher (the docs list it among the
events where "a `matcher` field is silently ignored") — see the mapping table.

## Install

1. Open (or create) your Claude Code settings file:
   - User-wide (recommended — fires for every project you work in):
     `~/.claude/settings.json` (Windows: `%USERPROFILE%\.claude\settings.json`)
   - Project-only (shareable, commit to the repo): `.claude/settings.json`
2. Merge the `hooks` object from [`settings.hooks.json`](settings.hooks.json)
   in this folder into your settings file. If you already have a `hooks`
   block, merge event-by-event (arrays of matcher-groups) rather than
   overwriting — Claude Code merges matching hooks and runs them in parallel,
   so duplicated events would double-fire.
3. Make sure `atv` is on `PATH` (a normal install via `winget install
   Agentaskvoid.Atv`, or the dev-loop `AppExecutionAlias`, both put it there).
   No further setup — every hook command guards on `Get-Command atv
   -ErrorAction SilentlyContinue` and is a silent no-op if `atv` isn't
   installed yet, so it's safe to install this hook config before installing
   `atv` itself.
4. Start (or resume) a Claude Code session in any project. A taskbar card
   should appear titled "Claude Code".

No script files to copy — each hook is a single-line PowerShell command
embedded directly in the JSON `command` field (`"shell": "powershell"`), so
installing is exactly "paste this JSON block into your settings."

## Event → verb mapping

| Claude Code event | Matcher | `atv` call | Why |
|---|---|---|---|
| `SessionStart` | *(all sources: `startup`, `resume`, `clear`, `compact`)* | `atv start <session_id> --title "Claude Code" --subtitle <folder> --icon Robot` | Upsert (ERGO-25) — safe to re-fire on every source, including `resume`, without duplicating or resetting an existing card. |
| `PostToolUse` | *(all tools)* | `atv state <session_id> running` then `atv step <session_id> "<tool>: <input>"` | The state reset runs **every time**, ahead of the step, specifically to undo a prior `attention` (see caveat below) — chaining it into the same hook line avoids depending on a second event firing in the right order. |
| `Notification` | `permission_prompt`, `idle_prompt` | `atv attention <session_id> "<message>"` | These two notification types are the "needs a human" moments; the other six (`auth_success`, `elicitation_*`, `agent_needs_input`, `agent_completed`) are left unmapped — auth/elicitation/subagent chatter isn't "this session needs you" in the same sense. |
| `Stop` | *(none — `Stop` doesn't support matchers)* | `atv done <session_id>` | Fires on **every** turn completion (turn-done semantics). Claude Code doesn't fire `Stop` on a user interrupt, so no matcher is needed (and none is possible — a `matcher` on `Stop` is silently ignored). See caveat below. |
| `SessionEnd` | *(all reasons)* | `atv remove <session_id>` | Best-effort cleanup on `clear`/`resume`/`logout`/`prompt_input_exit`/`bypass_permissions_disabled`/`other`. Not fired on a hard kill/crash/closed-terminal — the watchdog's idle reap is the backstop for those (LIFE-17/18). **This is the one hook that is deliberately *synchronous* (no `async`)** — see below. |

None of the five hook lines ever pass `--strict` — every `atv` invocation
stays on the non-disruptive exit-0-always posture (FAIL-1), which matters
most for `PostToolUse`: a `preToolUse`-style fail-closed host would be a
correctness risk here, but Claude Code's `PostToolUse` hooks have no decision
control at all (per the docs: "cannot block; already ran"), and even so, exit
0 is used throughout for consistency and to keep every hook's stderr/exit
code out of your transcript.

## The state-reset-after-`attention` caveat (phase 05, ratified 2026-07-07)

`step` **preserves** the card's current state. If a card is sitting in
`NeedsAttention` (because a `Notification` fired `atv attention`) and the very
next `PostToolUse` just called `atv step` directly, the write would be
refused non-disruptively (silently, exit 0, nothing shown) — `step` rebuilds
`SequenceOfSteps` content with no question attached, and
`(SequenceOfSteps, NeedsAttention, no question)` is outside the ERGO-10 safe
combination matrix (`src/Atv/Operations/SafeCombinationMatrix.cs`).

This artifact resolves it by chaining `atv state <session_id> running` ahead
of every `step` call, unconditionally, inside the same `PostToolUse` hook
line. `state running` is valid from **any** current state — including
`NeedsAttention` and `Completed` — because `TaskOperations.SetState` rebuilds
content from the readable steps and drops any pending question rather than
gating on the task's prior state. The extra `atv state` call on every tool
use is a deliberate simplicity/robustness trade: it costs one extra fast
process launch per tool call, in exchange for never depending on hook
firing order across two different Claude Code events (e.g. assuming
`UserPromptSubmit` always fires between a permission-prompt `attention` and
the next tool call — it doesn't necessarily, since approving a permission
dialog can resume the *same* tool call without a fresh user prompt).

## The `Stop`-is-"turn-done"-not-"task-done" caveat

`Stop` fires at the end of **every** assistant turn, not just when the whole
session's work is finished — a single Claude Code session can turn `done`
(Completed) and then `running` again (via the next `PostToolUse`'s chained
state reset) many times over its life. This is intentional, matches the
phase design ("turn-done" semantics), and is harmless: a `Completed` card
only actually disappears if it lingers idle past its configured period
(`idle-completed`, ~10 minutes by default) with no further activity, or the
session truly ends (`SessionEnd` → `remove`). An actively-worked session
never sits idle long enough to be reaped between turns.

## Verified live vs. verified-against-docs

- **Verified against docs + a local functional smoke test (this build):**
  every event/field name below was fetched fresh from
  `code.claude.com/docs/en/hooks` (not recalled from training data), and
  every embedded PowerShell command was syntax-checked
  (`[System.Management.Automation.Language.Parser]::ParseInput`) and
  functionally exercised against representative mock JSON stdin + a stub
  `atv` — confirming JSON parsing, positional-argument passing (including a
  space-containing `--subtitle`), the truncation logic, and the two-call
  `state`-then-`step` chain all produce the expected `atv` invocations.
- **NOT yet verified:** a real, live Claude Code session driving this hook
  config end-to-end against the real `atv` binary (a real taskbar card
  appearing/stepping/needing-attention/completing without perturbing the
  session). That is the supervised dogfood the orchestrator + operator run
  next — see the phase report for the exact steps.

## Suggested live dogfood steps (for the supervised run)

1. Install this hook config user-wide (`~/.claude/settings.json`) and confirm
   `atv doctor` reports identity present + API supported.
2. Start a fresh Claude Code session in any project directory. Expect a
   "Claude Code" card to appear within ~1s of the prompt.
3. Ask Claude to run a couple of tool calls (e.g. "list the files in this
   directory, then read one"). Expect the card's step text to update after
   each `PostToolUse` fires.
4. Trigger a real permission prompt (e.g. ask for a `Bash` command in
   `default` permission mode, if not already auto-approved) and confirm the
   card flips to "needs attention" showing the prompt text, then confirm a
   subsequent tool call's step text lands correctly (proving the
   `state running` reset worked).
5. Let Claude finish responding; confirm the card shows Completed
   momentarily, then (if you send another prompt) flips back to Running.
6. End the session cleanly (`/exit` or closing the terminal via the normal
   path, not a hard kill) and confirm the card disappears
   (`atv list --json` returns `[]`, or the icon vanishes from the taskbar).

Throughout: watch for any hook error surfaced in Claude Code's own UI/debug
log (`claude --debug`), and confirm no perceptible latency was added to tool
calls or turn completion (the four in-session hooks — `SessionStart`,
`PostToolUse`, `Notification`, `Stop` — are all `"async": true`, so they never
block the session).

## Why `SessionEnd` is synchronous (a dogfood finding, 2026-07-10)

`SessionEnd` is the **only** hook here that is NOT `async`. During the first
live dogfood, an `async` `SessionEnd` hook left the card behind on `/exit`: an
async hook "runs in the background without blocking" (per the Claude Code
hooks docs), so as the session process tears down, the fire-and-forget
`atv remove` child was killed before it finished — no removal, no log entry.
The docs' recommended pattern for cleanup-on-exit is a **synchronous** hook,
which Claude Code awaits (up to its `timeout`) before exiting. Making
`SessionEnd` synchronous (`timeout: 10`) fixed it: `atv remove` completes and
the card disappears on a clean exit. The imperceptible teardown delay (a
sub-second `atv remove`) is the correct trade for reliable cleanup; unclean
deaths still fall through to the watchdog's idle reap regardless.
