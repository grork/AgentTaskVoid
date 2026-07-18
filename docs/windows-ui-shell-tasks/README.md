# Windows.UI.Shell.Tasks — Local API Reference

Local reference for the experimental `Windows.UI.Shell.Tasks` WinRT namespace, from Microsoft Learn, the SDK's generated CsWinRT projection, and experimentation while building this CLI. Prefer these files over the `microsoft-learn` MCP tools for routine lookups; fall back to the MCP server only for what isn't covered here, or to re-verify a signature (this is an experimental namespace and can change between SDK releases).

## Contents

- [AppTaskInfo.md](AppTaskInfo.md) — the task itself: create, update, remove, enumerate
- [AppTaskContent.md](AppTaskContent.md) — the visual content shown for a task
- [AppTaskResultAsset.md](AppTaskResultAsset.md) — a generated-asset reference used inside content
- [AppTaskState.md](AppTaskState.md) — the state enum
- [state-content-compatibility.md](state-content-compatibility.md) — which `AppTaskState`/`AppTaskContent` combinations render, render blank, or crash the Shell

## This is not jump lists

`Windows.UI.Shell.Tasks` has nothing to do with jump lists (`Windows.UI.StartScreen.JumpList` / the per-app right-click menu on a pinned taskbar icon). App tasks are:

