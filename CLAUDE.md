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

The manifest is `src/Atv/Package/AppxManifest.template.xml`. Only `Identity/@Name` (brand + a stable hash of the project directory) and `Identity/@Version` (Nerdbank.GitVersioning's 4-part `$(BuildVersion)`) are stamped at build time; every other field is static, hand-authored to match the brand. The stamping target is `build/Atv.Package.targets` (`AtvStampAppxManifest`), which writes the resolved manifest to `$(IntermediateOutputPath)AppxManifest.xml` (i.e. under `obj/`, RID-qualified for a RID-specific build/publish) — never checked into source. See `questions/distribution/DIST-7-release-full-package-manifest.md` for the full implementation rules (why the version/path computation must happen in the target's execution-time `PropertyGroup`, not the file body).

### Dev loop: `dotnet run` / F5, no manual pre-steps

`Microsoft.Windows.SDK.BuildTools.WinApp` (a build-time-only NuGet package, no runtime dependency) redirects `dotnet run` into `winapp run`, which loose-layout-registers the build output and launches with package identity — the same mechanism VS/VS Code F5 and `dotnet run` both go through. Prerequisite: **Developer Mode on** (Settings > Privacy & security > For developers) — a one-time machine setting, unrelated to end users (`doctor`, phase 10, will check it).

One real quirk: `winapp`'s own dev-run gate is evaluated at MSBuild *file-evaluation* time (before any target, including our stamping target, has run this invocation), and it requires the stamped manifest to already exist on disk. So the very first `dotnet run` in a totally fresh clone (no prior `obj/`) builds fine but launches as a plain unpackaged process (no identity) — the *next* `dotnet run` picks up the manifest that the first build's `Build` target just stamped and gets identity from then on. In practice this never matters: `dotnet build` (or any prior build) before you first `dotnet run` is normal, not a bespoke step. Full explanation in the comments on `WinAppManifestPath` in `src/Atv/Atv.csproj`.

`src/Atv/Properties/launchSettings.json` sets `ATV_WATCHDOG_MODE=off` (the brand-derived env var, resolved via `SettingsLoader.CurrentEnvVarName` — not a bare `WATCHDOG_MODE`) for the default profile, so F5 / Ctrl+F5 / `dotnet run` never spawn a detached watchdog. Two other profiles exist for watchdog work: "watchdog (foreground)" runs `atv watchdog` directly for breakpoints, and "app + spawn" sets `ATV_WATCHDOG_MODE=spawn` to exercise the real detached path (phase 09).

### NativeAOT release build

`dotnet publish -r win-x64 -c Release -p:PublishAot=true` (or `win-arm64`) produces a single self-contained native exe, ~3.0–3.5 MB, no `Microsoft.WindowsAppSDK` runtime dependency. Size levers (`InvariantGlobalization`, `OptimizationPreference=Size`, disabled `StackTraceSupport`/`EventSourceSupport`/`DebuggerSupport`, `UseSystemResourceKeys`, no `.pdb` in Release) live in `Directory.Build.props`. Sub-1 MB is explicitly not a goal (`questions/infrastructure/INFRA-2-minimizing-the-on-disk-size-of-the-tool.md`).

A published AOT exe still has no identity by itself — to smoke-test it, register + launch it with `winapp run <publish-output-folder> --manifest <RID-qualified obj/AppxManifest.xml> --with-alias`. Full release packaging (`winapp package` / `winapp sign` / winget) is phase 12, out of scope here.

On an ARM64 dev machine, publishing a non-native RID (e.g. `-r win-x64`) needs `vswhere.exe` on `PATH` for NativeAOT's cross-architecture native linking step (`Microsoft.DotNet.ILCompiler`'s targets shell out to it to locate the matching MSVC cross tools); it isn't on `PATH` by default even when Visual Studio is installed — add `...\Microsoft Visual Studio\Installer` for that session. Without it, `link.exe` fails with an opaque "filename, directory, or volume label syntax is incorrect" (exit 123).

### CsWinRT projection

`Windows.UI.Shell.Tasks` is an **experimental** WinRT namespace (marked `[Experimental]`, requires `AppTaskContract` v2.0). It is present in the SDK's `UnionMetadata\<version>\Windows.winmd` but **not** in the system WinMetadata or the default `CsWinRTWindowsMetadata` lookup targets. `src/Atv/Atv.csproj` explicitly references the SDK UnionMetadata WinMD via `CsWinRTInputs`:

```xml
<CsWinRTInputs Include="$(MSBuildProgramFiles32)\Windows Kits\10\UnionMetadata\$(TargetPlatformVersion)\Windows.winmd" />
```

`CsWinRTWindowsMetadata=sdk` or a version number both resolve to `%WinDir%\System32\WinMetadata\` (system) rather than the SDK UnionMetadata, so they don't work for experimental types. The `CsWinRTInputs` approach is the correct one.

Generated projection files land in `obj/.../Generated Files/CsWinRT/` at build time — never committed. Spike-verified (2026-07-04): NativeAOT + this projection builds, runs, and drives the API (create/list/clear) with zero trim/CsWinRT warnings.

### API availability

`AppTaskInfo.IsSupported()` may throw `COMException (CLASS_E_CLASSNOTAVAILABLE)` on some Windows 11 builds even when the package identity is correct, because the API isn't in the WinRT activation registry on all builds. The code wraps `IsSupported()` in a try/catch for this reason. The API requires Windows 11 26100+ with a build that has the activation entry registered.
