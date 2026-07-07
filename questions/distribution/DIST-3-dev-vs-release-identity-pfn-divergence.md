# DIST-3: Dev vs release identity (PFN) divergence
**Status:** DECIDED
**Decision:** Accept PFN divergence as deliberate isolation. There are three identity pools
by design -- release (real cert publisher), dev-interactive (dev/winapp publisher), and
per-worktree test (Name + build-path hash, INFRA-16, "Test-time identity provisioning and
deep isolation") -- and each gets its own `tasks.json`, write mutex (INFRA-6), and sidecar
(ERGO-21, "The sidecar store design") via package-relative resolution. Isolation is the
feature: a dev experiment or a crashed test watchdog can never touch release state, and
concurrent worktrees don't collide. Forcing one common publisher was rejected -- it would
pin dev to the release cert (DIST-2, deferred + secret), reintroduce shared-state
collisions, and defeat INFRA-16.

Invariant baked in: nothing hardcodes a PFN. Everything PFN-keyed derives it at runtime
from the current package -- the INFRA-6 / LIFE-18 ("Watchdog single-instance enforcement")
mutex `Local\<brand>-<PFN>-watchdog`, `ApplicationData.Current` paths, and `tasks.json` +
sidecar. The ERGO-18 ("The shipped command name") brand constant is the single source for
the Name prefix; publisher and path-hash are the divergence axes layered on top. DIST-7
("The release full-package manifest") already implements the stamping.

The dev loose-layout identity and the release signed-MSIX identity derive from the same
manifest, but the PFN's publisher hash depends on the signing publisher -- a dev/test
publisher and a real release cert produce DIFFERENT PFNs. Everything keyed off PFN
diverges between dev and release: the write mutex (INFRA-6), the sidecar / app-data paths
(ERGO-21, "The sidecar store design"), and `tasks.json`. Decide: keep the publisher
identical across modes (one pool), or accept divergence as deliberate dev/release
isolation. Must confirm the ERGO-18 ("The shipped command name") brand constant
parameterizes both. Likely "divergence is fine / arguably good," but needs ratifying.
