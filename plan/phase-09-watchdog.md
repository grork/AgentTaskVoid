# Phase 09: Watchdog — supervision, expiry, boot recovery, dev-loop hygiene

**Depends on:** phases 04 (persistence), 05 (operations), 06 (config), 08 (verb
integration points), 03 (adapter suite hosts the required integration test)
**Unblocks:** phase 11 (`run` relies on supervision), phase 12

## Goal

Implement the per-identity background watchdog that expires idle tasks after unclean
session death (the product's core cleanup guarantee — no agent host emits a reliable
session-end signal), plus boot recovery and the dev-loop hygiene that keeps the
watchdog from being a nuisance during development.

## Decisions implemented

### Architecture (INFRA-21, "Debugging watchdog mode itself"; LIFE-16)

One shared logic core behind a thin hosting seam — spawned-process, in-proc-thread,
and test hosts run IDENTICAL logic:

- **`WatchdogLoop`** — the supervision logic. Stateless-over-disk (LIFE-16): one
  watchdog per package identity, each tick re-derives ALL liveness state fresh
  (per-handle `lastUpdate` from the sidecar dir + task state/`HiddenByUser` via the
  store's `FindAll()` — the watchdog observes through the `IAppTaskStore` seam like
  all other logic; NO production raw `tasks.json` reader, ratified 2026-07-07 — raw
  reading stays adapter-suite test tooling, phase 03); no
  in-memory timer state, so a respawn reconstructs everything. POLL only, no
  filesystem watcher (idle expiry is an absence detector; there is no FS event for
  "nothing happened"). Poll interval = config tunable (default ~30 s).
- **`EnsureWatchdog`** — decide-to-run: resolve `watchdog-mode` (INFRA-19), then a
  cheap `OpenMutex` liveness check IN THE INVOKER (so a busy session's `step` stream
  never spawns doomed processes), then spawn/in-proc/nothing. The spawned watchdog's
  acquire-or-exit is the correctness backstop for the check→spawn race.
- **`IWatchdogHost`** — only the ACT of hosting differs: `ProcessHost` (detached
  process), `InProcThreadHost` (background thread, dies with the invoking process —
  a dev/debug hosting mode, NOT production supervision), `FakeHost` (tests).
- Modes (INFRA-19): `spawn` (prod default) | `inproc` | `off` — explicit config
  only, no debugger sniffing. The default launch profile's `WATCHDOG_MODE=off`
  (phase 01) covers F5/Ctrl+F5/`dotnet run`.

### Spawn & single-instance (LIFE-17, LIFE-18)

- Process = the same `atv` exe in the hidden `watchdog` verb, spawned windowless
  (`CreateNoWindow`) and detached (new process group; survives parent exit). Spawn
  failure is non-disruptive: log + continue.
- Single instance via named mutex `Local\<brand>-<PFN>-watchdog`, held for the
  watchdog's lifetime; the LIFE-18 acquire-or-exit lives in the SHARED loop (so
  inproc and spawn behave identically); a startup-race loser exits cleanly.
- `--wait-for-debugger`: the spawned child spins at startup until a debugger
  attaches (opt-in dev flag).

### Tick behavior (LIFE-19, LIFE-21, LIFE-22, LIFE-23, requirements.md)

Each tick, under the shared INFRA-6 write mutex where it mutates:

1. **Reconcile/active work first, THEN expiry** (requirements.md ordering): the
   expiry pass re-reads each handle's `lastUpdate` under the mutex IMMEDIATELY
   before comparing to now — never from an earlier snapshot. Age = wall-clock
   `lastUpdate` vs now (correctly counts sleep time); never a monotonic/paused timer
   or pre-sleep cached deadline. This closes the sleep/wake reap-fresh-task window.
2. **Idle expiry per state** (LIFE-22, config-driven defaults): Running ~30 min
   (outlasts the longest realistic single tool call); Paused ~4 h; NeedsAttention
   ~4 h — deliberately longer than Running so a card waiting on an away user is not
   reaped quickly, but it DOES expire at the threshold (and nothing resurrects it);
   Completed/Failed ~10 min linger then remove.
3. **What expiry does** (LIFE-21): `Remove()` the card (no visible tombstone) AND
   write the recycle-bin record `{handle, title, subtitle, icon-ref, deepLink,
   whenTombstoned}` — values READ OFF THE API CARD at expiry time (the sidecar
   caches no content) — moving the icon copy into the recycle folder (phase 07 move
   op). The watchdog performs the `Remove()` itself under the shared mutex — never
   deferred to "the next CLI invocation" (there may be none).
4. **User-hidden sweep**: `HiddenByUser` tasks → `Remove()` + drop entry (catches
   hides during long sessions; ERGO-19 keeps this off the step hot path).
