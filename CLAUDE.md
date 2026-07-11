# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

```
dotnet build
```

Solution: `AppTaskInfoCli.slnx` -> `src/Atv/Atv.csproj`, plus `tests/Atv.LogicTests` (fake-backed logic suite, runs anywhere with no package identity/API; Microsoft.Testing.Platform via `EnableMSTestRunner`). The real-API adapter suite is `tests/Atv.AdapterTests` (identity-carrying MTP exe, per-worktree test identity; registers via an MSBuild `_MTPBuild`/`_TestRunStart` hook, run serially). There are no lint steps.

## What this project is

A minimal C# CLI tool, brand "Agentaskvoid", binary `atv`, that wraps the experimental `Windows.UI.Shell.Tasks.AppTaskInfo` WinRT API, which drives persistent task entries shown as their own separate icons on the Windows 11 taskbar — grouped independently of, and not requiring, any running app window.

**This is not jump lists.** Jump lists are per-app right-click menus on a pinned icon; app tasks are distinct taskbar icons in their own group, created via a completely different API. If you catch yourself reasoning about this feature in terms of jump lists, stop — the mental model is wrong.

Commands (POC surface, superseded by `questions/usage-ergonomics/ERGO-27-consolidated-v1-command-surface.md` as the CLI framework lands in later phases): `create <title> [subtitle]`, `list`, `clear`.

## Brand parameterization

`src/Atv/Branding.cs` is the single source of truth for the brand (`Agentaskvoid`) and command name (`atv`). Every identity, command-name, env/config, mutex, and path string in the codebase must derive from these two constants — never re-literal them. `Directory.Build.props` extracts the same two values into MSBuild properties (`$(AtvBrandName)` / `$(AtvCommandName)`) by regexing `Branding.cs` at build time, so a rebrand is a one-file edit that also flows into `AssemblyName` and the AppxManifest identity stamp. See `plan/README.md`'s standing invariant #2 and `questions/usage-ergonomics/ERGO-18-shipped-command-name.md`.

## API reference

Before calling the `microsoft-learn` MCP tools for `Windows.UI.Shell.Tasks` types, check [`docs/windows-ui-shell-tasks/`](docs/windows-ui-shell-tasks/README.md) — a local, token-cheap reference for every class/member in the namespace, cross-checked against the generated CsWinRT projection and annotated with gotchas found by experimentation. Update it if you learn something new about the API's runtime behavior.

## Key constraints and non-obvious decisions

### Package identity: full-package MSIX model, not sparse

`AppTaskInfo` only works when the process has package identity. This project uses the **full-package identity model** (`questions/distribution/DIST-1-end-user-distribution-vehicle.md`): a loose-layout package in dev, a signed full MSIX at release. There is no sparse/external-location package, no `Register-Identity.ps1`, and no `<msix>` element in an `app.manifest` — that scaffolding was deliberately dropped (`questions/infrastructure/INFRA-17-dogfood-run-ergonomics-without-a-load-bearing-script.md`): sparse and full-package identity are mutually exclusive at the manifest level, and only the full-package model gets a zero-manual-step dev loop.

