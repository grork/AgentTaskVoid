# ERGO-27: The consolidated v1 command surface
**Status:** DECIDED
**Plan:** all-phases
**Decision:** The single v1 surface for `atv` (ERGO-18, "The shipped command name") is the spec
below -- one binary, seven lifecycle verbs + four data/util verbs + one hidden mode, a fixed set of
global options, and per-verb flags. Two collisions the union surfaced were ratified 2026-07-05:
per-task `--idle`/`--linger` is DROPPED (idle/linger via env/config only -- amends LIFE-22), and
`run`'s exit code is always the child's (`--strict` never overrides it). Also amends ERGO-16: `clear`
no longer prompts or gates -- an explicit invocation executes immediately (C4). And amends ERGO-6: a
caller-supplied handle is REQUIRED on every lifecycle verb -- no omit-to-default, `start` with no id
is an error (C3). The rest resolved to documented defaults (below). This spec is the input the
execution plan consumes.

**Successor note (updated 2026-07-13):** ERGO-31 ("The v2 semantic verb contract"), spawned
from LIFE-24's ("The host-event → task-state integration semantics") conduit/translator
drill-down, is now **DECIDED and supersedes this surface.** The v2 semantic verbs replace the
lifecycle verbs `start`/`step`/`state`/`attention`/`done`/`fail`; the data/util verbs
(`list`/`run`/`doctor`/`clear`/`remove`), the hidden `watchdog` mode, and all global flags
here carry forward unchanged. This record stays DECIDED (its `**Plan:** all-phases` stamp is
permanent — a v2 contract does not un-plan the shipped v1 phase) as the historical v1 spec;
ERGO-31 is the current surface. See ERGO-31 §5 for the full supersession terms.

---

