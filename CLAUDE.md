# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

```
dotnet build
```

Solution: `AppTaskInfoCli.slnx` -> `src/Atv/Atv.csproj`, plus `tests/Atv.LogicTests` (fake-backed logic suite, runs anywhere with no package identity/API; Microsoft.Testing.Platform via `EnableMSTestRunner`). The real-API adapter suite is `tests/Atv.AdapterTests` (identity-carrying MTP exe, per-worktree test identity; registers via an MSBuild `_MTPBuild`/`_TestRunStart` hook, run serially). There are no lint steps.

## What this project is

A minimal C# CLI tool, brand "Agent Task Void", binary `atv`, that wraps the experimental `Windows.UI.Shell.Tasks.AppTaskInfo` WinRT API, which drives persistent task entries shown as their own separate icons on the Windows 11 taskbar — grouped independently of, and not requiring, any running app window.

**This is not jump lists.** Jump lists are per-app right-click menus on a pinned icon; app tasks are distinct taskbar icons in their own group, created via a completely different API. If you catch yourself reasoning about this feature in terms of jump lists, stop — the mental model is wrong.

The current command surface is documented in [`README.md`](README.md)'s Manual usage section.

## Brand parameterization

`src/Atv/Branding.cs` is the single source of truth for the package identity name (`Codevoid.AgentTaskVoid`), the display name (`Agent Task Void`), and the command name (`atv`). Every identity, command-name, env/config, mutex, path, and display string in the codebase must derive from these three constants — never re-literal them. `Directory.Build.props` extracts `IdentityName` and `Command` into MSBuild properties (`$(AtvBrandName)` / `$(AtvCommandName)`) by regexing `Branding.cs` at build time, so a rebrand is a one-file edit that also flows into `AssemblyName` and the AppxManifest identity stamp; the manifest templates' display fields are hand-authored to match `DisplayName` (DIST-7). See [`plan/README.md`](plan/README.md)'s standing invariant #2 and ERGO-18.

## API reference

Before calling the `microsoft-learn` MCP tools for `Windows.UI.Shell.Tasks` types, check [`docs/windows-ui-shell-tasks/`](docs/windows-ui-shell-tasks/README.md) — a local, token-cheap reference for every class/member in the namespace, cross-checked against the generated CsWinRT projection and annotated with gotchas found by experimentation. Update it if you learn something new about the API's runtime behavior.

## Key constraints and non-obvious decisions

### Package identity: full-package MSIX model, not sparse

`AppTaskInfo` only works when the process has package identity. This project uses the full-package identity model (DIST-1): a loose-layout package in dev, a signed full MSIX at release. There is no sparse/external-location package, no `Register-Identity.ps1`, and no `<msix>` element in an `app.manifest` (INFRA-17) — sparse and full-package identity are mutually exclusive at the manifest level, and only the full-package model gets a zero-manual-step dev loop.

