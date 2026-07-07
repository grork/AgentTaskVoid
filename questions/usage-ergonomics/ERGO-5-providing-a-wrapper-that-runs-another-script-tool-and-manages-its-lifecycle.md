# ERGO-5: Providing a wrapper that runs another script/tool and manages its lifecycle
**Status:** DECIDED
**Decision:** Yes -- v1 includes a minimal `run` verb:
`atv run --title "Build" -- <command...>`. It launches the command, drives a task
from its lifecycle, and stays transparent (child stdout/stderr still stream to the
terminal; the wrapper exits with the child's exit code). Output is reflected
mechanically, never interpreted; scope is hard-bounded below.

Should we provide a 'wrapper' around scripts/other command line tools? E.g.
invoke the other script/tool, and read the stdout/stderr, and manage the
lifecycle of the AppTaskInfo through those states -- putting lines into the
AppTaskContent sequence?

Decision detail (2026-07-02):

Lifecycle mapping:
- launch -> `start` (title = --title, else the command line); the wrapper mints its
  own handle (wrapper mode owns the id, ERGO-6).
- output -> a `step` stream (below).
- exit 0 -> `done`; nonzero -> `fail`. The finished task lingers as a normal
  completed card (LIFE-7 expiry). The wrapper self-supervises; if it is SIGKILL'd,
  the stale sidecar `lastUpdate` lets the watchdog sweep the orphan (LIFE-5).

Correctness structure (not polish):
- Decoupled reader/updater: drain + mirror child output at full speed on one path;
  push task updates on a debounced timer (~10/sec, a build-phase tuning value) on
  another -- so a chatty child never blocks on a full stdout buffer (which would
  slow the wrapped command) and a 10k-line build never becomes 10k API writes.
- Stream lines as steps: keep a 10-line in-memory rolling buffer; each tick write
  the whole buffer as the step list (no read-back -- the wrapper owns the task
  exclusively). Shows "the last 10 lines as of now", refreshing ~10x/sec.
- Signal handling: Ctrl+C forwards to the child, waits for exit, marks fail/done,
  cleans up -- never orphans the child or leaves a stuck Running card.

Output hygiene (fixed, bounded -- the explicit anti-creep boundary):
Principle: turn raw bytes into a short readable string; NEVER interpret meaning,
NEVER emulate a terminal; applies only to the step copy (terminal passthrough is
byte-for-byte untouched). A fixed per-line pipeline, no config/regex knobs:
1. strip ANSI/VT escape sequences (one regex);
2. collapse `\r` overwrites (keep text after the last `\r` in a physical line, so a
   progress bar becomes its final value; trailing-remnant imperfection accepted);
3. scrub remaining control chars (tabs->space; drop bell/backspace/null/form-feed);
4. trim whitespace;
5. drop blank lines (no wasted step slot);
6. truncate to a max length + ellipsis (max = build-phase tuning value).

Explicitly OUT (each would need its own decision):
- any semantic interpretation (progress %, phase/error detection, mapping output to
  Paused/NeedsAttention);
- terminal-grid / multi-line-redraw emulation (cursor-addressing TUIs, Docker-layer
  / cargo multi-line progress) -- the same boundary as interactive/TTY out of scope;
- configurable filters (deferred; ERGO-11);
- encoding negotiation beyond UTF-8 / console-default with replacement;
- binary-output detection.

Targets line-oriented batch commands (builds, tests, installers, migrations,
backups) -- the class where a background taskbar status is most wanted. Minimal in
surface (one verb, no interpretation) but carries the real reader/updater +
signal-handling "mini-supervisor" structure.