## Binary
`atv` -- one exe (the watchdog is the same exe in a hidden mode, LIFE-17, "Watchdog spawn
mechanics"). Brand "Agentaskvoid," parameterized through the system (ERGO-18).

## Global options
Accepted anywhere (before or after the verb); per-verb flags come after the verb. All layered
`flags > env > config > default` (ERGO-17, "Configuration surface for recurring defaults"); env/
config names are brand-derived (ERGO-18). Config = JSON in package app-data (ERGO-26, "Config file
location and format").
- `--json` -- machine-readable stdout; exit stays 0 (FAIL-2, "Output and exit-code contract").
  Shape is per-verb (see collision C5).
- `--strict` -- enable the stable exit vocabulary (FAIL-2): `0` ok, `1` generic, `2` API
  unavailable, `3` identity not registered, `4` invalid args / unsafe combo (ERGO-10, "Guarding
  unsupported combinations"). Without `--strict`, always exit 0 (FAIL-1, "Failure posture toward
  the host caller").
- `--verbose` -- live diagnostic detail on stderr; also enables success logging (default is
  failures-only) (FAIL-3, "Diagnosability when nothing shows on the taskbar").
- `--watchdog-mode spawn|inproc|off` -- watchdog hosting mode (INFRA-19, "Inner-loop watchdog
  suppression"); default `spawn`.
- `--unsafe` -- bypass the ERGO-10 safe-combo validator (off by default; experimentation only).
  Meaningful only on content-emitting write verbs.
- `--wait-for-debugger` -- dev/hidden; the spawned child spins at startup until a debugger attaches
  (INFRA-21, "Debugging watchdog mode itself").

## Lifecycle verbs
Each takes a REQUIRED positional `<handle>` (ERGO-6, "The identifier a caller holds"). There is NO
default handle and NO default task -- calling any lifecycle verb without a handle is invalid args
(exit 4 under `--strict`); the tool never creates or addresses a task without a caller-supplied id
(operator, 2026-07-05, amending ERGO-6's earlier omit-to-default affordance). Each write-path verb
ensures a watchdog is live (LIFE-17).

| Verb | Positional | Flags | Effect |
|------|-----------|-------|--------|
| `start <handle>` | -- | `--title`, `--subtitle`, `--icon <token>`, `--deep-link <uri>`, `--reset`, `--unsafe` | Create-or-adopt the card; upsert/idempotent (ERGO-25, "`start` on an already-live handle") -- re-applies title/state, preserves step history; `--reset` forces a clean slate. Sets state=Running, `SequenceOfSteps`. Triggers the ERGO-2 GC sweep (sweep runs on create and remove). Defaults: `--icon` = built-in glyph (ERGO-20, "Icon representation" / ERGO-12, "Defaults for parameters that are secretly required"); `--deep-link` = `file:` URI to app-data (ERGO-24, "The default deepLink URI value"). |
| `step <handle> <message>` | message | `--unsafe` | Advance model (ERGO-8, "Update verbs"): archive prior executing step -> `completedSteps` (FIFO cap 10), set new executing step. No GC sweep (ERGO-19, "Should `update` trigger the GC sweep?"). |
| `state <handle> <running\|paused>` | state enum | `--unsafe` | Set Running or Paused only (Completed/Failed/NeedsAttention go via their own verbs -- see C7). |
| `attention <handle> <question>` | question | `--unsafe` | NeedsAttention + `SetQuestion` on the safe cell (ERGO-9, "Overall command-surface shape" / ERGO-10). Display-only in v1 (no round-trip; INTER-* deferred). |
| `done <handle>` | -- | `--summary <text>`, `--unsafe` | Completed; bare -> `SequenceOfSteps`, `--summary` -> `TextSummaryResult`. Lingers then auto-Remove (LIFE-22, "Idle-period defaults per state"). |
| `fail <handle>` | -- | `--summary <text>`, `--unsafe` | Failed; same shape options as `done`. |
| `remove <handle>` | -- | -- | Remove task + sidecar entry + icon copy (ERGO-21, "The sidecar store design" / ERGO-23, "Clean up of sidecar files"). Triggers the ERGO-2 GC sweep. |

## Data / utility verbs

| Verb | Form | Flags | Effect |
|------|------|-------|--------|
| `list` | `list` | `--json` | Enumerate this identity's tasks (identity-global, ERGO-16, "Ownership and isolation"). |
| `run` | `run --title <t> -- <command...>` | `--title`, `--icon` | Wrapper (ERGO-5, "Providing a wrapper"): launch the command, mint own handle, drive a task from its lifecycle, stream the last-10-lines as steps, self-supervise. Exit = child's exit code (C2). |
| `doctor` | `doctor` | `--json`, `--verbose` | Self-check: IsSupported, identity registration, API availability, the dev-only Developer-Mode check (INFRA-17, "Dogfood/run ergonomics"), and print the config / app-data path (FAIL-3; the surfacing point ERGO-26 flagged). When nothing is installed/registered it prints the one-line `winget install ...` remedy (DIST-4, "Posture for the zero-pre-install script consumer"). |
| `clear` | `clear` | `--include-recycle-bin` | Purge ALL active handles (task + sidecar + icon) immediately -- NO confirmation prompt (an explicit invocation is intent enough). `--include-recycle-bin` also purges the LIFE-21 ("What expiry does") recycle bin. Cross-consumer note: `clear` is identity-global, so everyday cleanup should use targeted `remove <handle>` (ERGO-16, "Ownership and isolation" -- gate dropped, see C4). |

## Hidden / internal

| Verb | Flags | Effect |
|------|-------|--------|
| `watchdog` | `--wait-for-debugger` | Same exe in hidden supervision mode (LIFE-17): runs `WatchdogLoop`, single-instance via the LIFE-18 mutex, self-exits on an empty supervised set (LIFE-19, "Watchdog shutdown conditions"). Not user-facing. |

## Idle / linger
Set via env/config only, per-state (Running ~30m, Paused/NeedsAttention ~4h, Completed ~10m linger;
LIFE-22), layered through ERGO-17. NO per-task flag in v1 (see C1).

## Collisions surfaced by the union
- **C1 (ratified) -- per-task idle has nowhere to persist.** The watchdog is stateless-over-disk
  and reads only tasks.json + the sidecar `{id,lastUpdate,schemaVersion}` (ERGO-21); a per-task
  override that must survive to a COLD task's expiry can't live anywhere cheap. Resolution: DROP
  `--idle`/`--linger`; idle/linger are env/config only. Amends LIFE-22 (per-task override removed);
  sidecar schema untouched.
- **C2 (ratified) -- `run` exit code vs `--strict`.** The child's exit code always wins for `run`
  (ERGO-5 transparency); `--strict`'s vocabulary applies only to atv's own pre-launch failures
  (bad args, can't spawn).
- **C3 -- handle is REQUIRED on every lifecycle verb** (ERGO-6 amended 2026-07-05): no default
  handle, no default task; a missing handle is invalid args. `remove <handle>` removes one task;
  `clear` is the only no-handle bulk removal.
- **C4 (revised 2026-07-05) -- `clear` does NOT prompt.** An explicitly-invoked `clear` executes
  immediately (operator directive: "they invoked it, do it"). The earlier interactive-confirmation /
  `--all` gate is DROPPED -- it added friction to a command the user typed on purpose. Amends ERGO-16
  ("gated"); the cross-consumer-wipe risk ERGO-16 guarded against is now handled by guidance (use
  targeted `remove` for your own tasks), not a gate.
- **C5 -- `--json` shape is per-verb.** `list` -> task array; mutating verbs -> `{"ok":bool,
  "reason":str}` (FAIL-2); `doctor` -> a structured report. One principle (machine output),
  documented shape per verb.
- **C6 -- global-flag placement.** Global options accepted anywhere; per-verb options after the
  verb.
- **C7 -- `state` enum is restricted** to `running|paused`. `state <h> done|completed|failed` is
  invalid args (exit 4 under `--strict`); use the dedicated verbs.

No single record holds the full v1 surface -- it exists only as the union of decisions:
the lifecycle verbs `start`/`step`/`state`/`done`/`fail`/`attention`/`remove` + `list`
(ERGO-9, "Overall command-surface shape for content input"), `run` (ERGO-5, "Providing a
wrapper that runs another script/tool"), `doctor` (FAIL-3, "Diagnosability when nothing
shows on the taskbar"), gated `clear` + `--include-recycle-bin` (ERGO-16, "Ownership and
isolation between consumers"), and the hidden `watchdog` mode (LIFE-17, "Watchdog spawn
mechanics"); plus the cross-cutting flags: `--json`/`--strict` (FAIL-2, "Output and
exit-code contract"), `--verbose` (FAIL-3), `--unsafe` (ERGO-10, "Guarding unsupported
combinations"), `--idle`/`--linger` (LIFE-22, "Idle-period defaults per state"),
`watchdog-mode` (INFRA-19, "Inner-loop watchdog suppression"), `--wait-for-debugger`
(INFRA-21, "Debugging watchdog mode itself"); plus the ERGO-12/ERGO-20 defaults (icon
token, deepLink -- pending ERGO-24, "The default deepLink URI value") and ERGO-25's
("`start` on an already-live handle") outcome.

Produce the single consolidated spec: every verb with its arguments, flags, and defaults;
which flags are global vs per-verb; and any collisions the union surfaces (they land
here). Operator direction (2026-07-05): answer this BEFORE the execution plan is built --
the plan consumes it.

Surfaced by the 2026-07-05 review pass.
