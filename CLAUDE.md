# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

```
dotnet build
```

No test project exists. There are no lint steps.

## What this project is

A minimal C# CLI tool that wraps the experimental `Windows.UI.Shell.Tasks.AppTaskInfo` WinRT API, which drives persistent task entries shown as their own separate icons on the Windows 11 taskbar — grouped independently of, and not requiring, any running app window.

**This is not jump lists.** Jump lists are per-app right-click menus on a pinned icon; app tasks are distinct taskbar icons in their own group, created via a completely different API. If you catch yourself reasoning about this feature in terms of jump lists, stop — the mental model is wrong.

Commands: `create <title> [subtitle]`, `list`, `clear`

## API reference

Before calling the `microsoft-learn` MCP tools for `Windows.UI.Shell.Tasks` types, check [`docs/windows-ui-shell-tasks/`](docs/windows-ui-shell-tasks/README.md) — a local, token-cheap reference for every class/member in the namespace, cross-checked against the generated CsWinRT projection and annotated with gotchas found by experimentation. Update it if you learn something new about the API's runtime behavior.

## Key constraints and non-obvious decisions

### Package identity is required
`AppTaskInfo` only works when the process has package identity. This project uses a **sparse package** (external location, no MSIX packaging, no signing required for dev/sideload). The identity is registered once via `Register-Identity.ps1`; the `app.manifest` links the exe to it at runtime via the `<msix>` element.

### CsWinRT projection
`Windows.UI.Shell.Tasks` is an **experimental** WinRT namespace (marked `[Experimental]`, requires `AppTaskContract` v2.0). It is present in the SDK's `UnionMetadata\<version>\Windows.winmd` but **not** in the system WinMetadata or the default `CsWinRTWindowsMetadata` lookup targets. The csproj explicitly references the SDK UnionMetadata WinMD via `CsWinRTInputs`:

```xml
<CsWinRTInputs Include="$(MSBuildProgramFiles32)\Windows Kits\10\UnionMetadata\$(TargetPlatformVersion)\Windows.winmd" />
```

`CsWinRTWindowsMetadata=sdk` or a version number both resolve to `%WinDir%\System32\WinMetadata\` (system) rather than the SDK UnionMetadata, so they don't work for experimental types. The `CsWinRTInputs` approach is the correct one.

Generated projection files land in `obj/.../Generated Files/CsWinRT/` at build time — never committed.

### API availability
`AppTaskInfo.IsSupported()` may throw `COMException (CLASS_E_CLASSNOTAVAILABLE)` on some Windows 11 builds even when the package identity is correct, because the API isn't in the WinRT activation registry on all builds. The code wraps `IsSupported()` in a try/catch for this reason. The API requires Windows 11 26100+ with a build that has the activation entry registered.

## Identity registration (one-time setup per machine)

```powershell
# Register (run once after build, re-run if identity\AppxManifest.xml changes)
.\Register-Identity.ps1

# Verify
Get-AppxPackage AppTaskInfoCli

# Unregister
.\Unregister-Identity.ps1
```

`Register-Identity.ps1` points at `bin\Debug\net10.0-windows10.0.26100.0`. Update the path if changing TFM or build configuration.
