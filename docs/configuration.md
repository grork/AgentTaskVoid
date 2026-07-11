# Configuration reference

`atv` resolves every tunable through one precedence chain, per-tunable
(ERGO-17):

```
--flag  >  environment variable  >  config file  >  built-in default
```

A value that fails to parse at one layer is treated as *absent* at that
layer — the next lower layer is tried, never a hard failure (one bad setting
never poisons the rest of the file, or the run). `--verbose` surfaces any
such fallback as a warning; the tool never crashes on a malformed config.

## Config file location and format (ERGO-26)

- **Location:** package app-data
  (`ApplicationData.Current.LocalFolder`) — `atv-config.json` next to the
  sidecar index, recycle bin, icon cache, and log file. The path is
  non-obvious by design (it's per-package-identity, so dev/test/release each
  get their own, isolated file with zero manual pinning) — run `atv doctor`
  to print the exact resolved path (`config file: ...`) rather than guessing
  it.
- **Format:** flat JSON, string keys to string values (no nested objects).
  Every tunable below uses its **bare key name** (e.g. `"watchdog-mode"`,
  not `"WatchdogMode"`) as the JSON property name:

  ```json
  {
    "idle-completed": "00:15:00",
    "watchdog-mode": "spawn"
  }
  ```
- Environment variable names are the same bare key, brand-derived and
  upper-cased with dashes turned to underscores:
  `ATV_<KEY_WITH_UNDERSCORES>` (e.g. `idle-completed` →
  `ATV_IDLE_COMPLETED`). A rebrand (`src/Atv/Branding.cs`) changes the prefix
  automatically — never hardcode `ATV_` yourself in a script that's meant to
  survive a rebrand; call `atv doctor` or read this doc fresh instead.
- The config file is **optional** — it vanishes cleanly on uninstall
  (package app-data is removed with the package), and a missing file is
  normal, not a warning.

## Every tunable

`TimeSpan` values use .NET's standard `TimeSpan.Parse` format
(`"hh:mm:ss"`, e.g. `"00:30:00"` for 30 minutes; also accepts
`"d.hh:mm:ss"` for values over 24h).

| Key (config file) | Env var | Type | Default | What it controls |
|---|---|---|---|---|
| `watchdog-mode` | `ATV_WATCHDOG_MODE` | `spawn`\|`inproc`\|`off` | `spawn` | Watchdog hosting mode (also settable per-invocation via the global `--watchdog-mode` flag). `spawn` = real detached supervisor process (production). `inproc` = background thread on the invoking process (dev/debug only — dies with the process). `off` = no supervision at all. |
| `idle-running` | `ATV_IDLE_RUNNING` | TimeSpan | `00:30:00` (30m) | How long a `Running` card may go without an update before the watchdog reaps it. Sized to outlast a long single tool call; `run`'s wrapper self-heartbeats so it never hits this on its own. |
| `idle-paused` | `ATV_IDLE_PAUSED` | TimeSpan | `04:00:00` (4h) | Idle budget for a `Paused` card (a held session / an away user). |
| `idle-needs-attention` | `ATV_IDLE_NEEDS_ATTENTION` | TimeSpan | `04:00:00` (4h) | Idle budget for a card waiting on a human response. |
| `idle-completed` | `ATV_IDLE_COMPLETED` | TimeSpan | `00:10:00` (10m) | How long a `Completed`/`Error` card lingers before auto-`Remove()`. Applies to both `done` and `fail`. |
| `recycle-bin-ttl` | `ATV_RECYCLE_BIN_TTL` | TimeSpan | `1.00:00:00` (1 day) | How long a removed/expired handle stays resurrectable (ERGO-25) before the recycle bin permanently forgets it. |
| `mutex-wait-budget` | `ATV_MUTEX_WAIT_BUDGET` | TimeSpan | `00:00:02` (2s) | Bounded wait for the cross-process write mutex (INFRA-6) before a write is skipped non-disruptively. |
| `watchdog-poll-interval` | `ATV_WATCHDOG_POLL_INTERVAL` | TimeSpan | `00:00:30` (30s) | How often the watchdog wakes to re-check idle deadlines. |
| `log-max-bytes` | `ATV_LOG_MAX_BYTES` | integer (bytes) | `1048576` (1 MiB) | Durable failure-log rotation size threshold. |
| `log-max-age` | `ATV_LOG_MAX_AGE` | TimeSpan | `14.00:00:00` (14 days) | Durable failure-log rotation age threshold. |
| `run-update-debounce` | `ATV_RUN_UPDATE_DEBOUNCE` | TimeSpan | `00:00:02` (2s) | How often `run`'s wrapper coalesces child output into a `step` update (a burst of output between ticks becomes one update, not one per line). |
| `run-step-max-length` | `ATV_RUN_STEP_MAX_LENGTH` | integer (chars) | `200` | Max length of one step line `run` streams before truncation with an ellipsis. |
| `run-keepalive-interval` | `ATV_RUN_KEEPALIVE_INTERVAL` | TimeSpan | `00:05:00` (5m) | How often `run` touches the sidecar's `lastUpdate` for an otherwise-silent child, so the watchdog never reaps a quiet-but-alive wrapped process. |

There is deliberately **no per-task override** for idle/linger (e.g. no
`--idle`/`--linger` flag) — ERGO-27 dropped it (a per-task value has nowhere
cheap to persist for a cold/unwatched task). Idle periods are always
env/config, applying identity-wide.

## Per-host recurring defaults (ERGO-17)

A shipped host integration (e.g. `integrations/claude-code/`) passes its own
recurring values — icon token, title text — directly as `--flag`s on each
`atv` call baked into the hook config, not through this config file. The
config file is for *your own* machine-wide overrides (idle periods, watchdog
mode); per-host presentation choices live in the integration artifact itself,
editable by copying and adjusting that artifact's hook commands.

## Diagnosing configuration problems

`atv doctor [--verbose]` prints the resolved `config file:`, `app-data
folder:`, `sidecar dir:`, and `log file:` paths for the *current* identity
(dev build vs. release vs. test worktree each resolve to a different path —
see the `(dev)`/`(test)` marker on `doctor`'s identity line). If a config
value silently didn't apply, check the durable failure log at the printed
`log file:` path — every per-tunable fallback ("ignoring invalid env value
for 'X'; trying the next-lower-precedence source") is written there
unconditionally at startup, whether or not `--verbose` was passed (v1 does
not additionally echo these live to stderr — the log file is the source of
truth for a mis-set tunable).
