# AppTaskState

`Windows.UI.Shell.Tasks.AppTaskState` — `enum AppTaskState : int`, `Experimental`, contract v1.0.

| Name | Value | Meaning |
|---|---|---|
| `Running` | `0` | Actively executing. |
| `Completed` | `1` | Finished successfully. Terminal state. |
| `NeedsAttention` | `2` | Needs user input to continue — pair with `AppTaskContent.SetQuestion`/`AddButton`/`SetTextInput`. |
| `Paused` | `3` | Suspended; resumable without user action. |
| `Error` | `4` | Completed with an error. Terminal state. |

`Completed` and `Error` are the two "ending" states — `AppTaskInfo.EndTime` is populated once a task reaches either of them.

Set via `AppTaskInfo.Update(state, content)` or `AppTaskInfo.UpdateState(state)`. See [AppTaskInfo.md](AppTaskInfo.md).