The manifest is `src/Atv/Package/AppxManifest.template.xml`. `Identity/@Version` (Nerdbank.GitVersioning's 4-part `$(BuildVersion)`) is always stamped the same way; every other field is static, hand-authored to match the brand — except `Identity/@Name`, the `AppExecutionAlias`'s `Alias`, and (DIST-14) the `AppTaskProvider` extension's `Id`, which are build-kind-aware (DIST-3, amended by DIST-12 and DIST-14). The extension `Id` is always stamped to the same value as `Identity/@Name` — the template reuses the same `{IdentityName}` token, so there is no separate computation to keep in sync:

- **Dev (default — `dotnet build`/`dotnet run`/F5/`winapp run`, no special properties set):** Name = the identity name + a stable hash of the project directory (e.g. `Codevoid.AgentTaskVoid-bbbb1168` in the primary worktree), alias `atv-dev`. This is the identity driving real dogfood use, so the hash computation must keep resolving to the same Name.
- **Release** (`-p:AtvReleaseIdentity=true`; `-t:AtvRelease` sets this automatically): a clean, pathhash-free Name = the identity name exactly (e.g. `Codevoid.AgentTaskVoid`), alias `atv`. This backs the operator's own daily taskbar card use, installed on the dev box the same way an end user installs it. A shipped identity must not encode the developer's build-directory path.
- **The `-reltest` throwaway smoke variant** (`-p:AtvVerifyIdentity=true`, via `-t:AtvRelease -p:AtvVerifyIdentity=true`): a distinct Name = the identity name + `-reltest` (e.g. `Codevoid.AgentTaskVoid-reltest`), alias `atv-reltest`. Exists so a release-on-dev-box smoke install can coexist with the dev and retail pools without clobbering either alias or sharing either PFN.
- **Per-worktree test** (`tests/Atv.AdapterTests`, via `build/Atv.TestIdentity.targets`, a separate sibling template): Name = the identity name + `.Test.` + a stable hash of the test project's directory, alias `<command>-test-<hash>`.

Different Names produce different PFNs, which makes each pool's package state (app-data, `tasks.json`, the write mutex) structural, independent of the still-static Publisher (`CN=AppTaskInfoCli`, pending DIST-2's real cert). PFN divergence isolates that state; it does not by itself isolate the `AppTaskInfo` provider registration (the manifest's `com.microsoft.apptaskprovider` `uap3:AppExtension`), which needed its own per-pool fix — see DIST-14 and [`docs/windows-ui-shell-tasks/README.md`](docs/windows-ui-shell-tasks/README.md).

**Bare `atv` is the operator's live retail install.** In-repo and agent work targets `atv-dev` or the translator test stub, and never drives bare `atv` — no claim or state-changing verb against it, ever. Read-only `atv doctor` / `atv --version` to inspect the retail install are fine.

The stamping target is `build/Atv.Package.targets` (`AtvStampAppxManifest`), which writes the resolved manifest to `$(IntermediateOutputPath)AppxManifest.xml` (i.e. under `obj/`, RID-qualified for a RID-specific build/publish) — never checked into source. See DIST-7 and DIST-12 for the full implementation rules, including why the version/path computation must happen in the target's execution-time `PropertyGroup`, not the file body.

**The `(dev)`/`(test)` console/log marker:** `Codevoid.AgentTaskVoid.Diagnostics.BuildKindResolver` (`src/Atv/Diagnostics/BuildKind.cs`) classifies the current process's build kind from `Package.Current.Id.Name` (Release when it equals the identity name exactly, Test when it starts with `<identity-name>.Test.`, Dev otherwise — including the `-reltest` variant) and renders a `(dev)`/`(test)` marker (`null` for Release/no-identity — release output stays unmarked). Surfaced in `doctor`'s identity line, `--version`'s output, and every durable failure-log entry (a trailing `buildKind` field on `LogEntry` that the composition root stamps once per process), so traces are self-identifying. The classifier reads only the Name, so the alias each pool claims is not an input to it.

### Dev loop: `dotnet run` / F5, no manual pre-steps

`Microsoft.Windows.SDK.BuildTools.WinApp` (a build-time-only NuGet package, no runtime dependency) redirects `dotnet run` into `winapp run`, which loose-layout-registers the build output and launches with package identity — the same mechanism VS/VS Code F5 and `dotnet run` both go through. Prerequisite: Developer Mode on (Settings > Privacy & security > For developers) — a one-time machine setting, unrelated to end users (`doctor` checks it).

To pass CLI args through the redirect you need a double `--`: `dotnet run --project src/Atv -- -- <verb> [args]` (the first `--` ends `dotnet run`'s args, the second tells `winapp run` where the app's args begin). A single `--` makes `winapp` reject the verb with "Unrecognized argument". The packaged app's stdout pipes back to the console, so `list --json`/`doctor` output is visible. Verified in a live taskbar dogfood, 2026-07-10.

Validate a change by running it through `dotnet run`, not a bare alias. Each `dotnet run` re-registers the build output (above), so you launch the build you just made. A bare alias launches whatever was last registered — a plain `dotnet build` does not re-register — so `atv-dev` can run a stale build, and bare `atv` reaches the retail install driving the operator's real taskbar cards. `atv-dev doctor` reports the registered build's version and `atv-dev --version` the compiled one; they diverge when the registration is stale.

For a behavior check that does not need the taskbar, run `tests/Atv.LogicTests` (the fake-backed suite — no identity, no registration).

One quirk: `winapp`'s own dev-run gate is evaluated at MSBuild *file-evaluation* time (before any target, including our stamping target, has run this invocation), and it requires the stamped manifest to already exist on disk. So the very first `dotnet run` in a totally fresh clone (no prior `obj/`) builds fine but launches as a plain unpackaged process (no identity) — the *next* `dotnet run` picks up the manifest that the first build's `Build` target just stamped and gets identity from then on. In practice this never matters: `dotnet build` (or any prior build) before you first `dotnet run` is normal, not a bespoke step. Full explanation in the comments on `WinAppManifestPath` in `src/Atv/Atv.csproj`.

`src/Atv/Properties/launchSettings.json` sets `ATV_WATCHDOG_MODE=off` (the brand-derived env var, resolved via `SettingsLoader.CurrentEnvVarName` — not a bare `WATCHDOG_MODE`) for the default profile, so F5 / Ctrl+F5 / `dotnet run` never spawn a detached watchdog. Two other profiles exist for watchdog work: "watchdog (foreground)" runs `atv watchdog` directly for breakpoints, and "app + spawn" sets `ATV_WATCHDOG_MODE=spawn` to exercise the real detached path.

### What the common commands change on the machine

| Command | Effect |
|---|---|
| `dotnet build` | Nothing installed — compiles, restamps the `obj/` manifest. |
| `dotnet test` on `tests/Atv.LogicTests` | Nothing installed — fake-backed, no identity. |
| `dotnet test` on `tests/Atv.AdapterTests` | Registers a per-worktree test package (its own Name and alias). |
| `dotnet run` / `winapp run` | Registers (installs) the working copy and puts its alias on PATH. |
| A registered alias + a state-changing verb (`working`, `activity`, `start`, …) | Writes app-data (sidecar, `tasks.json`), renders taskbar cards, may spawn the watchdog. |
| A registered alias + `doctor` / `list` / `--version` | Reads only. |

`Remove-AppxPackage` on a registered package removes it and drops its taskbar cards and app-data (DIST-9).

### NativeAOT release build

`dotnet publish -r win-x64 -c Release -p:PublishAot=true` (or `win-arm64`) produces a single self-contained native exe, no `Microsoft.WindowsAppSDK` runtime dependency. Size levers (`InvariantGlobalization`, `OptimizationPreference=Size`, disabled `StackTraceSupport`/`EventSourceSupport`/`DebuggerSupport`, `UseSystemResourceKeys`, no `.pdb` in Release) live in `Directory.Build.props`. Sub-1 MB is not a goal (INFRA-2).

The published binary is about 4.4–4.8 MB, reflecting real functional surface (`System.Diagnostics.Process`, `Windows.ApplicationModel.StartupTask`, the icon-rendering D2D/DWrite interop). Every documented size lever is already at its maximum: `StackTraceSupport=false` implies full method-body folding, so `IlcFoldIdenticalMethodBodies` is redundant; `IlcGenerateCompleteTypeMetadata` defaults to its smaller (unset) value; CsWinRT's `CsWinRTAotOptimizerEnabled`/`CsWinRTAotExportsEnabled` default to `true` for this TFM+`PublishAot` combination. JSON (source-gen `JsonSerializerContext`s only, never reflection-based `JsonSerializer.Serialize/Deserialize`) and regex (`[GeneratedRegex]`, not the interpreted `Regex` constructor) are AOT-safe by construction, so neither is a hidden bloat source. Extra feature-switch flags (`HttpActivityPropagationSupport`, `NullabilityInfoContextSupport`, `MetadataUpdaterSupport`, `CustomResourceTypesSupport`, etc.) make zero byte difference — the code paths are already unreachable/trimmed. `CsWinRTMergeReferencedActivationFactories=true` breaks the build (bad codegen against the `Atv.IconRendering` project reference), so it stays off. Further reduction would need extraordinary measures (e.g. `IlcDisableReflection`, risky against CsWinRT's interop marshalling) that INFRA-2 doesn't ask for; 3–5 MB is fine.

