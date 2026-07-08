# Phase 10: Utility verbs — `list`, `clear`, `doctor`

**Depends on:** phase 08 (CLI framework); consumes phases 04/06/07. Can run in
parallel with phase 09 (no watchdog dependency).
**Unblocks:** phase 12 (doctor is part of release verification), phase 13 (docs)

## Goal

Complete the data/utility half of the ERGO-27 surface: enumeration, bulk purge, and
the self-diagnosis verb that answers "why is nothing on my taskbar?".

## Decisions implemented

### `list` (ERGO-27; ERGO-16)

- `list [--json]` — enumerate this identity's tasks. Identity-global by design (one
  identity = one pool the API can't partition; there is no ownership layer in v1).
- Human-readable default; `--json` → task array (handle where known via the sidecar,
  title, state, executing step, lastUpdate). stdout = data (FAIL-2).

### `clear` (ERGO-16 as amended by ERGO-27 C4; requirements.md)

- `clear [--include-recycle-bin]` — purge ALL active handles: every task
  (`Remove()`), its sidecar entry, and its icon copy. Executes IMMEDIATELY — no
  confirmation prompt, no `--all` gate (operator: "they invoked it, do it";
  explicit-intent-over-magic). Cross-consumer safety is guidance (use targeted
  `remove <handle>`), not a gate — documented in help text + docs.
- Default scope EXCLUDES the LIFE-21 recycle bin; `--include-recycle-bin` extends
  the purge to it (records + co-located icons). (The LIFE-20 boot-recovery clear is
  the internal exception that always wipes the recycle bin — phase 09.)
- Clears all internal cross-invocation tracking state (requirements.md): after
  `clear`, the sidecar dir has no entries, no per-handle icons remain, and (with the
  flag) the recycle bin is empty. The canonical render cache MAY remain (pure
  accelerator, ERGO-23).
- Runs under the write mutex; whole operation is the standard reconcile-then-act
  cycle.

### `doctor` (FAIL-3; INFRA-13; INFRA-17; DIST-4; ERGO-26)

- `doctor [--json] [--verbose]` — self-check reporting, at minimum:
  1. Package identity present? (`GetCurrentPackageFullName`) — with the PFN when
     present.
  2. API availability: `IsSupported()` wrapped for the `CLASS_E_CLASSNOTAVAILABLE`
     COMException (runtime detection is authoritative; the documented Win11 26100+
     floor is expectation-setting only).
  3. Dev-only: Developer Mode enabled (relevant for loose-layout dev/test loops;
     label it dev-facing).
  4. Paths: print the config file path and the app-data folder (log, sidecar,
     tasks.json) — the surfacing point ERGO-26 requires because the
     `%LOCALAPPDATA%\Packages\<PFN>\…` path is non-obvious.
  5. Watchdog liveness (OpenMutex on the LIFE-18 name) — informational.
  6. When nothing is installed/registered: print the one-line remedy
     `winget install <package-id>` (DIST-4 — no silent self-install path exists,
     ever). Use the phase 12 package id; a placeholder until then.
- `--json` → structured report (ERGO-27 C5). `doctor` is diagnostic: it always runs
  to completion and exits 0 unless `--strict` (then the worst finding maps onto the
  FAIL-2 vocabulary).

## Files affected

```
src/Atv/Cli/Verbs/ListVerb.cs
src/Atv/Cli/Verbs/ClearVerb.cs
src/Atv/Cli/Verbs/DoctorVerb.cs
src/Atv/Diagnostics/DoctorChecks.cs    # individual checks, unit-testable with injected probes
tests/Atv.LogicTests/Cli/ListVerbTests.cs / ClearVerbTests.cs / DoctorTests.cs
```

## Acceptance criteria (written first)

1. `list`: fake-backed tests for empty, multiple tasks, and `--json` array shape;
   entries correlate handle↔task via the sidecar; tasks without entries (entryless)
   still listed (identity-global truth).
2. `clear`: purges every task + sidecar entry + per-handle icon; recycle bin
   untouched by default and emptied with `--include-recycle-bin`; no prompt; a
   second `clear` is a clean no-op; render cache survives.
3. `doctor`: each check unit-tested via injected probes (identity absent/present,
   API absent/present, dev-mode on/off); the not-installed path emits the winget
   remedy line; `--json` shape stable; paths printed match `AppPaths`.
4. Manual: run `doctor` on this machine → all green + correct paths; run `clear`
   with live cards → taskbar empties immediately.
5. Real-adapter suite: one end-to-end `list` and `clear` test.

## Out of scope

`run` (phase 11); winget package id finalization (phase 12 feeds the doctor remedy
string).
