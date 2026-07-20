# Phase 01: Foundation — solution layout, full-package identity, dev loop, AOT

**Depends on:** nothing (first phase)
**Unblocks:** every other phase

## Goal

Replace the POC's hand-rolled sparse-package scaffolding with the decided
full-package identity model, establish the multi-project solution layout, the brand
constant, the stamped `AppxManifest` build machinery, the winapp-CLI dev loop, and the
NativeAOT release configuration. At the end, `dotnet run` / F5 carry package identity
with zero manual pre-steps, and a release AOT publish produces a ~3 MB single exe.

## Decisions implemented

- ERGO-18 ("The shipped command name"): binary `atv`, brand "Agent Task Void", brand
  parameterized through a single source of truth.
- INFRA-17 ("Dogfood/run ergonomics without a load-bearing script"): full-package
  identity via `Microsoft.Windows.SDK.BuildTools.WinApp` (build-time-only NuGet;
  spike-verified: no runtime dependency, byte-identical AOT output). `dotnet run` is
  redirected into `winapp run`, which loose-layout-registers the build output and
  launches with identity. The sparse setup (`Register-Identity.ps1`,
  `Unregister-Identity.ps1`, `identity\AppxManifest.xml`, the `<msix>` element in
  `app.manifest`) is DROPPED. Dev prerequisite: Developer Mode on (one-time machine
  setting; checked by `doctor` in phase 10).
- DIST-1 ("The end-user distribution vehicle"): full-package identity model end to
  end — loose layout in dev, full MSIX at release. One binary.
- DIST-7 ("The release full-package manifest"): one brand-parameterized
  `AppxManifest` template serves dev loose-layout AND release pack. Implementation
  rules are settled — do not relitigate:
  - Template invariants: `AppExecutionAlias` → `atv`; a declared-but-DISABLED MSIX
    `StartupTask` (for LIFE-20 boot recovery, phase 09); `TargetDeviceFamily` min
    26100; `runFullTrust`; NO protocol handler (INTER-1 deferred). Publisher is
    static (not parameterized). Display/identity fields derive from the brand.
  - Only two fields are stamped at build time: Identity **Name** = brand + a hash of
    the build-output path (gives per-worktree isolation, INFRA-16), and **Version** =
    Nerdbank.GitVersioning `$(BuildVersion)` (4-part numeric — NOT `$(Version)`,
    whose semver+githash suffix makeappx rejects).
  - One MSBuild target in an imported `.targets` reads the template, replaces
    `{IdentityName}`/`{Version}`, writes a RID-qualified `obj/` copy with
    `WriteOnlyWhenDifferent`, gated by `Inputs`/`Outputs`.
  - Compute the version token and the `obj/` path inside the target's EXECUTION-TIME
    `PropertyGroup`, never the file body (evaluation-time values are empty/unfinalized
    and fail silently).
  - Consumption is out-of-process winapp: dev `winapp run` reads the stamped manifest
    from build output; release packaging (phase 12) `Exec`s
    `winapp package --manifest <obj-copy>` explicitly.
- DIST-3 ("Dev vs release identity (PFN) divergence"): PFN divergence between pools
  is deliberate. Nothing hardcodes a PFN — all PFN-keyed values derive at runtime
  (`Package.Current` / `ApplicationData.Current`).
- INFRA-2 ("Minimizing the on-disk size") + INFRA-3 ("C#/NativeAOT over C++/Rust"):
  NativeAOT single-file self-contained exe, ~3.0–3.5 MB accepted. Bank the free
  levers: `OptimizationPreference=Size`, `InvariantGlobalization` (unless
  culture-sensitive formatting emerges), feature switches off
  (`StackTraceSupport`, `UseSystemResourceKeys` on, `DebuggerSupport`,
  `EventSourceSupport`), don't ship the `.pdb`. Optional to try/measure: CsWinRT 2.2
  opt-in AOT/size knobs. Sub-1 MB is explicitly NOT a goal.
- INFRA-19 ("Inner-loop watchdog suppression"), partial: the default
  `launchSettings.json` profile sets `WATCHDOG_MODE=off` so F5 / Ctrl+F5 /
  `dotnet run` never spawn a detached watchdog (the setting is consumed from
  phase 09; harmless before then).
- CLAUDE.md: rewrite the build/identity sections to match the new model (the current
  text documents the sparse setup being deleted).

## Files affected

Delete: `Register-Identity.ps1`, `Unregister-Identity.ps1`, `identity/` (move the two
logo PNGs into the new package-assets folder), the `<msix>` element usage in
`app.manifest` (delete the file if nothing else needs it).

Create/restructure (names are the plan's proposal; keep them consistent once chosen):

```
Directory.Build.props            # brand constant (MSBuild property), NBGV, shared AOT props
Directory.Packages.props         # (optional) central package versions
src/Atv/Atv.csproj               # AssemblyName=atv; CsWinRT projection props move here
src/Atv/Program.cs               # POC code migrated; full rewrite lands over phases 02–10
src/Atv/Branding.cs              # brand constant surfaced to code (generated from the MSBuild property or a single shared source file)
src/Atv/Package/AppxManifest.template.xml
src/Atv/Package/Assets/…         # logos from identity/Assets
build/Atv.Package.targets        # the DIST-7 stamping target
src/Atv/Properties/launchSettings.json   # default profile: WATCHDOG_MODE=off
version.json                     # NBGV config
CLAUDE.md                        # updated instructions
```

Preserve in `src/Atv/Atv.csproj`: the CsWinRT projection setup
(`CsWinRTGenerateProjection`, `CsWinRTIncludes=Windows.UI.Shell.Tasks`, and the
explicit `CsWinRTInputs` pointing at the SDK UnionMetadata `Windows.winmd` — the
experimental namespace is NOT in the system WinMetadata; see CLAUDE.md).

## Acceptance criteria

1. `dotnet build` clean from a fresh clone (given SDK + Developer Mode); no sparse
   remnants anywhere in the tree.
2. `dotnet run -- list` (or the surviving POC command) executes WITH package identity
   — verify via `GetCurrentPackageFullName` returning a real PFN whose Name embeds the
   brand + path hash.
3. The stamped manifest exists only under `obj/` (RID-qualified), carries the
   NBGV 4-part version and the brand+hash Identity Name; a no-op rebuild does NOT
   rewrite it (WriteOnlyWhenDifferent verified).
4. `dotnet publish -r win-x64 -c Release` with `PublishAot=true` succeeds with zero
   trim/CsWinRT warnings; the exe is ≤ ~3.5 MB; running it (from a registered layout)
   still drives the API (POC create/list/clear smoke).
5. The POC's existing create/list/clear behavior still works end to end on this
   machine (taskbar card appears) — the foundation swap must not regress the working
   proof of concept.
6. CLAUDE.md describes the new model (winapp dev loop, no register scripts).

## Out of scope

Test projects (phases 02/03), the watchdog and its launch profiles (phase 09),
release pack/sign (phase 12).
