# Phase 11: The `run` wrapper verb

**Depends on:** phase 08 (operation core + CLI), phase 09 (supervision backstop)
**Unblocks:** phase 12/13 (docs + release include it)

## Goal

Ship `atv run --title <t> [--icon <token>] -- <command…>`: launch a command, drive a
task card from its lifecycle, stream its output as steps, stay completely transparent
to the terminal and the caller's exit-code handling. Targets line-oriented batch
commands (builds, tests, installers, migrations).

## Decisions implemented (ERGO-5, "Providing a wrapper"; ERGO-27 C2)

### Lifecycle mapping

- Launch → `start` (title = `--title`, else the command line). The wrapper MINTS ITS
  OWN handle (the one exception to caller-supplied handles — the CLI owns id
  generation here; make it unique per run).
- Output → a `step` stream (below).
- Exit 0 → `done`; nonzero → `fail`. The finished card lingers as a normal completed
  card (LIFE-22).
- The wrapper self-supervises (it is a live process stamping `lastUpdate` on every
  update). Silent-child keepalive (ratified 2026-07-07): when no output has arrived,
  the wrapper still refreshes the handle's sidecar `lastUpdate` on a sparse interval
  (config tunable, well below the Running idle period; no content write needed) so
  the watchdog never reaps a card whose child is alive-but-quiet — the LIFE-22
  "wrapper self-heartbeats" assumption made real. If the wrapper is SIGKILL'd, the
  stale sidecar `lastUpdate` lets the watchdog sweep the orphan — no special
  mechanism.
- **Exit code = the child's exit code, always** (ERGO-27 C2). `--strict`'s
  vocabulary applies only to atv's own pre-launch failures (bad args, can't spawn).

### Correctness structure (not polish)

- **Decoupled reader/updater**: drain + mirror child stdout/stderr to the terminal
  at full speed on one path (byte-for-byte untouched — transparency); push task
  updates on a debounced timer (~10/sec, config tunable) on another. A chatty child
  must never block on a full pipe buffer; a 10k-line build must never become 10k
  API writes.
- **Steps**: keep a 10-line in-memory rolling buffer; each tick write the WHOLE
  buffer as the step list (no read-back — the wrapper owns the task exclusively).
  Renders "the last 10 lines as of now".
- **Signal handling**: Ctrl+C forwards to the child, waits for exit, marks
  fail/done per the exit code, cleans up — never orphans the child, never leaves a
  stuck Running card.

### Output hygiene (fixed, bounded — the explicit anti-creep boundary)

Applies ONLY to the step copy. Principle: turn raw bytes into a short readable
string; NEVER interpret meaning, NEVER emulate a terminal. Fixed per-line pipeline,
no config/regex knobs:

1. strip ANSI/VT escape sequences (one regex);
2. collapse `\r` overwrites (keep text after the last `\r` — a progress bar becomes
   its final value; trailing-remnant imperfection accepted);
3. scrub remaining control chars (tabs→space; drop bell/backspace/null/form-feed);
4. trim whitespace;
5. drop blank lines;
6. truncate to a max length + ellipsis (config tunable).

Explicitly OUT (each would need its own decision — do not add): semantic
interpretation (progress %, phase/error detection, output→Paused/NeedsAttention),
terminal-grid/multi-line-redraw emulation (TUIs, docker/cargo progress),
configurable filters, encoding negotiation beyond UTF-8/console-default with
replacement, binary-output detection.

## Files affected

```
src/Atv/Cli/Verbs/RunVerb.cs
src/Atv/Run/ChildProcess.cs         # spawn, signal forwarding, exit-code capture
src/Atv/Run/OutputPump.cs           # reader (mirror) + rolling buffer
src/Atv/Run/StepPublisher.cs        # debounced updater over the phase 05 operations
src/Atv/Run/LineHygiene.cs          # the 6-step pipeline
tests/Atv.LogicTests/Run/*
```

## Acceptance criteria (written first)

1. `LineHygiene`: table-driven unit tests — ANSI-heavy lines, `\r` progress bars,
   control chars, blanks, over-long lines → expected cleaned output.
2. Lifecycle (fake-backed, scripted fake child or injected streams): launch →
   start; output ticks produce ≤10-step whole-buffer writes at the debounce cadence
   (a burst of 100 lines in one tick = ONE update); exit 0 → done, exit N → fail
   and the wrapper's own exit code is N; `--strict` does not alter a child's exit
   code. A silent stretch past the keepalive interval refreshes `lastUpdate` with
   no step write.
3. Transparency: child stdout/stderr bytes reach the terminal unmodified and
   unreordered per stream (integration test with a real child process emitting
   known sequences on both streams).
4. Ctrl+C integration test: signal during a long-running child forwards, child
   exits, card marked, no orphan process, no Running card left.
5. Manual dogfood: `atv run --title "Build" -- dotnet build` on this repo shows a
   live card with scrolling last-lines, completing to done; a failing command shows
   fail with the same transparency.

## Out of scope

Anything on the "explicitly OUT" list above. Interactive/TTY children.