A published AOT exe still has no identity by itself — to smoke-test it standalone, register + launch it with `winapp run <publish-output-folder> --manifest <RID-qualified obj/AppxManifest.xml> --with-alias`. For the full signed-MSIX release build, see "Release build (signed MSIX)" below.

On an ARM64 dev machine, publishing a non-native RID (e.g. `-r win-x64`) needs `vswhere.exe` on `PATH`, for NativeAOT's cross-architecture native linking step (`Microsoft.DotNet.ILCompiler`'s targets shell out to it to locate the matching MSVC cross tools). It isn't on `PATH` by default even when Visual Studio is installed — add `...\Microsoft Visual Studio\Installer` for that session. Without it, `link.exe` fails with an opaque "filename, directory, or volume label syntax is incorrect" (exit 123). This also applies to a same-architecture (`win-arm64`) publish, not just cross-arch — `vswhere.exe` is needed regardless of RID.

### Release build (signed MSIX)

```
dotnet build src\Atv\Atv.csproj -t:AtvRelease
```

One command (`build/Atv.Release.targets`): publishes NativeAOT `atv.exe` for both `win-x64` and `win-arm64`, packages each against its RID-qualified stamped manifest (`winapp package --manifest <obj-copy>`, explicit, never auto-detected — DIST-7), and signs both with a throwaway self-signed dev cert (`winapp cert generate` / `winapp sign`) into `artifacts\release\msix\*.msix` (gitignored). This stamps the release identity (Name = brand, alias `atv` — see "Package identity" above). Version comes from NBGV's `$(BuildVersion)`; re-running with nothing changed is a no-op repack (MSBuild Inputs/Outputs on the per-arch targets).

