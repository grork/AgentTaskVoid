# Agent Task Void (`atv`)

A Windows CLI that gives a coding agent (or any long-running script) its own
**persistent taskbar task card** — a standalone Windows 11 taskbar icon,
independent of any app window, showing what it's doing, whether it needs
you, and when it's done.

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

## Quickstart

Each supported agent host gets a ready-made plugin that drives task cards
from its own hooks — nothing to write yourself, just install it.

### Claude Code

Drop (or symlink) the plugin folder into a skills directory Claude Code
already scans, then start a session:

```
# personal, every project:
<repo>/integrations/claude-code/plugins/atv-integration  ->  ~/.claude/skills/atv-integration

# this repo only:
<repo>/integrations/claude-code/plugins/atv-integration  ->  <repo>/.claude/skills/atv-integration
```

No `settings.json` edits, no commands. Confirm with `claude plugin list`.
Full details, including the marketplace-install alternative:
[`integrations/claude-code/README.md`](integrations/claude-code/README.md).

### GitHub Copilot CLI

```
copilot plugin install grork/AppTaskInfoCli:integrations/copilot-cli/plugins/atv-integration
```

Confirm with `copilot plugin list`. Full details:
[`integrations/copilot-cli/README.md`](integrations/copilot-cli/README.md).

### Codex

Not yet shipped.

## Manual usage

For scripting `atv` directly, or building your own host integration. Every
lifecycle verb takes a required `<handle>` — an id you choose and reuse for
the same logical task:

```
atv start build-123 --title "Fixing the build" --subtitle "release branch"
atv step build-123 "Running the test suite"
atv attention build-123 "Should I force-push?"
atv state build-123 running
atv done build-123 --summary "Build fixed, tests green"
atv remove build-123
```

- **`start <handle>`** — create or adopt (safe to call again on a live
  handle; resumes `Running` without losing step history). Flags: `--title`,
  `--subtitle`, `--icon`, `--deep-link`, `--reset`.
- **`step <handle> <message>`** — set the "what's happening now" line.
- **`state <handle> running|paused`** — pause/resume.
- **`attention <handle> <question>`** — flag the card as needing you
  (display-only; `atv` can't carry your answer back to the agent).
- **`done`** / **`fail <handle> [--summary TEXT]`** — mark complete/errored;
  the card auto-removes itself after an idle period.
- **`remove <handle>`** — remove one task now.
- **`list [--json]`** / **`clear [--include-recycle-bin]`** — list or wipe
  every task under this install (see note below — these are not per-agent
  scoped).
- **`run --title T -- <command...>`** — wrap another command: mints a
  handle, mirrors its output, drives the card from its lifecycle, exits with
  its exit code.
- **`doctor [--json] [--verbose]`** — self-check; start here if a card isn't
  showing.

Omit `--icon` and a new card gets an icon keyed to the repo it's created in
(same repo, same icon, every time — not the fixed Robot glyph); omit
`--deep-link` and it opens the folder the session works in, instead of
`atv`'s own app-data folder. See
[`docs/configuration.md`](docs/configuration.md) for the exact pick rule and
its floors.

Global flags: `--json`, `--strict` (real nonzero exit codes), `--verbose`,
`--watchdog-mode spawn|inproc|off`, `--unsafe`, `--wait-for-debugger`. Run
`atv --help` for the full current syntax.

`list` and `clear` operate on every task under the current Windows package
identity, not just tasks you created — there's no per-consumer partitioning.
Use `remove <handle>` for routine cleanup of your own task.

## Docs

- [`CLAUDE.md`](CLAUDE.md) — build/dev-loop, package identity model, release build
- [`docs/configuration.md`](docs/configuration.md) — every tunable, env var, config file
- [`docs/release.md`](docs/release.md) — signed MSIX build + install runbook
- [`docs/windows-ui-shell-tasks/`](docs/windows-ui-shell-tasks/README.md) — reference for the underlying WinRT API
- [`docs/testing/fake-fidelity-promises.md`](docs/testing/fake-fidelity-promises.md) — what the test fake does and doesn't mimic
