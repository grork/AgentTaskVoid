# Phase 03: Real-API adapter test harness + per-worktree test identity

**Depends on:** phase 01 (manifest template + stamping), phase 02 (`AppTaskStore`)
**Unblocks:** phase 09 (the required ProcessHost integration test lands in this suite)

## Goal

Stand up the small, serial, real-API adapter test suite: a Microsoft.Testing.Platform
(MTP) exe that carries its own per-worktree package identity, registered outside the
test run, proving the thin `AppTaskStore` adapter faithfully drives the platform.
This is deliberately the only flaky, env-bound, non-parallel suite — everything else
stays in the fake-backed logic suite.

## Decisions implemented

- INFRA-9 ("Integration-test harness over tasks.json"): the real suite is small and
  data-driven; it registers-or-asserts identity, shares the single per-identity
  `tasks.json` (hence SERIAL), clears before/after each test, asserts via the store's
  `FindAll()` plus a raw `tasks.json` reader, and covers: adapter translation +
  `tasks.json` fidelity, ≥1 end-to-end per verb primitive, and a PERIODIC (explicitly
  opt-in, never a gate) reality-check that the platform still clobbers as modeled —
  the demoted 4×100 multi-process run.
- INFRA-16 ("Test-time identity provisioning and deep isolation"):
  - The suite builds as an **MTP exe** — it IS the identity-carrying process (a
    classic `dotnet test` testhost cannot hold our identity).
  - It consumes the DIST-7 manifest template, stamped with Identity Name = brand + a
    hash of the TEST build-output path → per-worktree PFN → isolated `tasks.json`,
    write mutex, and sidecar across concurrent worktrees, and from dev/release pools.
  - **Registration happens OUTSIDE the test run**: a `_TestRunStart` MSBuild hook
    (fires on run-not-build) register-or-asserts the per-worktree loose-layout
    package via programmatic `PackageManager.RegisterPackageByUriAsync` on the
    stamped `AppxManifest.xml` (development/loose-layout option) — NOT winapp CLI
    (tests need deterministic per-worktree PFNs). Prereq: Developer Mode.
  - The in-test gate only ASSERTS: check `GetCurrentPackageFullName`; when identity
    is absent (VS Test Explorer / direct-exe launch in a fresh worktree), SKIP the
    suite with the message "register first — run `dotnet test` once or the explicit
    register target, then re-run". The gate NEVER registers in-proc.
  - **Cleanup is an external, non-identity step only**: an explicit MSBuild
    target/script run from a process that does not hold the identity. NEVER suite
    teardown (unregistering under the running identity-holder kills it mid-run),
    NEVER an MSBuild after-hook (skipped on test failure). Registration persists
    between runs; the external sweep also reaps orphaned `<brand>.Test.*`
    registrations whose target path no longer exists.
  - **The one open mechanic to verify first**: the prior spike proved identity for
    winapp-mediated launches; the MTP exe is launched DIRECTLY. Verify a direct
    launch of an exe from a registered loose layout carries identity. If it does
    not, the fallback is launching the suite via a declared `AppExecutionAlias`
    (alias launch grants identity) — same registration model either way.
- INFRA-11 ("Test strategy for machines where the API is unavailable"): when the API
  is absent (`IsSupported()` throws/false even with identity), the suite SKIPS —
  never fails.
- INFRA-15 (fake fidelity confirmation, automated half): the real suite carries the
  automated confirming checks — unknown-Id / removed-Id behavior, and the negative
  whole-content-replacement check (an `Update` replaces content wholesale; nothing
  merges). Tag these as the checks referenced by
  `docs/testing/fake-fidelity-promises.md`.
- INFRA-19 ("Inner-loop watchdog suppression"): real-adapter test runs set
  `watchdog-mode=off` so no supervisor perturbs assertions.

## Files affected

```
tests/Atv.AdapterTests/Atv.AdapterTests.csproj   # MTP exe; consumes the DIST-7 template via build/Atv.Package.targets
tests/Atv.AdapterTests/IdentityGate.cs           # assert-or-skip fixture
tests/Atv.AdapterTests/TasksJsonReader.cs        # raw reader: %LOCALAPPDATA%\Packages\<PFN>\SystemAppData\AppTasks\tasks.json (shape: {tasks:[...], version})
tests/Atv.AdapterTests/AdapterFidelityTests.cs   # per-primitive round-trips, unknown-Id, whole-content replacement
tests/Atv.AdapterTests/PeriodicClobberTests.cs   # 4×100 multi-process run, [Explicit]/trait-gated
build/Atv.TestIdentity.targets                   # _TestRunStart register-or-assert; explicit Register/Unregister/SweepOrphans targets
```

## Acceptance criteria

1. From a fresh worktree, `dotnet test` on the adapter suite is single-pass: the
   hook registers the per-worktree identity, the exe launches with a PFN embedding
   the test-output path hash, tests run serially and green (on this machine, where
   the API is present).
2. Launching the test exe directly without prior registration produces the
   documented SKIP (not a failure); after one `dotnet test`, direct launches carry
   identity (or the AppExecutionAlias fallback is implemented and documented).
3. Two concurrent worktrees can run the suite simultaneously without interference
   (distinct PFNs → distinct `tasks.json`). Verify once manually.
4. Every test clears the identity's tasks before AND after itself; a crashed test
   run leaves at most stale tasks that the next run's clear removes.
5. The unregister/sweep target removes the worktree's test identity and any orphaned
   `<brand>.Test.*` registrations; running it is never required for tests to pass.
6. On an API-absent machine (or simulated), the suite reports skipped, exit success.
7. The periodic 4×100 clobber test exists, is excluded from default runs, and when
   run manually reproduces last-writer-wins loss without the mutex (confirming the
   fake's flagship promise).

## Out of scope

The watchdog ProcessHost end-to-end test (added by phase 09 into this suite). Any
logic testing (stays in the fake suite).
