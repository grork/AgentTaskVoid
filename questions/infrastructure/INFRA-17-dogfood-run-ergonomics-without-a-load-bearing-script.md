# INFRA-17: Dogfood/run ergonomics without a load-bearing script
**Status:** DECIDED
**Plan:** phase-01
**Decision:** Adopt the full-package MSIX identity model and take a dependency on
Microsoft's **winapp CLI**. `dotnet run` and VS / VS Code F5 gain package identity
automatically via the build-time NuGet package `Microsoft.Windows.SDK.BuildTools.WinApp`
-- its MSBuild targets redirect `dotnet run` into `winapp run`, which loose-layout-
registers the build output and launches with identity. No bespoke script. Spike-verified
(2026-07-04) this needs **no** `Microsoft.WindowsAppSDK` runtime dependency and does not
affect the NativeAOT exe (byte-identical output). The hand-rolled sparse setup
(`Register-Identity.ps1`, `identity\AppxManifest.xml`, the `<msix>` app.manifest) is
DROPPED. Dev-loop prerequisite: Developer Mode on (loose-layout registration) -- a
one-time machine setting, `doctor`-checkable, irrelevant to end users. The end-user
distribution vehicle is DIST-1 ("The end-user distribution vehicle").
**Parent:** INFRA-14

Running the tool on a dev box should follow common convention with zero manual
pre-steps: F5 from Visual Studio just works (no "run these scripts first"), `dotnet run`
works, and the existing build-and-run flow works. Accessible to humans AND robots
without deriving deep upfront knowledge.

Driving constraint (operator, 2026-07-03): a load-bearing bespoke PowerShell script is
bad -- it forces tribal knowledge and breaks the assumed ergonomics. The tension: the
real API needs package identity to exist, but requiring a manual register step before
F5/`dotnet run` violates the convention. To design: how identity gets provisioned for
the interactively-run tool via a conventional mechanism (e.g. a build target / MSBuild
integration) rather than a script the user must know to run. Distinct from INFRA-16
(automated-test isolation) -- this is the single-dev interactive loop.

Decision detail (2026-07-04):
- Bedrock spike (Sonnet, 2026-07-04): NativeAOT + the experimental CsWinRT
  `Windows.UI.Shell.Tasks` projection + identity builds, runs, and drives the API
  (create/list/clear) with zero trim/CsWinRT warnings; `dotnet run` launched with a real
  PFN using ONLY the build-time package; removing `Microsoft.WindowsAppSDK` kept identity
  working with byte-identical AOT output. `Microsoft.Windows.SDK.BuildTools.WinApp` has
  zero nuspec dependencies / no runtime assemblies -- by construction it cannot bloat the
  binary.
- Structural finding that forces the model choice: sparse (external-location) and
  full-package identity are MUTUALLY EXCLUSIVE at the manifest level -- `winapp run`, the
  `dotnet run` integration, and a full-MSIX install all reject a sparse manifest
  (`0x80073CF9`, "must be installed with an external location"). winappcli's clean dev
  loop exists ONLY on the full-package model, so keeping sparse would have kept a bespoke
  register path. Therefore: winappcli => full-package => sparse dropped.
- winappcli maturity (Public Preview, v0.4) accepted (operator, 2026-07-04): the
  "what if it's EOL'd" failure mode is a one-time fork of the integration -- the mechanism
  underneath is the `RegisterPackageByUriAsync` proven in the prior spike -- not a
  load-bearing risk.
- Build-phase follow-ups: swap the sparse scaffolding for a `winapp init`-style
  full-package manifest; add the dev-only Developer-Mode check to `doctor`; DIST-5
  confirms the end-to-end release leg (AOT exe -> full MSIX -> install -> drive API).
- INFRA-16 ("Test-time identity provisioning and deep isolation") inherits this model
  (per-run identities via loose-layout / `winapp create-debug-identity`) and is handled
  there.
