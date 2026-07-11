# Agentaskvoid (`atv`)

A small Windows CLI that gives a coding agent (or any long-running script) a
**persistent taskbar task card** — a standalone Windows 11 taskbar icon,
grouped independently of any running app window, that shows what the agent
is doing, whether it needs you, and when it's done.

**This is not jump lists.** Jump lists are a per-app right-click menu on a
pinned icon. `atv` drives
[`Windows.UI.Shell.Tasks.AppTaskInfo`](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.tasks.apptaskinfo)
— a completely different, experimental Windows Shell API that creates its
own separate taskbar icons, independent of any window or pinned app. If
you're picturing a jump list, that's the wrong mental model.

## Why

Agent CLIs (Claude Code, GitHub Copilot CLI, Codex, …) run in a terminal you
have to keep looking at to know whether they're working, stuck waiting on
you, or finished. `atv` gives each session its own taskbar card instead —
glanceable state, at a distance, without a terminal in focus. Hook it up
once per host (see [Wiring up a host](#wiring-up-a-host) below) and every
session gets a card automatically.

## Requirements (soft floor — see the note below)

- Windows 11, build 26100+, with `AppTaskContract` v2.0 registered
- Windows 11 Developer Mode on (Settings → Privacy & security → For
  developers) — **dev/build machines only**, not needed on an end-user
  machine with a proper installed package

These numbers are an *expectation-setting* floor, not something `atv`
enforces or version-checks against. `AppTaskInfo` is an experimental API:
`atv doctor` calls `IsSupported()` at runtime and tells you directly whether
this machine can render task cards right now — that live check is always the
authoritative answer, never a build-number comparison (INFRA-13).
`IsSupported()` can itself report `false` (or throw a
`CLASS_E_CLASSNOTAVAILABLE` `COMException`, which `atv` catches and treats
as unsupported) even on a build that otherwise looks new enough — that's a
real, observed possibility, not a hypothetical.

## Install

```
winget install Agentaskvoid.Atv
```

That's the whole install — a signed MSIX package with the `atv` command
registered on `PATH` (via an `AppExecutionAlias`) once winget finishes. No
separate registration step, no admin script.

*(Distribution note: `Agentaskvoid.Atv` is this project's real, finalized
winget package id — confirm it any time via `Atv.Diagnostics.DoctorChecks.WingetPackageId`
in source, or by reading `atv doctor`'s own remedy line when nothing is
installed. Publishing the package to the public `winget-pkgs` repository is
a deferred ship-time step — see `docs/release.md` §4/§5 — so `winget
install` will start working the moment that submission lands; until then,
build from source per `CLAUDE.md`, or install a locally-built signed MSIX
per `docs/release.md`.)*

## Quickstart

Every lifecycle verb takes a **required** `<handle>` — an id you choose and
keep reusing for the same logical task (a session id, a job id, whatever's
convenient). There is no default/implicit task.

```
atv start build-123 --title "Fixing the build" --subtitle "release branch"
atv step build-123 "Running the test suite"
atv attention build-123 "Should I force-push?"
atv state build-123 running
atv done build-123 --summary "Build fixed, tests green"
atv remove build-123
```

- **`start <handle>`** — create-or-adopt (calling `start` again on a live
  handle re-applies the title/subtitle and resumes `Running` **without**
  losing step history — safe to call every time a session/job (re)starts,
  including on a resumed session with the same id). Flags: `--title`,
  `--subtitle`, `--icon <token>`, `--deep-link <uri>`, `--reset` (force a
  clean slate instead of adopting).
- **`step <handle> <message>`** — advance the card's "what's happening now"
  line; the previous one moves into the completed-steps history (last 10
  kept).
- **`state <handle> running|paused`** — pause/resume without changing steps.
  (`Completed`/`Error`/`NeedsAttention` are their own verbs below, not
  reachable via `state`.)
- **`attention <handle> <question>`** — flag the card as needing you, with
  the question/prompt text. Display-only in v1 — there's no way for `atv`
  itself to carry your answer back to the agent; that's the agent host's own
  job once you respond in its own UI.
- **`done <handle> [--summary TEXT]`** / **`fail <handle> [--summary TEXT]`**
  — mark Completed/Error. Bare keeps the step history visible; `--summary`
  swaps to a one-line result. The card lingers briefly (config: `idle-completed`,
  default ~10 minutes — see [Configuration](docs/configuration.md)) then
  auto-removes itself.
- **`remove <handle>`** — remove one task immediately (task + its sidecar
  record + its icon). This is the right verb for "I'm done with just this
  one," including from your own cleanup scripts.
- **`list [--json]`** — every task under the current install, whether or not
  `atv` on this machine created it (see [Cross-consumer
  guidance](#cross-consumer-guidance-list-and-clear-are-identity-global)
  below).
- **`clear [--include-recycle-bin]`** — remove **every** active task
  immediately, no confirmation prompt. See the same cross-consumer note
  before reaching for this in a script.
- **`run --title T [--icon TOKEN] -- <command...>`** — wrapper mode: launch
  another command, mint your own handle automatically, mirror its output to
  your terminal exactly, and drive a task card from its lifecycle (steps =
  last-lines-of-output, done/fail = its real exit code). The wrapper's own
  exit code is **always** the wrapped command's exit code, regardless of
  `--strict`.
- **`doctor [--json] [--verbose]`** — self-check; see
  [Troubleshooting](#troubleshooting-a-blank-taskbar) below.

Global options, accepted before or after the verb: `--json` (machine-readable
stdout; shape is per-verb), `--strict` (turn on real nonzero exit codes —
without it, `atv` always exits 0 and logs failures instead of surfacing
them, so a misbehaving hook can never break your agent host), `--verbose`
(live diagnostic detail + also log successes, not just failures),
`--watchdog-mode spawn|inproc|off`, `--unsafe` (bypass the built-in
state/content safety validator — experimentation only, see
`src/Atv/Operations/SafeCombinationMatrix.cs`), `--wait-for-debugger`.
Run `atv --help` any time for the full current syntax (the arbiter for the
exact surface is `questions/usage-ergonomics/ERGO-27-consolidated-v1-command-surface.md`
in this repo).

## Wiring up a host

`atv` carries no host-specific logic itself — every agent host gets a
small, ready-made **integration artifact** that translates that host's own
events into the verb set above (`start`/`step`/`state`/`attention`/`done`/
`remove`). Nothing to write yourself.

| Host | Status | Artifact |
|---|---|---|
| **Claude Code** | Supported now | [`integrations/claude-code/`](integrations/claude-code/) — a `settings.json` hooks fragment + install README |
| GitHub Copilot CLI | Planned | Not yet shipped in this build — see the addition criterion below |
| Codex | Planned | Not yet shipped in this build — see the addition criterion below |

Claude Code, Copilot CLI, and Codex are the three hosts targeted for v1
(`questions/lifecycle/LIFE-8-which-agent-hosts-are-in-scope-for-hook-coverage.md`);
Copilot CLI and Codex are being tackled as discrete follow-up passes, not
included in this build. The criterion for adding *any* host (these two or a
future one) is the same: it needs a usable hook/notification surface that
maps onto the verb set above without host-specific branching living inside
`atv` itself, plus enough demand to justify maintaining the artifact.

To wire up Claude Code right now: read
[`integrations/claude-code/README.md`](integrations/claude-code/README.md) —
it covers the exact event mapping, the two-minute install (paste a JSON
block into your Claude Code settings), and what's verified live vs.
verified-against-docs.

## Configuration

Full reference, every tunable, every env var name, precedence rules, and the
config file's location/format: [`docs/configuration.md`](docs/configuration.md).

Short version: `--flag > environment variable > config file > built-in
default`, resolved independently per tunable. Env var names and the config
file name are brand-derived (`ATV_...` / `atv-config.json`) — never
hand-guess them; `atv doctor` prints the exact config file path for the
current install.

## Troubleshooting a blank taskbar

Run `atv doctor` (add `--verbose` for more detail). It always exits 0 and
runs every check to completion regardless of earlier failures, specifically
so it's useful when nothing else works. Read top to bottom:

1. **`identity: <present|absent>`** — if absent, `AppTaskInfo` cannot be
   called at all; `doctor` prints the exact remedy line (`winget install
   Agentaskvoid.Atv`). A dev/test build shows a `(dev)`/`(test)` marker next
   to its identity line so you always know which pool of the tool produced
   the output you're looking at (a real release install is unmarked).
2. **`api: AppTaskInfo.IsSupported() -> true|false`** — if `false` (or the
   line mentions `CLASS_E_CLASSNOTAVAILABLE`), this Windows build doesn't
   have the taskbar-integration side of the API activated. This is a
   platform gap, not something `atv` can work around — no card will ever
   render, no matter how correctly `atv` is called. There is currently no
   known remedy beyond "try a newer Windows build."
3. **Developer Mode** — dev-facing only (loose-layout dev-loop registration).
   Irrelevant once you have a properly `winget`-installed package; only
   matters if you're building `atv` from source.
4. **Paths** (`config file:`, `app-data folder:`, `sidecar dir:`, `log
   file:`) — if a card *should* exist but `atv list` doesn't show it, check
   the sidecar dir and the log file at these paths for a clue (a refused
   write is always logged, even in non-`--strict` mode).
5. **`watchdog: running|not running`** — informational. The watchdog is what
   auto-removes idle-expired cards and recovers from an unclean shutdown; it
   is not required for a card to render in the first place.

If identity is present and the API is supported but a specific card still
isn't showing: run `atv list --json` — if the handle isn't there at all,
the `start` call that should have created it either never ran, or was
refused (check the log). If the handle *is* listed but you don't see it on
the taskbar, that's the boundary between "the platform accepted the write"
and "the Shell rendered it" — see
[`docs/windows-ui-shell-tasks/state-content-compatibility.md`](docs/windows-ui-shell-tasks/state-content-compatibility.md)
for the one place this gap is fully documented (not every accepted
state/content combination renders as intended; `atv` itself only ever emits
combinations from the safe subset, so this is unlikely to be the cause
unless you passed `--unsafe`).

## Cross-consumer guidance: `list` and `clear` are identity-global

Every consumer of one `atv` install (every agent session, every script)
shares one Windows package identity and therefore one task namespace —
`AppTaskInfo` has no per-consumer partitioning. `list` shows *everything*
under the current identity, not just what you created; `clear` removes
*everything*, immediately, with no confirmation prompt (you typed it, it
runs).

For everyday cleanup of your own task, use targeted `remove <handle>` —
it needs no ownership model, since the handle you hold is already scoped to
just your own task. Reach for `clear` only when you deliberately want to
wipe the whole board (e.g. resetting a dev machine's taskbar), and reach for
`clear --include-recycle-bin` even more deliberately (it also forgets every
resurrectable tombstone, not just live cards).

## Also in this repo

- [`CLAUDE.md`](CLAUDE.md) — build/dev-loop instructions, the identity model,
  NativeAOT release build, and every non-obvious implementation decision.
- [`docs/release.md`](docs/release.md) — the signed-MSIX release build +
  supervised install/upgrade/uninstall runbook.
- [`docs/windows-ui-shell-tasks/`](docs/windows-ui-shell-tasks/README.md) —
  a from-experimentation reference for the underlying WinRT API (every
  member, plus the undocumented gotchas: taskbar grouping mechanics, the
  state × content crash matrix, concurrency behavior).
- [`docs/testing/fake-fidelity-promises.md`](docs/testing/fake-fidelity-promises.md)
  — exactly what the test-suite's fake platform does and doesn't promise to
  mimic.
- [`docs/maintenance/new-build-checklist.md`](docs/maintenance/new-build-checklist.md)
  — the short manual re-verification list for a new Windows build (the
  experimental API's "dark matter": undocumented Shell behavior that isn't,
  and can't be, covered by automated tests).
