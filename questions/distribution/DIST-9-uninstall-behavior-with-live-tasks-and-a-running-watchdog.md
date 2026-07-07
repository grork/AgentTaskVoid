# DIST-9: Uninstall behavior with live tasks and a running watchdog
**Status:** DECIDED
**Decision:** No mitigation needed -- the Shell self-cleans. Empirically (2026-07-05) removing
the package deletes its app-data tree (including `tasks.json` at the standard package-relative
`SystemAppData\AppTasks\` location) AND explorer.exe immediately drops the rendered card. The
feared "permanent unaddressable turds" scenario does not occur; the "no unaddressable cards"
value prop survives uninstall with no action from us.

Detail (2026-07-05, combined DIST-9/ERGO-24 probe):
- Probe: registered identity, created a live card, ran `Remove-AppxPackage` while it was on the
  taskbar. Result: app-data tree gone immediately (`Test-Path` false), and the operator confirmed
  the taskbar icon/card was cleaned up -- no stale rendering, no broken-icon husk.
- Representativeness: tested via dev unregister of the external-location sparse package; `winget
  uninstall` (DIST-1, "The end-user distribution vehicle") of an installed MSIX uses the same
  deployment API and default app-data removal, so it's representative. Free confirmation lands at
  the first real packaged uninstall (same boundary as DIST-8, "The joined release-leg spike") --
  no standalone spike.
- Running-watchdog sub-case: same MSIX in-use boundary as DIST-6 ("Package upgrade while the
  watchdog is running"). The watchdog is stateless-over-disk (LIFE-16, "Watchdog granularity /
  scope") and self-exits on an empty supervised set (LIFE-19, "Watchdog shutdown conditions");
  once app-data is gone the cards are dropped regardless, so a lingering watchdog has nothing to
  supervise and no turd can persist. Worst case a busy session delays uninstall completion until
  tasks quiesce -- accepted, exactly as DIST-6.
- Dropped as unnecessary (superseded by the empirical): the earlier speculative "run `atv clear`
  before uninstall" guidance and a reinstall-same-identity self-heal escape hatch. NOT adopted.
- Residual edge (not the default path): a non-default uninstall that PRESERVES app-data would
  leave `tasks.json` on disk; reinstalling the same release identity (DIST-3, "Dev vs release
  identity (PFN) divergence") + `atv clear` reaps it. Noted, not mitigated.

What happens on `winget uninstall` (DIST-1's channel) while tasks are live on the taskbar
and/or a watchdog is running -- and what, if anything, do we do about it?

Known/asserted going in (operator, 2026-07-05): the Shell will NOT clear `tasks.json`
automatically when the owning package is removed -- so uninstalling with live cards risks
permanent, unaddressable taskbar turds (the identity that could `Remove()` them is gone,
and MSIX has no uninstall-script hook, so nothing of ours can run at removal time). "We
need something here."

To answer:
- Empirical: what actually happens to rendered cards and `tasks.json` on package removal?
  Package app-data (sidecar, icon copies, recycle bin, FAIL-3 log all live there) is
  deleted with the package -- does `tasks.json` live there too and go with it, and does the
  Shell drop the cards when their backing store vanishes, or keep rendering stale state
  (including broken icon references)?
- What happens to a running watchdog / in-flight `atv` invocation during removal (the same
  force-kill-vs-block boundary as DIST-6, "Package upgrade while the watchdog is running" --
  but removal has no "new version respawns and reconciles" backstop)?
- Posture: if turds persist, what mitigates -- documented "run `atv clear` before
  uninstall" guidance, a reinstall-to-clean escape hatch, or accepting it? Given the
  product's core value is "no unaddressable cards", silent acceptance needs justifying.

Surfaced by the 2026-07-05 review pass; adjacent to DIST-6 but not covered by it.
