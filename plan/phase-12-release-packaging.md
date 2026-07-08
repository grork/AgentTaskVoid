# Phase 12: Release packaging & distribution verification

**Depends on:** phases 09/10/11 (a complete product to package); phase 01 built the
manifest machinery this consumes.
**Unblocks:** phase 13 (install instructions, doctor's winget remedy string)

## Goal

Produce the signed full-MSIX release artifact for both architectures, verify the
complete release leg end to end (the DIST-8 confirmation), and prepare the winget
manifest. Publication operations (real cert purchase, winget submission) stay
deferred (DIST-2) — this phase proves the pipeline with a dev cert.

## Decisions implemented

- DIST-1 ("The end-user distribution vehicle"): one signed full MSIX containing the
  NativeAOT `atv.exe` with an `AppExecutionAlias` putting `atv` on PATH, delivered
  via winget. Built and signed with Microsoft's **winapp CLI** (`winapp pack` /
  `winapp sign`) — the same tool as the dev loop. Per-user, no-admin, no-Dev-Mode
  install. Release ships BOTH x64 and ARM64 NativeAOT builds; the winget manifest
  carries both (per-arch MSIX vs bundle is this phase's build detail to settle).
- DIST-7 ("The release full-package manifest"): packaging consumes the stamped
  `obj/` manifest via an orchestrating MSBuild target that `Exec`s
  `winapp package --manifest <obj-copy>` — explicit path, never auto-detect.
  Publisher is the static release value; Identity Name/Version stamp exactly as in
  dev (brand + path hash / NBGV).
- DIST-8 ("The joined release-leg spike"): no standalone spike — the confirmation
  folds into THIS first real packaged build: AOT publish → `winapp pack` → dev-cert
  sign → install → launch via the execution alias → confirm it drives `AppTaskInfo`.
  Recorded prediction: works. If it does NOT, the failure reopens the PREMISE (the
  experimental API not honoring installed-MSIX identity — a platform problem), not
  the vehicle; do not thrash the packaging choice.
- DIST-3 ("Dev vs release identity divergence"): the installed package's PFN
  differs from dev/test pools — verify the installed tool sees its OWN empty
  state (config, sidecar, tasks.json), confirming pool isolation end to end.
- DIST-6 ("Package upgrade while the watchdog is running") — free confirmation, no
  code: `winget upgrade`/re-install with a live watchdog stages the new version and
  defers registration (`ERROR_PACKAGES_IN_USE` fallback), exits successfully; the
  old watchdog keeps supervising, self-exits on empty set, next invocation runs the
  new version. Observe once; no upgrade-specific handling exists or should be added.
- DIST-9 ("Uninstall behavior with live tasks") — free confirmation, no code:
  uninstall with live cards deletes the app-data tree and the Shell drops the cards
  immediately (empirically verified 2026-07-05 on the dev path). Confirm once on
  the packaged path.
- DIST-4 ("Zero-pre-install posture"): finalize the winget package id and wire the
  real `winget install <id>` string into `doctor`'s remedy line (phase 10
  placeholder).

## Files affected

```
build/Atv.Release.targets       # or equivalent: publish (x64+ARM64) → winapp pack → winapp sign orchestration
build/winget/<manifests>        # winget manifest set (both architectures)
docs/release.md                 # the release runbook: commands, dev-cert vs real-cert note, deferred publication steps
src/Atv/… (doctor remedy string constant)
CLAUDE.md                       # release-build instructions
```

## Acceptance criteria

1. A single command (or documented two-step) produces signed MSIX artifacts for
   x64 and ARM64 from a clean tree; version comes from NBGV; re-running without
   changes is a no-op repack (Inputs/Outputs respected).
2. **The DIST-8 leg, on this machine (x64)**: install the signed package → open a
   NEW terminal → `atv start s1 --title Hi` via the PATH alias → card renders;
   `atv doctor` reports identity + API green; watchdog spawns and behaves. ARM64
   artifact builds clean (functional verification on ARM64 hardware is
   best-effort/deferred if no device).
3. Pool isolation: the installed package's `doctor` paths point at the release PFN
   app-data, distinct from the dev worktree's; dev state is invisible to it.
4. Upgrade-in-place observed once with a live watchdog: install v(N+1) over a
   running vN watchdog → install succeeds (possibly deferred registration), no
   corruption, supervision continuous, new version active after quiesce.
5. Uninstall observed once with live cards + running watchdog: taskbar cleans
   immediately, app-data gone, no stale processes wedged (watchdog self-exits on
   its next empty poll).
6. winget manifests validate (`winget validate` / wingetcreate), carrying both
   architectures. Actual submission + real-cert signing remain deferred (DIST-2) —
   documented as the two remaining ship-time steps in `docs/release.md`.
7. `doctor`'s not-installed remedy prints the finalized package id.

## Out of scope

Real certificate acquisition and winget repository submission (deferred, DIST-2).
Store distribution. Auto-update handling beyond the observed default behavior.
