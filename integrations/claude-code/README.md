# Claude Code integration (v2 plugin)

Wires a [Claude Code](https://code.claude.com/docs/en/hooks) session to `atv`
(brand **Agent Task Void**) taskbar task cards: one card per Claude Code
session, appearing/updating/disappearing as the session works, needs your
input, finishes a turn, fails, or ends — with fanned-out subagents getting
their own glomming child cards.

This is a native Claude Code plugin (DIST-11): installing it delivers the
translator script and wires the hooks in one step, with no hand-editing of
`settings.json`.

This artifact is host-agnostic on the `atv` side — every hook line below
calls only the [v2 semantic verb set](../../docs/integration-api.md)
(ERGO-31). All Claude-Code-specific logic
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

## Compatibility

Targets Claude Code's plugin/hooks API as documented at
`code.claude.com/docs/en/{plugins-reference,discover-plugins,plugin-marketplaces,hooks,settings}`.
[`docs/host-events/claude-code.md`](../../docs/host-events/claude-code.md) cross-checks every payload field this
translator reads against real captures — see that file for the Claude Code
version and capture date behind each finding. If a newer host version
behaves differently, re-run that capture to update the finding.

## Install

Two ways, both local/path-based (marketplace publication is out of scope for
this integration — DIST-11):

### Option A — skills-directory plugin (no settings.json edit, no install command)

Drop (or symlink) the `plugins/atv-integration/` folder itself under a
skills directory Claude Code already scans:

- **Personal, every project:** `~/.claude/skills/atv-integration/`
- **This repo only, shared with collaborators once trusted:**
  `<repo>/.claude/skills/atv-integration/`

On the next Claude Code session, it loads automatically as
`atv-integration@skills-dir` — no marketplace, install step, or
`settings.json` entry required. Confirm it loaded with `claude plugin list`
or `/plugin` → **Installed**.

### Option B — local marketplace + explicit install

```
/plugin marketplace add integrations/claude-code
/plugin install atv-integration@agent-task-void
```

(or the non-interactive form: `claude plugin install atv-integration@agent-task-void`,
after adding the marketplace). This writes a normal `enabledPlugins` entry to
your chosen scope's settings file (`user`/`project`/`local` — pass `--scope`).
The CLI writes this entry; there's no hand-editing involved.

Either way, after install: `atv doctor` should report identity present +
API supported, and a "Claude Code" card should appear within ~1s of your
next prompt.

### Uninstall

- Option A: delete the folder (or `claude plugin disable atv-integration@skills-dir`
  — there's no marketplace install to remove).
- Option B: `/plugin uninstall atv-integration@agent-task-void` (or
  `claude plugin uninstall atv-integration@agent-task-void --scope <scope>`),
  then optionally `/plugin marketplace remove agent-task-void`.

## Event → verb mapping

| Claude Code event | Matcher | `atv` call | Notes |
|---|---|---|---|
| `SessionStart` | *(all sources)* | *(no-op, except `source:"compact"` → `activity <sid> --kind compacting`)* | No session-start verb exists (ERGO-31). |
| `UserPromptSubmit` | *(none)* | `working <sid> --goal -` (+ `--title <session_title>` when the user explicitly named the session, ERGO-33) | Prompt text via stdin. |
| `PreToolUse` / `PostToolUse` | *(none)* | `activity <sid> --kind <map> --label -` (+ `--agent`/`--name` when the payload carries them) | `translate.ps1` suppresses `Agent` (subagent spawn) — no activity line, ever (agent-started/-stopped own that). `TodoWrite` composes a `(n/m) <item>` plan label in code. An unmapped tool falls to `--kind tool --name <tool_name> --label -`. |
| `PermissionRequest` | *(none)* | `blocked <sid> --question -` (+ `--agent` when present) | Attribution keys off `PermissionRequest`, not `Notification`. |
| `Notification` | `idle_prompt` only | `ready <sid>` | `permission_prompt` is a deliberate no-op — `PermissionRequest` already owns Blocked. Fires ~60s post-turn, once, focus-independent. |
| `Stop` | *(none — unsupported)* | `ready <sid> --summary -` | `last_assistant_message` via stdin. |
| `StopFailure` | *(none)* | `broken <sid> --reason <map> --detail -` | Never observed in a live capture (requires an induced API error) — best-effort field reading, see "Not yet observed live" below. |
| `SubagentStart` | *(none)* | `agent-started <sid> --agent <id> --name <type>` | |
| `SubagentStop` | *(none)* | `agent-stopped <sid> --agent <id>` | |
| `SessionEnd` | *(none)* | `session-ended <sid> --reason finished` | The one synchronous hook (`timeout: 10`, no `async`), to avoid a teardown race (INFRA-27). Both observed `reason` values (`other`/`prompt_input_exit`) map to `finished`. |

Every hook line is a plain program+args exec-form invocation —
`command: powershell.exe`, `args: [..., "-File", "${CLAUDE_PLUGIN_ROOT}/translate.ps1", "-Event", "<Name>", ...]`
— not an embedded one-liner or a `shell` selection (LIFE-25). Every
non-terminal event also passes `-ProjectDir "${CLAUDE_PROJECT_DIR}"`;
`translate.ps1` forwards it as `--cwd` on every upserting call (ERGO-30),
letting a repo's own `.atv.json` brand its cards with zero hook edits.
`SessionEnd` alone carries no `-ProjectDir` (`session-ended` takes no
`--cwd`, no upsert).

No hook line passes `--strict` (FAIL-1's non-disruptive posture).

## Identity flags: none, except `--title`

`translate.ps1` never passes `--subtitle`/`--icon`/`--icon-file` on any call.
`SemanticEngine.ApplyRepoDefaults` (`src/Atv/Semantics/SemanticEngine.cs`)
resolves title/subtitle/icon with `--flag > env > repo (.atv.json) > user
config > built-in default` precedence, and a caller-supplied flag always
wins over a repo's `title-template`/`subtitle`/`icon`. A translator that
hard-coded `--subtitle "Claude Code"` on every call would block repo
branding for every repo using this plugin. Leaving these unset lets
`.atv.json` (or, absent that, `atv`'s own defaults) resolve them instead.

`--title` is the one exception (ERGO-33): on `UserPromptSubmit` — the event
that creates the card — `translate.ps1` forwards Claude Code's own
`session_title` field as `--title`, only when the user has explicitly named
the session; it is absent otherwise. The concern above is about a
hard-coded constant, which would always win regardless of repo config; a
value present only on explicit user intent sits at the top of the
precedence chain for exactly that reason, and every other event still
passes no identity flags at all.

A repo with no `.atv.json` and no user-named session still gets a
non-blank title: `SemanticEngine.ApplyRepoDefaults` terminates the chain in
a built-in default derived from the `--cwd`/repo anchor. See
[`docs/configuration.md`](../../docs/configuration.md)'s defaults table for the full chain and examples.

## Not yet observed live

`translate.ps1` implements three payload shapes from the documented
contract rather than a real capture (`docs/host-events/claude-code.md`'s
Findings section marks each "Not exercised"). Double-check against a real
capture if the behavior looks wrong:

- **`StopFailure`** — requires an induced API error. `translate.ps1` reads a
  `reason` field (falling back to `error_type`) and maps it through
  `map.json`'s `brokenReason` table onto the ERGO-31 reason vocabulary
  ([`docs/integration-api.md`](../../docs/integration-api.md) §4), defaulting to `fatal` for anything
  unmapped; `--detail` comes from an `error` or `message` field if present.
- **`SessionStart` with `source:"compact"`** — `translate.ps1` implements
  this per [`docs/integration-api.md`](../../docs/integration-api.md)'s documented optional row.
- **`TodoWrite`** (via `PreToolUse`/`PostToolUse`) — `translate.ps1` composes
  `(n/m) <item>` from the tool's `todos` array (picking the first
  `in_progress` item, or a position derived from how many are already
  `completed` if none is in progress), per ERGO-31 §3's composition guidance.

Also not yet verified in a live session: a real Claude Code session driving
the card through every state ([`docs/integration-api.md`](../../docs/integration-api.md) §1), including
fan-out and removal on `/exit`, and a repo's `.atv.json` branding a card
through the real conduit. [`tests/Atv.LogicTests/Integrations/ClaudeCodeTranslatorTests.cs`](../../tests/Atv.LogicTests/Integrations/ClaudeCodeTranslatorTests.cs)
covers `translate.ps1`'s routing logic against real captured payloads, but
that test runs against a stub `atv`, not a live session.