The manifest is `src/Atv/Package/AppxManifest.template.xml`. `Identity/@Version` (Nerdbank.GitVersioning's 4-part `$(BuildVersion)`) is always stamped the same way; every other field is static, hand-authored to match the brand — except `Identity/@Name` and the `AppExecutionAlias`'s `Alias`, which are **build-kind-aware** as of the 2026-07-10 DIST-3 amendment:

- **Dev (default — `dotnet build`/`dotnet run`/F5/`winapp run`, no special properties set):** Name = brand + a stable hash of the project directory (e.g. `Agentaskvoid-bbbb1168` in the primary worktree), alias `atv`. Unchanged from the original mechanism — this is the identity that has been driving real dogfooding (Checkpoint C1's taskbar render), so it must keep resolving to the same Name.
- **Release (`-p:AtvReleaseIdentity=true`, set automatically by `build/Atv.Release.targets`'s `-t:AtvRelease`):** a clean, pathhash-free Name = brand exactly (e.g. `Agentaskvoid`), alias `atv`. A shipped identity must not encode the developer's build-directory path.
- **The phase-12 throwaway smoke variant (`-p:AtvVerifyIdentity=true`, additionally set by `-t:AtvRelease -p:AtvVerifyIdentity=true`):** a distinct Name = brand + `-reltest` (e.g. `Agentaskvoid-reltest`), alias `atv-reltest`. Exists so a release-on-dev-box smoke install can coexist with the dev-interactive pool above without clobbering its `atv` alias or sharing its PFN.

Different Names produce different PFNs, which is what makes DIST-3's ("Dev vs release identity (PFN) divergence") three-pool isolation (release / dev-interactive / per-worktree test) structural, independent of the still-static Publisher (`CN=AppTaskInfoCli`, until the deferred DIST-2 real cert). The per-worktree TEST pool (`build/Atv.TestIdentity.targets`, a separate sibling template) is untouched by this amendment — it already stamped its own `<brand>.Test.<hash>` Name and per-worktree alias.

The stamping target is `build/Atv.Package.targets` (`AtvStampAppxManifest`), which writes the resolved manifest to `$(IntermediateOutputPath)AppxManifest.xml` (i.e. under `obj/`, RID-qualified for a RID-specific build/publish) — never checked into source. See `questions/distribution/DIST-7-release-full-package-manifest.md` and `questions/distribution/DIST-3-dev-vs-release-identity-pfn-divergence.md` for the full implementation rules (why the version/path computation must happen in the target's execution-time `PropertyGroup`, not the file body).

**The `(dev)`/`(test)` console/log marker:** `Atv.Diagnostics.BuildKindResolver` (`src/Atv/Diagnostics/BuildKind.cs`) classifies the CURRENT process's build kind from `Package.Current.Id.Name` (Release when it equals the brand exactly, Test when it starts with `<brand>.Test.`, Dev otherwise — including the `-reltest` variant) and renders an unambiguous `(dev)`/`(test)` marker (`null` for Release/no-identity — release output stays unmarked). Surfaced in `doctor`'s identity line, `--version`'s output, and every durable failure-log entry (a new trailing `buildKind` field on `LogEntry`, stamped once per process by the composition root) — so traces are always self-identifying, per the operator's "not ambiguous looking at lots [of logs], traces etc." request.

### Dev loop: `dotnet run` / F5, no manual pre-steps

`Microsoft.Windows.SDK.BuildTools.WinApp` (a build-time-only NuGet package, no runtime dependency) redirects `dotnet run` into `winapp run`, which loose-layout-registers the build output and launches with package identity — the same mechanism VS/VS Code F5 and `dotnet run` both go through. Prerequisite: **Developer Mode on** (Settings > Privacy & security > For developers) — a one-time machine setting, unrelated to end users (`doctor`, phase 10, will check it).

To pass CLI args through the redirect you need a **double `--`**: `dotnet run --project src/Atv -- -- <verb> [args]` (the first `--` ends `dotnet run`'s args, the second tells `winapp run` where the app's args begin). A single `--` makes `winapp` reject the verb with "Unrecognized argument". The packaged app's stdout does pipe back to the console, so `list --json`/`doctor` output is visible. Verified during the phase-10 taskbar dogfood (2026-07-10).

One real quirk: `winapp`'s own dev-run gate is evaluated at MSBuild *file-evaluation* time (before any target, including our stamping target, has run this invocation), and it requires the stamped manifest to already exist on disk. So the very first `dotnet run` in a totally fresh clone (no prior `obj/`) builds fine but launches as a plain unpackaged process (no identity) — the *next* `dotnet run` picks up the manifest that the first build's `Build` target just stamped and gets identity from then on. In practice this never matters: `dotnet build` (or any prior build) before you first `dotnet run` is normal, not a bespoke step. Full explanation in the comments on `WinAppManifestPath` in `src/Atv/Atv.csproj`.

`src/Atv/Properties/launchSettings.json` sets `ATV_WATCHDOG_MODE=off` (the brand-derived env var, resolved via `SettingsLoader.CurrentEnvVarName` — not a bare `WATCHDOG_MODE`) for the default profile, so F5 / Ctrl+F5 / `dotnet run` never spawn a detached watchdog. Two other profiles exist for watchdog work: "watchdog (foreground)" runs `atv watchdog` directly for breakpoints, and "app + spawn" sets `ATV_WATCHDOG_MODE=spawn` to exercise the real detached path (phase 09).

### NativeAOT release build

`dotnet publish -r win-x64 -c Release -p:PublishAot=true` (or `win-arm64`) produces a single self-contained native exe, no `Microsoft.WindowsAppSDK` runtime dependency. Size levers (`InvariantGlobalization`, `OptimizationPreference=Size`, disabled `StackTraceSupport`/`EventSourceSupport`/`DebuggerSupport`, `UseSystemResourceKeys`, no `.pdb` in Release) live in `Directory.Build.props`. Sub-1 MB is explicitly not a goal (`questions/infrastructure/INFRA-2-minimizing-the-on-disk-size-of-the-tool.md`).

Actual size climbed from ~2.6 MB (phase 01, minimal surface) to ~4.4–4.8 MB (phase 12, full CLI + watchdog + `run` wrapper + icon pipeline) as real functional surface (`System.Diagnostics.Process`, `Windows.ApplicationModel.StartupTask`, the icon-rendering D2D/DWrite interop) became reachable. Phase 12's trim investigation confirmed every documented lever is already at its maximum: `StackTraceSupport=false` already implies full method-body folding (`IlcFoldIdenticalMethodBodies` is redundant); `IlcGenerateCompleteTypeMetadata` already defaults to its smaller (unset) value; CsWinRT's `CsWinRTAotOptimizerEnabled`/`CsWinRTAotExportsEnabled` are already `true` by default for this TFM+`PublishAot` combination. Both JSON (all call sites use source-gen `JsonSerializerContext`s, never reflection-based `JsonSerializer.Serialize/Deserialize` overloads) and regex (`[GeneratedRegex]`, not the interpreted `Regex` constructor) are already AOT-safe by construction, so neither is a hidden bloat source. Extra feature-switch flags (`HttpActivityPropagationSupport`, `NullabilityInfoContextSupport`, `MetadataUpdaterSupport`, `CustomResourceTypesSupport`, etc.) measured **zero** byte difference (code paths already unreachable/trimmed); `CsWinRTMergeReferencedActivationFactories=true` broke the build outright (bad codegen against the `Atv.IconRendering` project reference) and was rejected. Further reduction would require extraordinary measures (e.g. `IlcDisableReflection`, risky against CsWinRT's interop marshalling) that INFRA-2 explicitly doesn't ask for; 3–5 MB was operator-accepted as fine.

A published AOT exe still has no identity by itself — to smoke-test it standalone, register + launch it with `winapp run <publish-output-folder> --manifest <RID-qualified obj/AppxManifest.xml> --with-alias`. For the full signed-MSIX release build, see "Release build (signed MSIX)" below.

On an ARM64 dev machine, publishing a non-native RID (e.g. `-r win-x64`) needs `vswhere.exe` on `PATH` for NativeAOT's cross-architecture native linking step (`Microsoft.DotNet.ILCompiler`'s targets shell out to it to locate the matching MSVC cross tools); it isn't on `PATH` by default even when Visual Studio is installed — add `...\Microsoft Visual Studio\Installer` for that session. Without it, `link.exe` fails with an opaque "filename, directory, or volume label syntax is incorrect" (exit 123). This also applies to a same-architecture (`win-arm64` on this machine) publish, not just cross-arch — `vswhere.exe` is needed regardless of RID.

### Release build (signed MSIX)

```
dotnet build src\Atv\Atv.csproj -t:AtvRelease
```

One command (`build/Atv.Release.targets`, phase 12): publishes NativeAOT `atv.exe` for both `win-x64` and `win-arm64`, packages each against its RID-qualified stamped manifest (`winapp package --manifest <obj-copy>`, explicit, never auto-detected — DIST-7), and signs both with a throwaway self-signed dev cert (`winapp cert generate` / `winapp sign`) into `artifacts\release\msix\*.msix` (gitignored). This stamps the clean **release** identity (Name = brand, alias `atv` — see "Package identity" above). Version comes from NBGV's `$(BuildVersion)`; re-running with nothing changed is a no-op repack (MSBuild Inputs/Outputs on the per-arch targets).

For the throwaway **smoke** variant that coexists with the dev-interactive pool (Name = brand + `-reltest`, alias `atv-reltest`), add `-p:AtvVerifyIdentity=true`:

```
dotnet build src\Atv\Atv.csproj -t:AtvRelease -p:AtvVerifyIdentity=true
```

Artifacts land under distinct filenames (`artifacts\release\msix\Agentaskvoid_<ver>_{x64,arm64}_reltest.msix`), so they never collide with, or falsely up-to-date-skip against, the real release msix — see `docs/release.md` §3 for the supervised install steps this variant is for.

Full runbook, the dev-cert-vs-real-cert distinction, and the supervised install/upgrade/uninstall verification steps: [`docs/release.md`](docs/release.md).

### CsWinRT projection

`Windows.UI.Shell.Tasks` is an **experimental** WinRT namespace (marked `[Experimental]`, requires `AppTaskContract` v2.0). It is present in the SDK's `UnionMetadata\<version>\Windows.winmd` but **not** in the system WinMetadata or the default `CsWinRTWindowsMetadata` lookup targets. `src/Atv/Atv.csproj` explicitly references the SDK UnionMetadata WinMD via `CsWinRTInputs`:

```xml
<CsWinRTInputs Include="$(MSBuildProgramFiles32)\Windows Kits\10\UnionMetadata\$(TargetPlatformVersion)\Windows.winmd" />
```

`CsWinRTWindowsMetadata=sdk` or a version number both resolve to `%WinDir%\System32\WinMetadata\` (system) rather than the SDK UnionMetadata, so they don't work for experimental types. The `CsWinRTInputs` approach is the correct one.

Generated projection files land in `obj/.../Generated Files/CsWinRT/` at build time — never committed. Spike-verified (2026-07-04): NativeAOT + this projection builds, runs, and drives the API (create/list/clear) with zero trim/CsWinRT warnings.

### API availability

`AppTaskInfo.IsSupported()` may throw `COMException (CLASS_E_CLASSNOTAVAILABLE)` on some Windows 11 builds even when the package identity is correct, because the API isn't in the WinRT activation registry on all builds. The code wraps `IsSupported()` in a try/catch for this reason. The API requires Windows 11 26100+ with a build that has the activation entry registered.
