# Phase 06: Configuration, output contract, durable log

**Depends on:** phase 01 (brand constant, app-data paths), phase 02 (test suite)
**Unblocks:** phase 08 (every verb speaks through this layer)

## Goal

Build the cross-cutting plumbing every verb uses: layered configuration, the
non-disruptive failure posture, the stdout/stderr/exit-code contract, `--json`
output shapes, and the always-on durable failure log.

## Decisions implemented

### Configuration (ERGO-17, "Configuration surface for recurring defaults"; ERGO-26)

- Precedence: `flags > env var > config file > built-in default`.
- Config file: JSON via `System.Text.Json` with SOURCE-GENERATED serialization
  (AOT/trim-safe, in-box, ~zero size — TOML rejected: not in-box). Lives in package
  app-data (`ApplicationData.Current.LocalFolder`) → automatic per-pool isolation
  (release / dev / each test worktree see different config; a dev's config can never
  perturb a test run). Accepted trade-offs: edit-hostile path, vanishes on uninstall.
- Env var and file/dir names derive from the brand constant (e.g. `ATV_*` if the
  brand yields `atv` — derive, don't hardcode).
- Because the path is non-obvious, `doctor` (phase 10) prints it — expose a helper
  here that returns the config path.
- Tunables this system carries (built-in defaults; consumers in later phases):
  - `watchdog-mode`: `spawn` | `inproc` | `off` — CODE default `spawn` (INFRA-19;
    suppression is always explicit via env/profile, never debugger-sniffing).
  - Per-state idle periods (LIFE-22): Running ~30 min; Paused ~4 h;
    NeedsAttention ~4 h (same value for now); Completed/Failed linger ~10 min.
    Env/config ONLY — NO per-task flag (ERGO-27 C1: a per-task value has nowhere to
    persist for the stateless-over-disk watchdog).
  - Recycle-bin TTL (~1 day), mutex bounded-wait budget, watchdog poll interval,
    log rotation size/age, `run` update debounce + step max length + silent-child
    keepalive interval.

### Failure posture & output contract (FAIL-1, FAIL-2)

- Default: on ANY failure the CLI no-ops and exits 0 — it can never break a host
  hook. A durable failure log entry is ALWAYS written on the silent path (hard
  requirement of FAIL-1, not optional).
- `--strict`: errors to stderr + the stable exit vocabulary — `0` ok, `1` generic,
  `2` API unavailable, `3` identity not registered, `4` invalid args / unsafe combo.
- stdout = data, stderr = diagnostics. Mutating verbs print NOTHING on the happy
  path (no id returned — the caller owns the handle, ERGO-6).
- `--json` (exit stays 0): machine-readable stdout, shape per verb (ERGO-27 C5) —
  mutating verbs → `{"ok":bool,"reason":str}` so scripts learn the truth without
  leaving non-disruptive mode; `list` → task array; `doctor` → structured report.
- `run` exception (ERGO-27 C2): the child's exit code always wins; `--strict`
  applies only to atv's own pre-launch failures.

### Durable log & verbosity (FAIL-3, "Diagnosability when nothing shows on the taskbar")

- Log lives in package app-data (same container as tasks.json/sidecar), never a
  hand-rolled global path. Entries `{timestamp, verb, handle, error}`.
- FAILURES logged by default; success not logged by default. `--verbose` enables
  live diagnostic detail on stderr AND success logging (minimal).
- Size/age rotation (exact thresholds are tunables — pick sane defaults, e.g.
  1 MB / 14 days).

### Capability detection (INFRA-13, "Windows build compatibility strategy")

- Runtime detection is authoritative: `IsSupported()` + identity presence, wrapped
  for `CLASS_E_CLASSNOTAVAILABLE` (adapter already wraps, phase 02). Map the
  outcomes onto the FAIL-2 exit vocabulary (2 = API unavailable, 3 = identity not
  registered) and the non-disruptive path. No version pinning anywhere.

## Files affected

```
src/Atv/Config/Settings.cs            # typed settings + built-in defaults
src/Atv/Config/SettingsLoader.cs      # flags > env > file > default resolution
src/Atv/Config/SettingsJsonContext.cs # STJ source-gen context
src/Atv/Diagnostics/FailureLog.cs     # durable log + rotation
src/Atv/Diagnostics/Posture.cs        # run-verb-non-disruptively wrapper; strict/json/exit-code mapping
src/Atv/Diagnostics/Output.cs         # stdout/stderr discipline, per-verb json shapes
tests/Atv.LogicTests/Config/*         # precedence matrix tests
tests/Atv.LogicTests/Diagnostics/*    # posture, exit codes, log rotation
```

## Acceptance criteria (written first)

1. Precedence: data-driven tests prove flag beats env beats file beats default for a
   representative setting of each type; brand-derived env names verified against the
   brand constant (rename the brand in a test → names follow).
2. Posture: an injected failing operation under default mode → exit 0, nothing on
   stdout, one failure-log entry; under `--strict` → correct exit code (2/3/4/1
   mapped from failure class) + stderr message; under `--json` → exit 0 +
   `{"ok":false,"reason":…}`.
3. Log: rotation triggers on size and age; entries carry
   {timestamp, verb, handle, error}; log writes never throw (a failing log write is
   swallowed — logging must not violate FAIL-1).
4. Config file absent / malformed → built-in defaults + a logged failure, never a
   crash (non-disruptive).
5. AOT publish still clean (source-gen JSON only; no reflection serializers).

## Out of scope

`doctor` itself (phase 10), the settings' consumers (watchdog phase 09, run
phase 11).
