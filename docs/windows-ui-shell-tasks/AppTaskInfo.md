# AppTaskInfo

`Windows.UI.Shell.Tasks.AppTaskInfo` — represents an app task that can be displayed in the Windows Shell as its own **separate taskbar icon**, grouped independently of any running app window. `sealed class`, `Experimental`. Contract v1.0 base, v2.0 for 3 members — see [README](README.md#namespace-essentials).

**Not jump lists.** This is a distinct feature — separately-grouped taskbar entries, independent of running apps, not a per-app right-click menu. See [README](README.md#this-is-not-jump-lists).

Tasks are **persisted** across app sessions and reboots — they remain visible without the creating app needing to be running. For each logical task, create one `AppTaskInfo` and mutate it as the task progresses; call `Remove()` when it's no longer relevant. It's the app's own responsibility to do so — nothing removes stale tasks automatically, including hidden ones (see `HiddenByUser` below).

## Statics

| Member | Signature | Description |
|---|---|---|
| `IsSupported()` | `static bool IsSupported()` | Whether app tasks are supported on this device. Call before any other API, and at startup alongside `FindAll()`. Can throw `COMException(CLASS_E_CLASSNOTAVAILABLE)` instead of returning `false` — wrap in try/catch. If it returns `false`, `FindAll()` returns an empty collection. |
| `FindAll()` | `static AppTaskInfo[] FindAll()` | All non-removed tasks created by this app, including ones the user has hidden from the taskbar. Empty array if unsupported. Call at startup to recover tasks that outlived a previous app session. |
| `Create(title, subtitle, deepLink, iconUri, content)` | `static AppTaskInfo Create(string title, string subtitle, Uri deepLink, Uri iconUri, AppTaskContent content)` | Creates and persists a new task. `title` is required — throws if missing/empty. `subtitle` optional, `""` is fine. `deepLink`/`iconUri` must both be non-null `Uri`s (see [README local gotchas](README.md#local-gotchas-learned-by-experimentation-not-documented-by-ms) — the native side dereferences them unconditionally). `content` comes from one of the `AppTaskContent` factory methods. |

## Instance properties (all get-only)

| Property | Type | Contract | Notes |
|---|---|---|---|
| `Id` | `string` | v2.0 | Auto-generated unique id. |
| `Title` | `string` | v1.0 | Groups related tasks; sometimes shown appended with `Subtitle` (e.g. "Researcher - Trends in smart appliances"). Set via `Create`/`UpdateTitles`. |
| `Subtitle` | `string` | v1.0 | Optional additional context. Set via `Create`/`UpdateTitles`. |
| `State` | `AppTaskState` | v1.0 | Set via `Update`/`UpdateState`. See [AppTaskState.md](AppTaskState.md). |
| `StartTime` | `DateTimeOffset` | v1.0 | When the task was created. |
| `EndTime` | `DateTimeOffset?` | v1.0 | Populated once the task reaches an ending state (`Completed` or `Error`); `null` until then. |
| `DeepLink` | `Uri` | v1.0 | Launched when the user clicks the task's Shell representation. Set via `Create`/`UpdateDeepLink`. |
| `IconUri` | `Uri` | v1.0 | Icon representing the task. See [README URI formats](README.md#uri-formats-accepted-by-iconasset-parameters). |
| `HiddenByUser` | `bool` | v2.0 | `true` if the user hid this task from the taskbar via Shell UI. Doesn't affect the app-side task — only its Shell representation is removed. Hidden tasks are still returned by `FindAll()`. **The system does not clean these up** — if a task is hidden, the app is expected to notice (e.g. on next `FindAll()`) and call `Remove()` itself; otherwise it lingers forever as a hidden, un-removed entry. |

## Instance methods

| Method | Signature | Description |
|---|---|---|
| `Remove()` | `void Remove()` | Removes the Shell representation without changing task state. Idempotent — calling it more than once is harmless. |
| `Update(state, content)` | `void Update(AppTaskState state, AppTaskContent content)` | Sets state and content together. Use as the task progresses, errors, or completes. |
| `UpdateState(state)` | `void UpdateState(AppTaskState state)` | Sets state only; content unchanged. |
| `UpdateTitles(title, subtitle)` | `void UpdateTitles(string title, string subtitle)` | Sets title/subtitle only. |
| `UpdateDeepLink(deepLink)` | `void UpdateDeepLink(Uri deepLink)` | Sets deep link only. **Contract v2.0.** |
| `GetCompletedSteps()` | `string[] GetCompletedSteps()` | Steps already completed, in order — pairs with content created by `AppTaskContent.CreateSequenceOfSteps`. |
| `GetExecutingStep()` | `string GetExecutingStep()` | Currently-executing step — same pairing, use alongside `GetCompletedSteps()` to build the next content update. |

## Typical lifecycle

```
IsSupported() → Create(...) → [UpdateState / Update / UpdateTitles / UpdateDeepLink]* → Remove()
```

At app startup, call `IsSupported()` then `FindAll()` to recover any tasks left over from a previous session before creating new ones.
