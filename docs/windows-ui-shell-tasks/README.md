# Windows.UI.Shell.Tasks — Local API Reference

Local reference for the experimental `Windows.UI.Shell.Tasks` WinRT namespace, distilled from Microsoft Learn and cross-checked against the SDK's generated CsWinRT projection, plus notes learned by experimentation while building this CLI. Read these files instead of calling the `microsoft-learn` MCP tools for routine lookups — only fall back to the MCP server for things not covered here, or to re-verify if a signature looks like it drifted (this is an experimental namespace and can still change between SDK releases).

## Contents

- [AppTaskInfo.md](AppTaskInfo.md) — the task itself: create, update, remove, enumerate
- [AppTaskContent.md](AppTaskContent.md) — the visual content shown for a task
- [AppTaskResultAsset.md](AppTaskResultAsset.md) — a generated-asset reference used inside content
- [AppTaskState.md](AppTaskState.md) — the state enum

## This is not jump lists

`Windows.UI.Shell.Tasks` has nothing to do with jump lists (`Windows.UI.StartScreen.JumpList` / the per-app right-click menu on a pinned taskbar icon). App tasks are:

- **Separate taskbar icons**, shown in their own group — independent of, and separate from, any running-app icon/window.
- **Independent of the creating app's lifetime.** A task stays visible on the taskbar without the app that created it running — no relaunch needed to see it.
- **Persisted** across app sessions *and* reboots/user sessions (see [how this works](#how-this-actually-works), below).

If you catch yourself reasoning about this in terms of jump lists, stop — the mental model is wrong.

## How this actually works (under the hood)

- The WinRT API surface (`AppTaskInfo` etc.) is backed by `OSClient.API.dll`, which loads into the calling app's own process.
- Calls to `Create`/`Update`/`UpdateState`/`UpdateTitles`/`UpdateDeepLink`/`Remove` write a `tasks.json` file to `%LocalAppData%\Packages\<package identity>\SystemAppData\AppTasks\`. This file is directly inspectable — useful for debugging without needing a second app or a debugger attached.
- The taskbar itself runs inside `explorer.exe`, which watches that folder/file for changes and picks up updates live. There's no IPC call to the taskbar from the app directly — the file is the interface.
- This explains why tasks persist across reboots and don't need the app running: the taskbar reads state from disk, not from a live connection to the app.

## Namespace essentials

- Experimental namespace (`[Experimental]` on every type/member), requires `Windows.UI.Shell.Tasks.AppTaskContract`. Present in the SDK's `UnionMetadata\<version>\Windows.winmd`, not in system WinMetadata — see root [`CLAUDE.md`](../../CLAUDE.md) for how the csproj resolves this via `CsWinRTInputs`.
- Device family: **Windows Desktop Extension SDK**, introduced `10.0.26100.0`.
- All 3 classes (`AppTaskInfo`, `AppTaskContent`, `AppTaskResultAsset`) are `sealed`, `MarshalingBehavior.Agile`, `Threading.Both`.
- **Rollout note** (MS docs, page last updated 2026-04-24): *"App task support will start gradually rolling out to Windows 11 starting May, 2026."* This is almost certainly why `IsSupported()` can still throw `COMException(CLASS_E_CLASSNOTAVAILABLE)` on an otherwise-correct Windows 11 26100+ build with valid package identity — the activation registration itself is being staged, not just gated by a version check.
- **Two contract versions are actually in play**, even though the docs list a single "v2.0" requirement for `AppTaskInfo`:
  - **v1.0** (`ContractVersion` value `65536`): `AppTaskContent`, `AppTaskResultAsset`, `AppTaskState`, and most of `AppTaskInfo` — `Title`, `Subtitle`, `State`, `StartTime`, `EndTime`, `DeepLink`, `IconUri`, `Remove`, `Update`, `UpdateState`, `UpdateTitles`, `GetCompletedSteps`, `GetExecutingStep`, `IsSupported`, `FindAll`, `Create`.
  - **v2.0** (`131072`): `AppTaskInfo.Id`, `AppTaskInfo.HiddenByUser`, `AppTaskInfo.UpdateDeepLink`. Internally these come from a second interface, `IAppTaskInfo2`, that's invisible in the public docs (they're listed as plain `AppTaskInfo` members) but visible in the generated projection.
  - `AppTaskContract` itself is just an empty marker enum used for `[ContractVersion]` attributes — no members worth documenting separately.

### Packaging requirement

Requires a packaged app (this project uses a sparse package — see `CLAUDE.md`) **and** a `com.microsoft.apptaskprovider` AppExtension declared in the manifest:

```xml
<uap3:Extension Category="windows.appExtension">
  <uap3:AppExtension
    Name="com.microsoft.apptaskprovider"
    PublicFolder="Public"
    Id="MyApp.AppTaskProvider"
    DisplayName="AppTaskProvider for MyApp"/>
</uap3:Extension>
```

✅ Already present in `identity/AppxManifest.xml` in this repo.

### URI formats accepted by icon/asset parameters

Documented for `AppTaskInfo.IconUri`, and observed to apply to the equivalent parameters on `AppTaskContent.CreatePreviewThumbnail` and `AppTaskResultAsset`:

- `ms-appx:///...` — package-relative (recommended)
- `ms-appdata:///...` — app data folder
- Absolute path, e.g. `C:\...\icon.png` or `file://c:/temp/icon.png`

### Local gotchas (learned by experimentation, not documented by MS)

- `AppTaskInfo.Create`'s `deepLink` and `iconUri` parameters are typed as (nullable-looking) `Uri` in the projection, but the native implementation dereferences both unconditionally (`ResolveIconPath`, `CreateJsonObject` internally) — passing `null` for either throws. Always pass a real `Uri`, even a placeholder. See `Program.cs`.
- `AppTaskInfo.IsSupported()` can throw instead of returning `false` — always wrap in try/catch (see rollout note above).
- When `HiddenByUser` is `true`, the system does **not** remove the task on its own — the creating app must notice this (e.g. on the next `FindAll()`) and call `Remove()` itself, or the hidden entry lingers forever. See [AppTaskInfo.md](AppTaskInfo.md).
- For debugging, `%LocalAppData%\Packages\<package identity>\SystemAppData\AppTasks\tasks.json` can be inspected directly to see exactly what state the taskbar (`explorer.exe`) is reading — no need to add app-side logging just to check what got persisted. See [how this actually works](#how-this-actually-works-under-the-hood).

---
Sourced from `https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.tasks` (moniker `winrt-28000`, page updated 2026-04-24) via the `microsoft-learn` MCP server, cross-checked against `obj/Debug/net10.0-windows10.0.26100.0/Generated Files/CsWinRT/Windows.UI.Shell.Tasks.cs`. Fetched 2026-07-01.