5. **Entryless-orphan reap** (LIFE-23): reap ALL live API tasks with no sidecar
   entry, unconditionally — NO mass-deletion guard (per-identity scoping means only
   our own cards; orphaned-unaddressable is worse than a diagnosable wipe). Audible:
   log entryless reaps with a count to the FAIL-3 log — breadcrumb, never a gate.
   (Safe against the create race: API-created-but-sidecar-unwritten exists only
   inside the create's mutex hold, which excludes the reap tick.)
6. **Recycle-bin TTL scavenge** (phase 04 helper) folded in opportunistically.
7. **Shutdown check** (LIFE-19): exit when the supervised set is empty after a
   reconciliation poll, with a short anti-flap idle-grace (a quick
   start→done→remove burst must not thrash spawn/exit). On exit: release the
   LIFE-18 mutex, disable the boot-recovery startup item.

### Boot recovery (LIFE-20, "Logoff/reboot recovery")

- Premise: no task is valid across a reboot — recovery is an UNCONDITIONAL clear,
  not per-task reasoning. Mechanism: the manifest-declared MSIX StartupTask
  (phase 01, declared disabled) toggled programmatically — enabled
  (`RequestEnableAsync`) while the watchdog has live work, disabled on its clean
  exit. The boot-launched instance recognizes itself by ACTIVATION KIND
  (`AppInstance.GetActivatedEventArgs()` → StartupTask activation; ratified
  2026-07-07) — StartupTask launches carry no CLI args, and bare `atv` remains
  usage/help. The boot run does a flat clear — Remove every task, delete every sidecar
  entry and icon, AND wipe the recycle bin (the internal, non-interactive exception
  to `clear`'s default recycle-bin exclusion) — then disables itself and exits.
- No boot-time-age watermark, no shutdown-signal handler (rejected). User can
  disable the StartupTask in Task Manager → degrades to "accept the gap",
  non-disruptive. Accepted: rare console-window flash on this OS-launched path
  (INFRA-22 deferred); a session starting during the brief recovery window can lose
  its task, self-healing on next `start`.

### Dev-loop hygiene (INFRA-20)

- Pre-build MSBuild reap target: kill THIS worktree's watchdog (locate via its
  per-worktree PFN-scoped mutex/process) before compile so a locked exe never
  blocks rebuild. LIFE-19 self-exit is the passive backstop.
- launchSettings profiles (INFRA-21): default (`WATCHDOG_MODE=off`, phase 01),
  "watchdog (foreground)" — runs `atv watchdog` directly for F5 breakpoints — and
  "app + spawn" (`WATCHDOG_MODE=spawn`) to exercise the real detached path.

## Files affected

```
src/Atv/Watchdog/WatchdogLoop.cs
src/Atv/Watchdog/EnsureWatchdog.cs      # replaces phase 08's inert gate
src/Atv/Watchdog/IWatchdogHost.cs  + ProcessHost.cs / InProcThreadHost.cs
src/Atv/Watchdog/BootRecovery.cs        # StartupTask enable/disable + boot clear
src/Atv/Cli/Verbs/WatchdogVerb.cs       # hidden verb wiring
src/Atv/Properties/launchSettings.json  # + the two new profiles
build/Atv.DevReap.targets               # pre-build reap
tests/Atv.LogicTests/Watchdog/*         # FakeHost + fake clock + fake store + injected dirs
tests/Atv.AdapterTests/WatchdogProcessHostTests.cs      # THE required integration test
```

## Acceptance criteria (written first)

1. Unit (FakeHost + fake clock): every tick behavior — per-state expiry at exactly
   the configured thresholds; freshness ordering (a write interleaved before the
   expiry compare rescues the task — deterministic via the fake clock/hooks);
   expiry writes a complete recycle record read from the card; HiddenByUser sweep;
   entryless reap with logged count; anti-flap grace; empty-set exit + startup-item
   disable; acquire-or-exit single-instance (loser exits without disturbing winner).
2. Unit: `EnsureWatchdog` decide-to-spawn matrix — mode × mutex-liveness → spawn /
   inproc / nothing; spawn failure logs and continues.
3. REQUIRED integration test (adapter suite, per-worktree identity): a real
   detached `ProcessHost` watchdog spawns from a write verb, acquires the
   single-instance mutex, survives parent exit, expires a short-idle task (config
   pinned low), and self-exits on empty set. Without this test the process
   mechanics rot silently (the dev loop runs `off`).
4. Boot recovery: unit-test the enable/disable state machine; manually verify once
   on this machine that a reboot with a live card yields a clean taskbar and a
   wiped recycle bin.
5. Dev loop: F5 default profile spawns nothing; "watchdog (foreground)" hits a
   breakpoint in `WatchdogLoop`; a stale spawned watchdog does not block the next
   `dotnet build` (reap target proven by leaving one running).
6. Manual sleep/wake sanity check: a Running card, laptop asleep past the idle
   period, resumes without the card being reaped on stale pre-sleep state if a
   write arrives first (the requirements.md scenario).

## Out of scope

`clear`'s user-facing recycle-bin flag (phase 10), upgrade/uninstall interplay
(phase 12 confirms DIST-6/DIST-9 behavior — no watchdog code needed for either).