- **Separate taskbar icons**, shown in their own group — independent of any running-app icon/window.
- **Independent of the creating app's lifetime.** A task stays visible without the creating app running.
- **Persisted** across app sessions and reboots/user sessions (see [How it works](#how-it-works)).

## How it works

- `OSClient.API.dll` backs the WinRT API surface (`AppTaskInfo` etc.) and loads into the calling app's own process.
- `Create`/`Update`/`UpdateState`/`UpdateTitles`/`UpdateDeepLink`/`Remove` write a `tasks.json` file to `%LocalAppData%\Packages\<package identity>\SystemAppData\AppTasks\`. This file is directly inspectable — useful for debugging without a second app or an attached debugger. Verified empirically (phase 03, 2026-07-08): `<package identity>` here is the Package Family Name (`Package.Current.Id.FamilyName`, e.g. `Agentaskvoid-bbbb1168_016qghrny08mj`) — not the Package Full Name (which additionally embeds version/architecture, e.g. `..._0.1.0.39392_arm64__016qghrny08mj`). Using the full name looks up a nonexistent folder.
- On-disk task shape, verified against a real `Create()` (phase 03): `{"tasks":[{"id":"{guid}","title":"...","subtitle":"...","deepLink":"...","iconPath":"C:\\...\\Square44x44Logo.png","taskState":0,"timestamp":<FILETIME>,"startTime":<FILETIME>,"hiddenByUser":false,"dataJson":{"template":"SequenceOfSteps","data":{"completedSteps":[],"currentStep":"..."}}}],"version":"1.0"}`. Two details: `Id` on disk is brace-wrapped GUID text (e.g. `{f54a4c66-...}`) — still opaque per the platform's own contract, not a format to depend on; and `iconPath` (not `iconUri`) holds the platform's resolved absolute local file path, not the `ms-appx:///...` URI that was passed to `Create`.
- The taskbar runs inside `explorer.exe`, which watches that folder for changes and picks up updates live. There is no IPC from the app to the taskbar — the file is the interface.
- Hence tasks persist across reboots and don't need the app running: the taskbar reads state from disk, not from a live connection to the app.

## Concurrency: writes are not serialized across processes

Empirically (Windows 11 26100, 2026-07-02), `OSClient.API.dll` does not serialize writes to `tasks.json` across processes. Each `Create`/`Update`/`Remove` reads the whole file, mutates, and rewrites it, with no cross-process lock — so concurrent writers clobber each other — last-writer-wins on the whole file. A stress test of 4 processes each creating 100 tasks against one identity kept only **37 of 400** — ~91% silent lost writes (every call returned success). The file is not corrupted (it stays valid JSON); writes are lost. Because the contention is the whole file, even writes to *different* tasks collide. Any tool brokering multiple concurrent callers must wrap every write in its own cross-process lock — a system-wide named mutex scoped to the package identity fixes it (the same test then kept 400/400).

## Taskbar grouping mechanics

Undocumented Shell behavior for this build — not a contract, and it may change. Covers how multiple `AppTaskInfo` entries render together on the taskbar.

**Grouping into one taskbar icon is keyed by `IconUri`, not `Title`** — directly contradicting the MS docs remark on [`AppTaskInfo.Title`](AppTaskInfo.md) ("Tasks are grouped based on the title").
- Tasks sharing the exact same `iconUri` merge under one taskbar icon regardless of title — each still renders as its own card/row inside that icon's flyout.
- Tasks with a *different* `iconUri` get their own taskbar icon, even when the title is identical.
- Title is purely a per-card display label; it never merges or splits icons.

**Package identity is a separate, harder grouping boundary on top of the above.** Two different package identities never merge their tasks into one taskbar icon — confirmed even with byte-identical title and fully-resolved absolute icon path. Each identity has its own `tasks.json`.

**Per-card rendering, in one taskbar icon's flyout:**
- Each card shows the app icon, title, subtitle (bold), and a content row reflecting the `AppTaskContent` shape, with a state glyph: `Running` = purple spinner arc, `Paused` = gray pause bars, `Completed` = green checkmark, `Error` = red diamond with a white X, `NeedsAttention` = amber warning triangle. `Completed` and `Error` cards also get a "Show details" button; `Running`/`Paused`/`NeedsAttention` don't.
- When content has `SetQuestion` set, the question text takes over the bold subtitle slot — `AppTaskInfo.Subtitle` is not shown at all.
- Clicking a card invokes that item's `DeepLink`.
- Each card has an **X** dismiss button that sets `HiddenByUser = true`. This does not remove the task from `FindAll()`/`tasks.json`; per [`AppTaskInfo.HiddenByUser`](AppTaskInfo.md), the app must call `Remove()` itself or the hidden entry lingers forever.
- The flyout switches between a multi-column "card" layout and a compact vertical checklist based on available screen width, not item count.

**The taskbar icon's badge is priority-ranked, not recency-ranked**, reflecting the single most notable state among all items sharing that icon:

`Error` (red X) > `NeedsAttention` (exclamation) > `Completed` (green check) > `Paused` (gray pause) > `Running` (no badge)

Independent of update order — e.g. a group with both `Completed` and `Paused` shows `Completed`; a group with both `Error` and `Completed` shows `Error`, regardless of which was updated last. `NeedsAttention` slots at #2, verified 2026-07-13 (LIFE-24 empirical item 1) by staging shared-icon glommed pairs on the live taskbar (Windows 11 26200): a `NeedsAttention`+`Completed` group badges as the exclamation; a `NeedsAttention`+`Error` group badges as the red X. Note the **flyout list order differs from badge priority** — the `NeedsAttention` card sorted first in the hover list even in the `Error`-badged group. (`NeedsAttention` requires content with `SetQuestion` — see [state-content-compatibility.md](state-content-compatibility.md).)

## Namespace essentials

- Experimental namespace (`[Experimental]` on every type/member), requires `Windows.UI.Shell.Tasks.AppTaskContract`. Present in the SDK's `UnionMetadata\<version>\Windows.winmd`, not in system WinMetadata — see [`CLAUDE.md`](../../CLAUDE.md) for how the csproj resolves this via `CsWinRTInputs`.
- Device family: **Windows Desktop Extension SDK**, introduced `10.0.26100.0`.
- All 3 classes (`AppTaskInfo`, `AppTaskContent`, `AppTaskResultAsset`) are `sealed`, `MarshalingBehavior.Agile`, `Threading.Both`.
- **Rollout** (MS docs, updated 2026-04-24): *"App task support will start gradually rolling out to Windows 11 starting May, 2026."* This is almost certainly why `IsSupported()` can still return `CLASS_E_CLASSNOTAVAILABLE` on an otherwise-correct Windows 11 26100+ build with valid package identity — the activation registration is being staged, not just gated by a version check.
- **Two contract versions are in play**, though the docs list a single "v2.0" requirement:
  - **v2.0** (`ContractVersion` `131072`): `AppTaskInfo.Id`, `AppTaskInfo.HiddenByUser`, `AppTaskInfo.UpdateDeepLink`. These come from a second interface, `IAppTaskInfo2`, invisible in the public docs (listed as plain `AppTaskInfo` members) but visible in the generated projection.
  - **v1.0** (`65536`): everything else — `AppTaskContent`, `AppTaskResultAsset`, `AppTaskState`, and all other `AppTaskInfo` members. Per-member contract is annotated in each class file's tables.
  - `AppTaskContract` itself is an empty marker enum used for `[ContractVersion]` attributes — nothing to document separately.

### Packaging requirement

Requires a packaged app (this project uses the full-package identity model — see [CLAUDE.md](../../CLAUDE.md)) and a `com.microsoft.apptaskprovider` AppExtension in the manifest:

```xml
<uap3:Extension Category="windows.appExtension">
  <uap3:AppExtension
    Name="com.microsoft.apptaskprovider"
    PublicFolder="Public"
    Id="MyApp.AppTaskProvider"
    DisplayName="AppTaskProvider for MyApp"/>
</uap3:Extension>
```

Already present in `src/Atv/Package/AppxManifest.template.xml`.

### URI formats accepted by icon/asset parameters

Documented for `AppTaskInfo.IconUri`; also applies to `AppTaskContent.CreatePreviewThumbnail` and `AppTaskResultAsset`:

- `ms-appx:///...` — package-relative (recommended)
- `ms-appdata:///...` — app data folder
- Absolute path, e.g. `C:\...\icon.png` or `file://c:/temp/icon.png`

### Local gotchas

- `AppTaskInfo.Create`'s `deepLink` and `iconUri` parameters are typed as (nullable-looking) `Uri`, but the native side dereferences both unconditionally (`ResolveIconPath`, `CreateJsonObject`) — passing `null` for either fails. Always pass a real `Uri`, even a placeholder. See `Program.cs`.
- Some `AppTaskContent` shapes crash `explorer.exe` when the Shell renders their flyout card — see [state-content-compatibility.md](state-content-compatibility.md#createtextsummaryresult-crashes-explorerexe). The crash needs the flyout displayed (hovering the taskbar icon); it isn't triggered by the task existing in `tasks.json` or by Explorer starting. Recover by clearing the offending task's data (`Remove()`/`clear`) before hovering that icon again.
- Empirically (ARM64 Windows 11 26100, phase-01 foundation work, 2026-07-07): a NativeAOT `win-x64` publish, registered and launched via `winapp run` on this ARM64 host (so it executes under x64 emulation), got a *registered* package identity fine but `AppTaskInfo.IsSupported()` returned `false` ("AppTaskInfo is not supported on this system"). The identical build published natively for `win-arm64` and run the same way (`winapp run` + `--with-alias`) worked end-to-end (create/list/clear). Not re-verified against a real x64 machine, so unclear whether this is x64-emulation-specific or a native x64 gap too — but package identity registration itself is not the blocker; `IsSupported()`'s activation-registry check is.
- `AppTaskInfo.UpdateTitles(title, subtitle)` throws (`COMException`, "The parameter is incorrect" / "Title cannot be empty") when called with an empty-string `title` on an already-live task — even though `Create(title: "", ...)` accepts an empty title fine (phase-15 real-adapter discovery, 2026-07-13). So any code path that re-applies a caller's title/subtitle on an update (not a create) must never pass through an empty title unconditionally; it must either skip the call when no title was actually supplied, or fall back to the task's current title. `Atv.Semantics.SemanticEngine.ApplyIdentityIfClaimed` is where this is handled.
- `AppTaskInfo.IconUri` readback does not round-trip byte-identical to the `iconUri` passed to `Create` (phase-15 real-adapter discovery, 2026-07-13) — this is the live-object-property counterpart to the on-disk `iconPath`-resolution behavior noted above (both stem from the same `ResolveIconPath` native call). Concretely: `Create(..., new Uri("ms-appx:///Assets/Square44x44Logo.png"), ...)` then reading `.IconUri` back off the returned/refetched `AppTaskInfo` does not return the literal `ms-appx://` Uri — it returns the platform's resolved local path, so `Uri.op_Inequality` against the original `ms-appx` Uri is `true` even though nothing meaningfully "changed." Any code that detects "did the caller's icon token change" by comparing a live task's `IconUri` against a caller-supplied `Uri` (`Atv.Semantics.SemanticEngine`'s icon-immutability-forces-recreate check; `Atv.Operations.TaskOperations`'s v1-era `AdoptLive` had the identical shape) is only safe when the caller-supplied side is always a real `file://` path too — which production always is (`Atv.Icons.IconService.Place` never emits `ms-appx`/`ms-appdata`), so this only bites test code that uses an `ms-appx` placeholder Uri across more than one write to the same handle. `tests/Atv.AdapterTests/SemanticVerbsEndToEndTests.cs` uses a real per-test temp-file `file://` Uri for exactly this reason.

---
Sourced from `https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.tasks` (moniker `winrt-28000`, page updated 2026-04-24) via the `microsoft-learn` MCP server, cross-checked against `obj/Debug/net10.0-windows10.0.26100.0/Generated Files/CsWinRT/Windows.UI.Shell.Tasks.cs`. Fetched 2026-07-01.
