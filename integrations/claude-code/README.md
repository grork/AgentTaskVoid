# Claude Code integration (v2 plugin)

Wires a [Claude Code](https://code.claude.com/docs/en/hooks) session to `atv`
(brand **Agentaskvoid**) taskbar task cards: one card per Claude Code
session, appearing/updating/disappearing as the session works, needs your
input, finishes a turn, fails, or ends — with fanned-out subagents getting
their own glomming child cards.

This is a native **Claude Code plugin** (DIST-11), superseding the phase-13
`settings.hooks.json` one-liner fragment. It bundles the hooks declaration
and the translator script together, so installing the plugin delivers the
files *and* wires the hooks in one step — no hand-editing `settings.json`.

This artifact is host-agnostic on the `atv` side — every hook line below
calls only the [v2 semantic verb set](../../docs/integration-api.md)
(`working`/`activity`/`blocked`/`ready`/`broken`/`agent-started`/
`agent-stopped`/`session-ended`, ERGO-31). All Claude-Code-specific logic
(event names, payload field names, matchers) lives here, in this plugin,
never in the `atv` binary itself (LIFE-10).

## Layout

```
integrations/claude-code/
├── .claude-plugin/marketplace.json     # a local marketplace listing the one plugin below
├── plugins/atv-integration/
│   ├── .claude-plugin/plugin.json      # the plugin manifest
│   ├── hooks/hooks.json                # the hooks declaration (11 events)
│   ├── translate.ps1                   # the real translator script
│   └── map.json                        # first-party event/tool -> verb/kind table (atv never reads this)
└── README.md                           # this file
```

## Install

**Verified against:** Claude Code **2.1.209** installed on the build
machine (`claude --version`); the reference doc stamp this integration was
authored against is Claude Code **2.1.207** (`docs/host-events/claude-code.md`,
phase 14's capture). This is a minor point-release gap (209 vs 207), not a
material drift — no re-capture was run for this pass; see "Capture staleness"
below.

Every field/schema claim in this README and in `hooks.json`/`plugin.json`
was verified against a **live fetch** of the current primary docs during
this build (`code.claude.com/docs/en/plugins-reference`,
`.../discover-plugins`, `.../plugin-marketplaces`, `.../hooks`, `.../settings`)
— not carried over from training-data memory (the phase-13 lesson: a
memory-authored hook config looked right and was subtly wrong).

Two ways to install, both **local/path-based** (marketplace publication is
explicitly out of scope for this build — DIST-11):

### Option A — skills-directory plugin (zero `settings.json` edits, zero commands)

Drop (or symlink) the `plugins/atv-integration/` folder itself under a
skills directory Claude Code already scans:

- **Personal, every project:** `~/.claude/skills/atv-integration/`
- **This repo only, shared with collaborators once trusted:**
  `<repo>/.claude/skills/atv-integration/`

On the next Claude Code session, it loads automatically as
`atv-integration@skills-dir` — no marketplace, no install step, and (per the
current plugin docs) genuinely **no `settings.json` entry at all**. Confirm
it loaded with `claude plugin list` or `/plugin` → **Installed**.

### Option B — local marketplace + explicit install

```
/plugin marketplace add integrations/claude-code
/plugin install atv-integration@agentaskvoid
```

(or the non-interactive form: `claude plugin install atv-integration@agentaskvoid`,
after adding the marketplace). This writes a normal `enabledPlugins` entry to
your chosen scope's settings file (`user`/`project`/`local` — pass `--scope`)
— still no *hand*-editing: the CLI writes it for you.

Either way, after install: `atv doctor` should report identity present +
API supported, and a "Claude Code" card should appear within ~1s of your
next prompt.

### Uninstall

- Option A: delete the folder (or `claude plugin disable atv-integration@skills-dir`
  — there's no marketplace install to remove).
- Option B: `/plugin uninstall atv-integration@agentaskvoid` (or
  `claude plugin uninstall atv-integration@agentaskvoid --scope <scope>`),
  then optionally `/plugin marketplace remove agentaskvoid`.

No hooks fire once disabled/uninstalled — the hooks live entirely inside the
plugin bundle, nothing is left behind in `settings.json` to hand-clean.

## Event → verb mapping

| Claude Code event | Matcher | `atv` call | Notes |
|---|---|---|---|
| `SessionStart` | *(all sources)* | *(no-op, except `source:"compact"` → `activity <sid> --kind compacting`)* | No session-start verb exists (ERGO-31). |
| `UserPromptSubmit` | *(none)* | `working <sid> --goal -` (+ `--title <session_title>` when the user explicitly named the session, ERGO-33) | Prompt text via stdin. |
| `PreToolUse` / `PostToolUse` | *(none)* | `activity <sid> --kind <map> --label -` (+ `--agent`/`--name` when the payload carries them) | `Agent` (subagent spawn) is suppressed — no activity line, ever (agent-started/-stopped own that). `TodoWrite` composes a `(n/m) <item>` plan label in code. An unmapped tool falls to `--kind tool --name <tool_name> --label -`. |
| `PermissionRequest` | *(none)* | `blocked <sid> --question -` (+ `--agent` when present) | Attribution keys off `PermissionRequest`, **not** `Notification` (phase-14 capture finding 5). |
| `Notification` | `idle_prompt` only | `ready <sid>` | `permission_prompt` is a deliberate no-op — `PermissionRequest` already owns Blocked. Fires ~60s post-turn, once, focus-independent (phase-14 finding). |
| `Stop` | *(none — unsupported)* | `ready <sid> --summary -` | `last_assistant_message` via stdin. |
| `StopFailure` | *(none)* | `broken <sid> --reason <map> --detail -` | **Never captured live** (phase 14: "not induced") — best-effort field reading, flagged below. |
| `SubagentStart` | *(none)* | `agent-started <sid> --agent <id> --name <type>` | |
| `SubagentStop` | *(none)* | `agent-stopped <sid> --agent <id>` | |
| `SessionEnd` | *(none)* | `session-ended <sid> --reason finished` | The **one** synchronous hook (`timeout: 10`, no `async`) — the phase-13 teardown-race lesson (INFRA-27). `reason` field confirmed by the phase-14 capture (not `exit_reason`); both observed values (`other`/`prompt_input_exit`) map to `finished`. |

Every hook line is a plain **program+args exec-form** invocation —
`command: powershell.exe`, `args: [..., "-File", "${CLAUDE_PLUGIN_ROOT}/translate.ps1", "-Event", "<Name>", ...]`
— never an embedded one-liner, never a `shell` selection (LIFE-25). Every
non-terminal event also passes `-ProjectDir "${CLAUDE_PROJECT_DIR}"`, the
literal placeholder Claude Code substitutes before spawning; `translate.ps1`
forwards it as `--cwd` on every upserting call (ERGO-30), letting a repo's
own `.atv.json` brand its cards with zero hook edits (phase 17). `SessionEnd`
alone carries no `-ProjectDir` (`session-ended` takes no `--cwd`, no upsert).

No hook line ever passes `--strict` (FAIL-1's non-disruptive posture).

## Deliberately no identity flags, except the host's own session name (`--title`)

Unlike the phase-13 v1 artifact, `translate.ps1` never passes
`--subtitle`/`--icon`/`--icon-file` on any call. This is intentional:
`SemanticEngine.ApplyRepoDefaults` (`src/Atv/Semantics/SemanticEngine.cs`)
resolves title/subtitle/icon with `--flag > env > repo (.atv.json) > user
config > built-in default` precedence — a caller-supplied flag **always**
wins over a repo's `title-template`/`subtitle`/`icon`. A translator that
hard-coded `--subtitle "Claude Code"` on every call would permanently block
phase 17's repo-branding feature for every repo using this plugin. Leaving
these unset lets `.atv.json` (or, absent that, `atv`'s own defaults) resolve
them instead.

`--title` is the one exception (ERGO-33, phase 19): on `UserPromptSubmit` —
the event that creates the card — `translate.ps1` forwards Claude Code's own
`session_title` field as `--title`, but **only when the user has explicitly
named the session**; it is absent otherwise, and no `--title` token is passed
at all. This does not reopen the concern above: that argument is against a
**hard-coded constant**, which would always win regardless of repo config. A
value present only on explicit user intent is exactly what the top of the
`--flag > env > repo > user > built-in default` chain is for, and every other
event still passes no identity flags at all.

A repo with no `.atv.json` and no user-named session no longer gets an
empty-titled card: `SemanticEngine.ApplyRepoDefaults` now terminates the
chain in a built-in default derived from the `--cwd`/repo anchor — the
anchor folder's own name, plus the repo folder name in parentheses when the
anchor sits below the discovered `.git` root (suppressed when the two
coincide), floored at the brand name (`Agentaskvoid`) for an anchor with no
last path segment (a bare drive root). The default subtitle is the resolved
git branch when a `.git` root is found, empty otherwise. Empty is never the
final title — see `docs/configuration.md`'s defaults table for the full
chain and examples.

## Assumptions flagged (never captured live in phase 14)

Three payload shapes in the table above were never exercised by the phase-14
recorder capture (`docs/host-events/claude-code.md`'s Findings section marks
each "Not exercised"). Each is implemented as the most reasonable reading of
ERGO-31/the current hooks docs, and should be double-checked against a real
capture if one becomes available:

- **`StopFailure`** — no real payload was ever observed (requires an
  induced API error). `translate.ps1` reads a `reason` field (falling back
  to `error_type`), maps it through `map.json`'s `brokenReason` table
  (`rate_limit`/`overloaded`/`api_error`/`timeout` → the ERGO-31 tokens),
  and defaults to `fatal` for anything else; `--detail` comes from an
  `error` or `message` field if present.
- **`SessionStart` with `source:"compact"`** — no `/compact` beat was in the
  phase-14 corpus. Implemented per `docs/integration-api.md`'s own
  documented optional row.
- **`TodoWrite`** (via `PreToolUse`/`PostToolUse`) — no beat exercised it.
  `translate.ps1` composes `(n/m) <item>` from the tool's `todos` array
  (picking the first `in_progress` item, or a position derived from how many
  are already `completed` if none is in progress) — per ERGO-31 §3's own
  documented composition guidance.

## Capture staleness (AC3)

Installed Claude Code on the build machine: **2.1.209** (`claude --version`).
The phase-14 capture findings this plugin's mapping is built from are stamped
**2.1.207**. This is a two-point-release gap; per the phase file's own
staleness-gate instructions, this pass did **not** attempt a live re-capture
(that requires a supervised interactive session, out of an unattended
executor's safety-constrained scope) — flagged here for the orchestrator to
decide whether a fresh INFRA-29 organic re-capture is warranted before the
live dogfood (AC5) runs.

## Verified live vs. verified-against-docs vs. verified-offline

- **Verified against live docs + real phase-14 captures (this build):**
  the plugin manifest schema, the hooks declaration shape (exec form, no
  `shell`, `${CLAUDE_PLUGIN_ROOT}`, matcher semantics), and every payload
  field this translator reads were checked against a fresh fetch of the
  current primary docs and/or the raw phase-14 capture JSONL
  (`tools/host-event-recorder/captures/session-cc-*.jsonl`).
- **Verified offline (this build):** `translate.ps1` was driven under real
  Windows PowerShell 5.1 against a stub `atv` (never the real binary) with
  payloads built from the real captures — every routing row, the UTF-8
  torture-payload byte-fidelity claim, and the unmapped-tool fallback are
  covered by `tests/Atv.LogicTests/Integrations/ClaudeCodeTranslatorTests.cs`.
  The plugin's local-install wiring (zero `settings.json` edits) was verified
  structurally in a throwaway scratch directory (`${CLAUDE_PLUGIN_ROOT}`
  authored correctly, resulting config shapes inspected) — not by launching
  a real Claude Code session.
- **NOT yet verified — deferred to an operator-supervised session (this
  plugin's own AC5/AC6):** a real Claude Code session driving the card
  through Working (goal + activity lines) / Blocked (a real permission
  prompt) / Ready (turn summary) / fan-out (≥2 parallel subagents) /
  removal on `/exit`, and a repo's `.atv.json` branding a card through the
  real conduit. Doc-only/offline verification is explicitly insufficient on
  its own (the phase-13 precedent: both its real live bugs — a hallucinated
  matcher and an async teardown race — were invisible on paper).
