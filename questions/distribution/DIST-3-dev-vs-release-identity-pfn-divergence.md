# DIST-3: Dev vs release identity (PFN) divergence
**Status:** DECIDED (mechanism corrected + isolation made structural, 2026-07-10, phase 12;
amended by DIST-12, 2026-07-18: dev-interactive no longer owns bare `atv` — it stamps
`atv-dev`, and the retail identity is the operator's daily driver. The "no `atv-dev`"
rejection below is superseded.)
**Plan:** all-phases

**Amendment 2026-07-10 (ratified, phase 12) — the original mechanism claim below is WRONG; the three-pool GOAL stands and is now structurally enforced.**
A PFN is `<Name>_<PublisherId>`, and `PublisherId` is a hash of the manifest's declared
`Identity/@Publisher` STRING — NOT the signing certificate (Microsoft Learn, "Package
Identity"). So dev and release do NOT diverge merely by being signed with different certs;
they diverge only when a declared identity STRING differs. As built through phase 11,
dev-interactive and the dev-cert release share the same template → same Name
(`Codevoid.AgentTaskVoid-<pathhash>`) AND same Publisher (`CN=AppTaskInfoCli`) → the IDENTICAL PFN
`Codevoid.AgentTaskVoid-bbbb1168_016qghrny08mj` (confirmed by computing the PublisherId hash of
`CN=AppTaskInfoCli`, 2026-07-10 — it equals the live dev PFN). Isolation was therefore NOT
structural: it leaned entirely on the DEFERRED DIST-2 real-cert Publisher edit.

Fix (ratified): make manifest stamping **build-kind-aware** (the mechanism INFRA-16 already
uses for the test pool):
- **Release** stamps a CLEAN, pathhash-free `Identity/@Name = <brand>` (e.g. `Codevoid.AgentTaskVoid`)
  and owns the bare `atv` alias. A shipped identity must not encode the developer's build
  directory path.
- **Dev-interactive** keeps `<brand>-<pathhash>` and also owns `atv` (on a dev box the
  primary `atv` = the working copy — convenient and correct).
- **Test** keeps `<brand>.Test.<hash>` + the suffixed alias `atv-test-<hash>` (unchanged).
Different Names → different PFNs → **structural** isolation, independent of publisher/cert.
**No `atv-dev` daily command** (rejected: it would tax every future in-repo invocation with a
"which am I running?" ambiguity to solve a coexistence case that only arises in the
artificial release-on-dev-box test). Dev and release collide on the bare `atv` only when both
are installed on one machine — normally they are not (dev box → dev; user box → release); the
phase-12 release-on-dev **smoke uses a distinct throwaway identity + alias (`atv-reltest`)**,
exactly as the test pool already coexists.
Additional (operator, 2026-07-10): the dev/test build must **mark itself `(dev)`/`(test)`** in
console/log/trace output so identity is never ambiguous when reading traces.

---

**Original decision (mechanism superseded above; the isolation intent it records still holds):** Accept PFN divergence as deliberate isolation. There are three identity pools
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
