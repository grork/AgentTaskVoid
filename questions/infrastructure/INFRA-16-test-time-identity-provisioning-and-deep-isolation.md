# INFRA-16: Test-time identity provisioning and deep isolation
**Status:** DECIDED
**Decision:** Only the real-API adapter suite (INFRA-9, "Integration-test harness over
tasks.json") needs identity; the fake logic suite (INFRA-11) needs none. The real suite is a
**Microsoft.Testing.Platform (MTP) exe** -- it IS the identity-carrying process -- holding a
per-worktree FULL-PACKAGE identity granted by **loose-layout registration performed OUTSIDE
the test run**. In-test code only asserts-or-skips; it never registers. Cleanup is an
EXTERNAL non-identity step, never suite teardown. Restated 2026-07-05: the originally
recorded sparse mechanics (`<msix>` app.manifest, `ExternalLocationUri`) are ELIMINATED per
DIST-1's ("The end-user distribution vehicle") full-package-end-to-end model.
**Parent:** INFRA-14

Decision detail (2026-07-04; restated on the full-package model 2026-07-05):
- **Identity-carrying process:** the real-adapter suite builds as an MTP test exe. A classic
  `dotnet test` testhost cannot hold our identity (not our exe); an MTP project builds its
  own exe, which can.
- **Identity model -- full-package loose layout:** the test project consumes the DIST-7
  ("The release full-package manifest") brand-parameterized AppxManifest template, stamped
  at build time with Identity Name = brand + a hash of the test build-output path. The
  stamped manifest lands in the build output; registering that output as a loose-layout
  package grants the exe identity when it runs from there. NO `<msix>` app.manifest and NO
  `ExternalLocationUri` -- sparse and full-package are mutually exclusive at the manifest
  level (INFRA-17's `0x80073CF9` finding), and dev/test/release are all full-package.
- **Isolation = one identity per worktree:** concurrent worktrees -> different build paths ->
  different Identity Names -> different PFNs -> isolated `tasks.json`, write mutex (INFRA-6),
  and sidecar (ERGO-21, "The sidecar store design"); also isolated from the dev-interactive
  identity and from release (DIST-3, "Dev vs release identity (PFN) divergence"). Brand
  prefix comes from the ERGO-18 ("The shipped command name") constant.
- **Registration happens OUTSIDE the test run:** the `_TestRunStart` MSBuild hook
  (post-build, pre-launch, fires on run-not-build) register-or-asserts the per-worktree
  loose-layout package BEFORE the exe launches, so `dotnet test` (CLI/CI) is single-pass.
  The in-test gate only ASSERTS: it checks `GetCurrentPackageFullName`; when identity is
  absent (VS Test Explorer / direct-exe launches bypass the hook in a fresh worktree) it
  SKIPS the real suite with a "register first -- run `dotnet test` once or the explicit
  register target, then re-run" message. The gate NEVER registers in-proc. Registration
  persists between runs, so the no-identity skip is a one-time per-worktree bootstrap.
- **Mechanism:** programmatic `PackageManager` loose-layout registration
  (`RegisterPackageByUriAsync` on the stamped `AppxManifest.xml` with the
  development/loose-layout option) -- NOT the winappcli CLI (tests need unique per-worktree
  PFNs and deterministic control). Prereq: machine Developer Mode (same as INFRA-17,
  "Dogfood/run ergonomics without a load-bearing script").
- **Build-phase verification (the one open mechanic):** the INFRA-17 spike proved identity
  for winapp-mediated launches; the MTP exe is launched DIRECTLY (`dotnet test` / VS).
  Verify a direct launch of an exe from a registered loose layout carries identity; if it
  does not, the fallback is activating the suite via a declared `AppExecutionAlias` (alias
  launch grants identity) -- same registration model either way.
- **Cleanup -- EXTERNAL non-identity step ONLY:** unregister runs from a separate process
  that does not hold the identity (an explicit target/script; exact form is build-phase).
  NEVER in suite teardown -- removing the package out from under the running
  identity-holding process kills it mid-run. NEVER an MSBuild after-hook -- it is skipped on
  test failure. Registration persists between runs; an external sweep reaps orphans (stale
  `<brand>.Test.*` registrations whose target path no longer exists -- crashed runs, deleted
  worktrees).
- **Consistency:** within one run the real suite stays serial on that run's single
  `tasks.json` (INFRA-9); per-worktree identity isolates ACROSS concurrent runs. The fake
  logic suite (INFRA-11) uses none of this.

Only the real-API adapter suite (INFRA-9, "Integration-test harness over tasks.json")
needs identity -- the fake logic suite needs none and is already isolated (temp dirs,
parallel). For the real suite: register-or-assert identity ONCE at suite setup;
teardown ALWAYS cleans up (including after a crashed run). Auto-registration is
acceptable (sparse register is per-user, no admin).

Hard part -- deep isolation: two branches on one dev box must run their real-API tests
CONCURRENTLY without fighting over identity. `tasks.json` and the write mutex (INFRA-6)
are scoped per package identity / PFN, so a single shared identity means concurrent runs
collide on one `tasks.json`. Deep isolation therefore implies each run gets its OWN
identity (PFN suffixed per branch/run), registered + unregistered around the run -- NOT
one shared identity behind a cross-run lock (that serializes, not isolates). To design:
how the per-run PFN is derived, the register/unregister lifecycle and its guaranteed
cleanup, and the INFRA-14 path-rot fix (registration path derived from build output, not
a hardcoded `bin\Debug\...` literal).
