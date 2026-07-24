# Agent Task Void (`atv`)

A Windows CLI that gives a coding agent (or any long-running script) its own
persistent taskbar task card — a standalone Windows 11 taskbar icon,
independent of any app window, showing what it's doing, whether it needs
you, and when it's done.

A card is always in one of five states, ranked by the cost of ignoring it:
**Blocked** (stalled on a question for you), **Broken** (died without
delivering), **Ready** (finished — output awaits your review), **Working**,
and **Idle**. The taskbar badge tells you at a glance whether switching
back can wait.

Built on [`Windows.UI.Shell.Tasks.AppTaskInfo`](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.tasks.apptaskinfo),
an experimental Windows Shell API.

## Requirements

- Windows 11, build 26100+, with `AppTaskContract` v2.0 registered
- Developer Mode on (Settings → Privacy & security → For developers) —
  build/dev machines only

`AppTaskInfo` is experimental: some builds report unsupported even when new
enough. Run `atv doctor` to check the live status on your machine — it's the
authoritative answer, not a version number.

## Build from source

No packaged install yet (winget submission is pending — see
[`docs/release.md`](docs/release.md)). To build and run:

```
dotnet build
dotnet run --project src/Atv -- -- doctor
```

The double `--` is required: the first ends `dotnet run`'s own args, the
second hands the rest to `atv`. See [`CLAUDE.md`](CLAUDE.md) for the full dev
loop, and [`docs/release.md`](docs/release.md) to build a signed MSIX
locally.

## Quickstart: install a host plugin

Each supported agent host gets a ready-made plugin that drives task cards
from its own hooks — install it and cards appear on your next session.

### Claude Code

Drop (or symlink) the plugin folder into a skills directory Claude Code
already scans:

```
# personal, every project:
<repo>/integrations/claude-code/plugins/atv-integration  ->  ~/.claude/skills/atv-integration

# this repo only:
<repo>/integrations/claude-code/plugins/atv-integration  ->  <repo>/.claude/skills/atv-integration
```

It loads automatically on the next session; confirm with
`claude plugin list`. Full details, including the marketplace-install
alternative:
[`integrations/claude-code/README.md`](integrations/claude-code/README.md).

### GitHub Copilot CLI

```
copilot plugin install grork/AppTaskInfoCli:integrations/copilot-cli/plugins/atv-integration
```

Confirm with `copilot plugin list`. Full details:
[`integrations/copilot-cli/README.md`](integrations/copilot-cli/README.md).

### Codex

Not yet shipped.

## Out of the box

With zero configuration, a new card takes its identity from the repo it was
created in:

- **Title** — the working folder's name. **Subtitle** — the git branch.
- **Icon** — picked from a pool of 100+ curated Segoe glyphs and emoji by
  hashing the repo path, so the same repo always gets the same icon and
  different repos usually get different ones.
- **Click** — the card opens the folder the session works in.
- **Grouping** — cards with the same icon share one taskbar group, so a
  session's subagent cards stack under it.

An icon you choose yourself (`--icon`, or `icon` in `.atv.json` below) can
be a curated Segoe Fluent Icons name (`Robot`, `Bug`), a single emoji
(`🦀`), or your own PNG/JPG/ICO via `--icon-file`. Glyphs render white on
an accent-color tile so they stay visible on light and dark taskbars; emoji
render full-color.

## Branding a repo: `.atv.json`

Drop a `.atv.json` at the root of a repo to brand every card created there
— it applies to direct `atv` calls and to every installed plugin, with no
hook edits:

```json
{
  "title-template": "{repo} ({branch})",
  "icon": "🦀",
  "group": "true"
}
```

`atv` walks up from the working directory (or the `--cwd` a plugin
forwards) and the nearest `.atv.json` wins, stopping at the first `.git`
boundary — monorepo packages can each carry their own. Five keys are
allowed: `title-template` (with `{repo}` and `{branch}` tokens),
`subtitle`, `icon`, `icon-file`, and `group` (`"true"` merges every card
from the repo into one taskbar group). An explicit command-line flag always
beats the repo file. Anything else in the file is ignored and logged — in
particular `deep-link`, since a checked-out repo must never decide what
your card opens. Discovery runs when a card is created; an edit applies to
the next new card.

