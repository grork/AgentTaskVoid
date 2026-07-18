# AppTaskState × AppTaskContent compatibility

Not documented by Microsoft. `AppTaskInfo.Update(state, content)` accepts far more `state`/`content` combinations than the Shell can render correctly — passing validation is not the same as rendering as intended.

## Supported scenarios — use only these

"Mutators" throughout this file means only `SetQuestion`/`AddButton`/`SetTextInput` — the optional "needs attention" additions described in the Mutators section of [AppTaskContent.md](AppTaskContent.md). It has nothing to do with changing a content shape's own payload (e.g. which steps are shown, what text a summary contains) — that's always done by calling the relevant factory method again with new data (e.g. `CreateSequenceOfSteps` with a new `completedSteps`/`executingStep`) and passing the fresh `AppTaskContent` to `Update()`, independent of mutators and unaffected by anything in this file.

| Content shape | State(s) | Mutators |
|---|---|---|
| `CreateSequenceOfSteps` | `Running`, `Completed`, `Paused`, `Error` | none |
| `CreatePreviewThumbnail` | `Running`, `Completed`, `Paused`, `Error` | none |
| `CreateSequenceOfSteps` or `CreatePreviewThumbnail` | `NeedsAttention` | `SetQuestion` only (untested with `AddButton`/`SetTextInput` also set — see below) |
| `CreateTextSummaryResult` | `Completed`, `Error` | none |
| `CreateGeneratedAssetsResult` | `Completed` | none |

**Everything else is unsupported.** Expect one of: blank/broken rendering (missing content, missing button labels, no visible text-input box) despite `Update()` succeeding and the data persisting correctly, or — specifically for `CreateTextSummaryResult` combined with `SetQuestion` outside the one safe sub-case documented below — a real crash in `explorer.exe`. Don't rely on undocumented combinations even if they happen to look fine in one build.

The rest of this file is the underlying evidence (full matrices, exact failure modes) for debugging a specific combination — not additional guidance beyond the table above.

## API-level rule

`AppTaskState.NeedsAttention` requires the content to have `SetQuestion` set — any content shape, but a question must be present. `AddButton`/`SetTextInput` alone don't satisfy this. Every other state accepts any content shape with any mutators, including none.

An invalid combination (`NeedsAttention` without `SetQuestion`) returns `E_INVALIDARG` (`0x80070057`, "Content is not valid for the new state"), leaves the task's state unmodified, and does not crash.

## Shell rendering vs. API acceptance

`Update()` succeeding only means the content was accepted and persisted (`tasks.json` shows `questionText`/`buttons`/`textInput` verbatim) — it doesn't mean the Shell displays it as intended. Outside `NeedsAttention`, the Shell doesn't render question/button/text-input UI at all for `CreateSequenceOfSteps`/`CreatePreviewThumbnail` — a card with mutators attached looks identical to one without them. `CreateTextSummaryResult` and `CreateGeneratedAssetsResult` are exceptions — see their sections below.

Schema detail: a content object with all three mutators set gets an extra `"templateVariant":"BinaryChoice"` field in `tasks.json` that partial combinations don't get.

## CreateTextSummaryResult: crashes explorer.exe

`Update()` succeeds for every combination below; the crash happens afterward, when the Shell renders the card — triggered by hovering the taskbar icon, not by task creation or Explorer startup alone.

Fault signature (Application Error / WER, identical every time): module `Taskbar.View.dll`, exception `0xc0000409` (`STATUS_STACK_BUFFER_OVERRUN`), classification `BEX64`, same fault offset on every occurrence. `explorer.exe` auto-restarts; recovery requires removing the task's data before hovering that icon again.

| Mutators | Running | Completed | Paused | Error | NeedsAttention |
|---|---|---|---|---|---|
| none | **crash** | renders fully | renders (blank) | renders fully | invalid — `E_INVALIDARG` |
| `SetQuestion` only | **crash** | **crash** | **crash** | **crash** | **crash** |
| `SetQuestion` + `AddButton` | **crash** | **crash** | **crash** | **crash** | **crash** |
| `SetQuestion` + `SetTextInput` | renders (blank) | renders (blank) | renders (blank) | renders (blank) | renders (blank) |
| all three | **crash** | **crash** | **crash** | **crash** | **crash** |

Rule: crashes whenever `SetQuestion` is set, unless `SetTextInput` is also set and `AddButton` is not (renders blank instead). With no mutators, only `Running` crashes.

## CreateGeneratedAssetsResult: renders blank instead of crashing

Never crashes, in any combination tested — degrades to an empty rounded-rectangle placeholder instead of the actual asset (icon, name, context).

| State | No mutators |
|---|---|
| `Running` | blank |
| `Completed` | renders fully |
| `Paused` | blank |
| `Error` | blank |
| `NeedsAttention` | invalid — `E_INVALIDARG` |

Under `Completed`, adding **any** mutator (alone or combined) replaces the working render with the same blank placeholder — unlike `CreateTextSummaryResult`, the specific combination doesn't matter here, and none of them crash it.

Under `NeedsAttention`: `SetQuestion` alone renders blank plus a "Show details" button; all three mutators together renders blank with no "Show details" and no visible buttons — "Show details" looks like a fallback shown only when no explicit `AddButton` is set, consistent with `CreateSequenceOfSteps`/`CreatePreviewThumbnail` under `Completed`/`Error`.

## NeedsAttention with all three mutators, non-crashing shapes

For `CreateSequenceOfSteps`/`CreatePreviewThumbnail` with all three mutators under `NeedsAttention`: two button-shaped placeholders render with no visible label text, and no text-input box is visible, despite `tasks.json` showing the data persisted correctly. Not root-caused; not yet isolated whether `SetQuestion` alone (no button/text-input) avoids this.
