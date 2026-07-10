# AppTaskContent

`Windows.UI.Shell.Tasks.AppTaskContent` — the visual content shown for a task in the Shell UI. `sealed class`, `Experimental`, contract v1.0. Not directly constructable — build one via exactly one of the 4 static factory methods below, optionally layer on the 3 mutator methods, then pass it to `AppTaskInfo.Create`/`Update`.

## Factory methods (statics) — pick exactly one representation

| Method | Signature | Use for |
|---|---|---|
| `CreateSequenceOfSteps` | `static AppTaskContent CreateSequenceOfSteps(string[] completedSteps, string executingStep)` | Step-by-step progress (e.g. AI agent workflows). The task doesn't need to know what steps come next — this shows only past + current progress. Pairs with `AppTaskInfo.GetCompletedSteps()`/`GetExecutingStep()` when building the next update. |
| `CreatePreviewThumbnail` | `static AppTaskContent CreatePreviewThumbnail(Uri imageUri, string executingStep)` | A preview thumbnail of task output while it runs. `imageUri` supports `ms-appx:///`, `ms-appdata:///`, absolute paths — see [README](README.md#uri-formats-accepted-by-iconasset-parameters). |
| `CreateTextSummaryResult` | `static AppTaskContent CreateTextSummaryResult(string text)` | A short text description summarizing a completed result. |
| `CreateGeneratedAssetsResult` | `static AppTaskContent CreateGeneratedAssetsResult(AppTaskResultAsset[] assets)` | A result that includes generated files/content — see [AppTaskResultAsset.md](AppTaskResultAsset.md). |

**Gotcha (empirical, 2026-07-09):** `CreateSequenceOfSteps`'s `executingStep` parameter cannot be `""` — an empty string throws `ArgumentException`/`E_INVALIDARG` ("executingStep cannot be empty"), even though the SDK signature accepts any `string` and no doc mentions the constraint. This bit `TaskOperations`'s "no real step content yet" baselines (a brand-new `start`, `--reset`, an icon-forced Remove+Create, or a resurrection's fresh content), which all originally passed `""` — caught only once phase 08's real-adapter suite exercised those paths against the live platform (`FakeAppTaskStore` doesn't model this validation; see INFRA-15's "must not" list). Fix: use a non-empty placeholder (`"Not started yet."`) for any not-yet-stepped baseline.

## Mutators — layer "needs attention" UI on top of any content

`SetQuestion` is a requirement for `AppTaskState.NeedsAttention`: `AppTaskInfo.Update(NeedsAttention, content)` returns `E_INVALIDARG` unless `content` has had `SetQuestion` called on it (any factory shape). `AddButton`/`SetTextInput` are optional additions on top of a question — neither one alone (without `SetQuestion`) satisfies the `NeedsAttention` requirement. All three mutators are valid under every other state, with no `SetQuestion` requirement there. Full matrix: [state-content-compatibility.md](state-content-compatibility.md).

| Method | Signature | Notes |
|---|---|---|
| `SetQuestion(question)` | `void SetQuestion(string question)` | Question text shown to the user. **Required for `AppTaskState.NeedsAttention`** (see above) — combine with `AddButton` and/or `SetTextInput` when the task needs a decision. |
| `AddButton(text, actionUri)` | `void AddButton(string text, Uri actionUri)` | Adds a clickable action button; `actionUri` is launched on click. Max count is `MaxButtons`. On its own it doesn't satisfy the `NeedsAttention` requirement (see above). |
| `SetTextInput(placeholderText, actionUriTemplate)` | `void SetTextInput(string placeholderText, string actionUriTemplate)` | Free-form text input field. `actionUriTemplate` must contain the literal token `{userTextInput}`, which is replaced with the user's URL-encoded input on submit. Example: template `my-app:task/?response={userTextInput}` + input `scope only` → `my-app:task/?response=scope%20only`. On its own it doesn't satisfy the `NeedsAttention` requirement (see above). |

## Statics (property)

| Property | Type | Notes |
|---|---|---|
| `MaxButtons` | `static uint` | Max number of buttons addable via `AddButton`. |
