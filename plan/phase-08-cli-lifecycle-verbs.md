# Phase 08: CLI framework + the seven lifecycle verbs

**Depends on:** phases 04 (persistence), 05 (operations), 06 (config/posture/output), 07 (icons)
**Unblocks:** phases 09, 10, 11

## Goal

Ship the user-facing command line: argument parsing, global options, and the seven
lifecycle verbs wired end to end through the operation core — so a hook one-liner
drives a real taskbar card. The contract is ERGO-27 ("The consolidated v1 command
surface"); this file inlines everything needed.

## Decisions implemented

### Framework

- One binary `atv` (the watchdog is the same exe in a hidden mode — the verb is
  registered here, its behavior lands in phase 09).
- Parsing: choose an AOT-safe approach (a trim/AOT-clean library or hand-rolled —
  binary-size budget per INFRA-2 is the constraint; no reflection-driven binder).
- Global options accepted anywhere (before or after the verb); per-verb flags after
  the verb (ERGO-27 C6). Global set: `--json`, `--strict`, `--verbose`,
  `--watchdog-mode spawn|inproc|off`, `--unsafe`, `--wait-for-debugger`
  (dev/hidden). All layered flags > env > config > default via phase 06.
- Every lifecycle verb takes a REQUIRED positional `<handle>` (ERGO-6 as amended by
  ERGO-27 C3): no default handle, no default task; missing handle = invalid args
  (exit 4 under `--strict`). The handle is an opaque caller-supplied string (agent
  session ids, script constants). No PID/cwd auto-derivation.
- Every write-path verb: (1) resolves settings; (2) ensures a watchdog is live via
  the mode-gated `EnsureWatchdog` hook — in THIS phase the gate + `OpenMutex`
  liveness check exist with hosts stubbed/no-op; phase 09 supplies the real hosts;
  (3) executes the phase 05 operation under the posture wrapper.
- Help surface lives HERE: bare `atv` (no verb) and `--help` print usage;
  `--version` prints the NBGV version. Verb help text carries the `clear`
  cross-consumer guidance (phase 10 supplies the wording).

### The verbs (ERGO-27 table, normative)

| Verb | Flags | Effect |
|------|-------|--------|
| `start <handle>` | `--title`, `--subtitle`, `--icon <token>`, `--deep-link <uri>`, `--reset`, `--unsafe` | Create-or-adopt (upsert, ERGO-25): re-applies title/subtitle/state, preserves steps; `--reset` = clean slate. Sets state=Running, `SequenceOfSteps`. Triggers the ERGO-2 user-hidden GC sweep. |
| `step <handle> <message>` | `--unsafe` | Advance model (ERGO-8): archive executing → completedSteps (FIFO 10), set new executing. NO GC sweep (ERGO-19 — hot path stays lean). |
| `state <handle> <running\|paused>` | `--unsafe` | Running/Paused only (C7); anything else = invalid args. |
| `attention <handle> <question>` | `--unsafe` | NeedsAttention + `SetQuestion` (safe cell only). Display-only in v1. |
| `done <handle>` | `--summary <text>`, `--unsafe` | Completed; bare → SequenceOfSteps, `--summary` → TextSummaryResult. Lingers, then the watchdog removes (LIFE-22). |
| `fail <handle>` | `--summary <text>`, `--unsafe` | Failed; same shapes as `done`. |
| `remove <handle>` | — | Remove task + sidecar entry + icon copy. Triggers the ERGO-2 sweep. |

### Defaults for secretly-required parameters (ERGO-12, ERGO-20, ERGO-24)

- `iconUri`: never null — no `--icon` means the built-in default glyph rendered to
  the PER-HANDLE path (separation-by-session preserved even on defaults).
- `deepLink`: never null — default is a `file:` URI to the tool's app-data folder
  (`ApplicationData.Current.LocalFolder`), computed at runtime, never hardcoded.
  Empirically opens Explorer cleanly on click (no Store prompt, no flash) and lands
  the user on the diagnostics folder (log/config/sidecar). Callers override
  per-invocation with `--deep-link`.

### Sweeps and miss path

- ERGO-2: on `start` (create) and `remove`, sweep `HiddenByUser` tasks —
  `Remove()` + drop entry (already part of phase 04 reconciliation; wire the
  trigger points). ERGO-19: never on `step`.
- Recycle-bin resurrection (phase 05) is live on every update-class verb; the
  opportunistic recycle-bin TTL scavenge (phase 04 helper) is folded into the same
  sweep points as ERGO-2 (start/remove), not the hot path.

### Output

- Happy-path mutating verbs print nothing; `--json` prints `{"ok":…,"reason":…}`;
  failures follow FAIL-1/FAIL-2 via the phase 06 posture wrapper. All behavior
  identical whether the failure is API-absent, identity-missing, validator-refused,
  or unknown-handle — only the logged reason and strict exit code differ.

## Files affected

```
src/Atv/Cli/CommandLine.cs           # parser + global option handling
src/Atv/Cli/Verbs/StartVerb.cs … RemoveVerb.cs   (or one dispatcher file — match codebase style as it emerges)
src/Atv/Cli/CompositionRoot.cs       # builds store/mutex/paths/settings/log; the ONLY place prod instances are created
src/Atv/Program.cs                   # thin main → CompositionRoot → dispatcher; POC probe code deleted
tests/Atv.LogicTests/Cli/*           # verb-level tests against fake store + temp dirs
```

## Acceptance criteria (written first)

1. Verb-level tests (fake-backed, full pipeline minus the real adapter): each verb's
   documented effect from the table above, including sweep-on-start/remove,
   no-sweep-on-step, defaults applied (icon per-handle path; deepLink file: URI),
   required-handle enforcement (exit 4 strict / silent 0 default), C7 state
   restriction, `--json` shapes, `--unsafe` pass-through.
2. Global options parse in any position; per-verb flags only after the verb.
3. A missing/failing platform (fake configured unavailable / adapter absent) leaves
   every verb exit-0-silent by default with a log entry — the "hook can never break
   the host" guarantee, tested.
4. Manual dogfood on this machine (real API): `atv start s1 --title "Demo"`,
   `atv step s1 "Working"`, `atv attention s1 "Continue?"`, `atv done s1 --summary
   "Done"`, `atv remove s1` — card appears, updates, shows the question, completes,
   disappears; a second handle renders as a SECOND taskbar icon.
5. Real-adapter suite (phase 03) extended with ≥1 end-to-end test per verb
   (INFRA-9's "≥1 per verb" now becomes meaningful at the verb level).
6. AOT publish clean; startup cost sane (no perceptible lag on `atv step` — informal;
   the formal latency budget is deferred INFRA-12).

## Out of scope

`list`/`clear`/`doctor` (phase 10), `run` (phase 11), real watchdog hosts
(phase 09 — this phase leaves `EnsureWatchdog` gated but inert).