For the throwaway smoke variant that coexists with the dev-interactive pool (Name = brand + `-reltest`, alias `atv-reltest`), add `-p:AtvVerifyIdentity=true`:

```
dotnet build src\Atv\Atv.csproj -t:AtvRelease -p:AtvVerifyIdentity=true
```

Artifacts land under distinct filenames (`artifacts\release\msix\Codevoid.AgentTaskVoid_<ver>_{x64,arm64}_reltest.msix`), so they never collide with, or up-to-date-skip against, the real release msix — see [`docs/release.md`](docs/release.md) §3 for the supervised install steps this variant is for.

Full runbook, the dev-cert-vs-real-cert distinction, and the supervised install/upgrade/uninstall verification steps: [`docs/release.md`](docs/release.md).

### CsWinRT projection

`Windows.UI.Shell.Tasks` is an experimental WinRT namespace (marked `[Experimental]`, requires `AppTaskContract` v2.0). It is present in the SDK's `UnionMetadata\<version>\Windows.winmd` but is not in the system WinMetadata or the default `CsWinRTWindowsMetadata` lookup targets. `src/Atv/Atv.csproj` references the SDK UnionMetadata WinMD via `CsWinRTInputs`:

```xml
<CsWinRTInputs Include="$(MSBuildProgramFiles32)\Windows Kits\10\UnionMetadata\$(TargetPlatformVersion)\Windows.winmd" />
```

`CsWinRTWindowsMetadata=sdk` or a version number both resolve to `%WinDir%\System32\WinMetadata\` (system) rather than the SDK UnionMetadata, so they don't work for experimental types — use `CsWinRTInputs` instead.

Generated projection files land in `obj/.../Generated Files/CsWinRT/` at build time — never committed. Spike-verified (2026-07-04): NativeAOT + this projection builds, runs, and drives the API end to end (creating, listing, and clearing task entries) with zero trim/CsWinRT warnings.

### API availability

`AppTaskInfo.IsSupported()` may throw `COMException (CLASS_E_CLASSNOTAVAILABLE)` on some Windows 11 builds even when the package identity is correct, because the API isn't in the WinRT activation registry on all builds. The code wraps `IsSupported()` in a try/catch for this reason. The API requires Windows 11 26100+ with a build that has the activation entry registered.