Full rules, plus the rest of the precedence chain (env vars, and the
per-user `atv-config.json` for machine-wide tunables like idle timers and
watchdog mode): [`docs/configuration.md`](docs/configuration.md).
`atv doctor` prints which `.atv.json` it found and the icon it would pick.

## Manual usage

For scripting `atv` directly, or building your own host integration. Every
card verb takes a required `<handle>` — an id you choose and reuse for the
same logical task. The first verb called on a new handle creates the card;
there is no separate "start" verb:

```
atv working build-123 --goal "Fix the release build"
atv activity build-123 --kind shell --label "dotnet test"
atv blocked build-123 --question "Should I force-push?"
atv ready build-123 --summary "Build fixed, tests green"
atv session-ended build-123 --reason finished
```

- **`working <handle> [--goal TEXT]`** — set this turn's goal line.
- **`activity <handle> --kind KIND [--label TEXT]`** — the "happening right
  now" line. `KIND` names the mechanism (`read`, `edit`, `write`, `search`,
  `shell`, `fetch`, `web-search`, `plan`, `compacting`, `tool`); the label
  carries the subject.
- **`blocked <handle> --question TEXT`** — the card demands attention with
  your question. Display only: you answer in the agent, and the card clears
  on its next event.
- **`ready <handle> [--summary TEXT]`** — the turn finished. A Ready card
  decays to Idle after you've had a chance to look — the clock only runs
  while the machine is unlocked and in use.
- **`broken <handle> --reason rate-limit|overloaded|api-error|timeout|fatal
  [--detail TEXT]`** — the session died; the card stays until you deal
  with it.
- **`agent-started` / `agent-stopped <handle> --agent ID`** — track
  subagents. Two or more running concurrently get their own child cards
  under the session's icon group; each retires on its `agent-stopped`.
- **`session-ended <handle> --reason finished|error`** — the session is
  over: `finished` removes the card, `error` marks it Broken.
- **`remove <handle>`** — remove one card now.
- **`run [--title T] -- <command...>`** — wrap any command: mints its own
  handle, mirrors the child's output onto a card as it runs, and exits with
  the child's exit code.
- **`list [--json]`** / **`clear [--include-recycle-bin]`** — list or wipe
  every card under this install (see note below).
- **`doctor [--json] [--verbose]`** — self-check; start here when a card
  isn't showing.

Every card verb also accepts `--title`, `--subtitle`, `--icon`/
`--icon-file`, and `--deep-link` on any call, and a free-text flag whose
value is exactly `-` reads that text from stdin. Failures never break the
caller: `atv` exits 0 and writes a durable log entry, unless `--strict`
asks for real exit codes. The full contract — idempotency, fan-out
addressing, the closed vocabularies —
is [`docs/integration-api.md`](docs/integration-api.md); `atv --help` has
the compact syntax and the global flags (`--json`, `--strict`, `--verbose`,
`--cwd`, `--watchdog-mode`, and more).

`list` and `clear` operate on every task under the current Windows package
identity, not just tasks you created — there's no per-consumer
partitioning. Use `remove <handle>` for routine cleanup of your own task.

## Docs

- [`CLAUDE.md`](CLAUDE.md) — build/dev-loop, package identity model, release build
- [`docs/integration-api.md`](docs/integration-api.md) — the verb contract, for building a host integration
- [`docs/configuration.md`](docs/configuration.md) — every tunable, env var, config file
- [`docs/release.md`](docs/release.md) — signed MSIX build + install runbook
- [`docs/windows-ui-shell-tasks/`](docs/windows-ui-shell-tasks/README.md) — reference for the underlying WinRT API
- [`docs/testing/fake-fidelity-promises.md`](docs/testing/fake-fidelity-promises.md) — what the test fake does and doesn't mimic
