# GitHub Copilot CLI integration

Wires GitHub Copilot CLI sessions to Agent Task Void (`atv`) taskbar cards through
a native Copilot plugin. The plugin targets only the host-agnostic v2 semantic
API in [docs/integration-api.md](../../docs/integration-api.md); no
Copilot-specific logic lives in `atv.exe`.

## Compatibility

Authored, captured, and live-dogfooded against GitHub Copilot CLI 1.0.71
(2026-07-16). Raw-event findings are in
[docs/host-events/copilot-cli.md](../../docs/host-events/copilot-cli.md) —
see that file for the version and capture date each finding is confirmed
against. If a newer host version behaves differently, re-run that capture
to update the finding.

## Layout

```text
integrations/copilot-cli/
├── .github/plugin/marketplace.json
├── plugins/atv-integration/
│   ├── plugin.json
│   ├── hooks/hooks.json
│   ├── translate.ps1
│   └── map.json
└── README.md
```

## Install

For development/dogfooding, load the working tree directly from any scratch
repository (Copilot caches installed plugins, but `--plugin-dir` bypasses
that cache):

```powershell
copilot --plugin-dir D:\path\to\AppTaskInfoCli\integrations\copilot-cli\plugins\atv-integration
```

For a normal install from this repository:

```powershell
copilot plugin install grork/AppTaskInfoCli:integrations/copilot-cli/plugins/atv-integration
```

Confirm it with `copilot plugin list` or `/plugin list`. Uninstall with:

```powershell
copilot plugin uninstall atv-integration
```

`atv doctor` must report package identity present and `AppTaskInfo` supported
before any card can render.

## Event mapping

| Copilot event | `atv` claim |
|---|---|
| `userPromptSubmitted` | `working <session> --goal -`; Copilot-internal `<system_notification>` prompts are discarded |
| `preToolUse` ordinary tool | `activity <session> --kind ... --label -` |
| `preToolUse:ask_user` | `blocked <session> --question -` |
| `postToolUse:ask_user` | a matching `activity` that clears Blocked after the answer |
| `preToolUse:task` | `agent-started <parent> --agent <task-name> --name <agent-type>` plus a pending correlation record |
| Child `userPromptSubmitted` | atomically claims the matching pending prompt hash into a child-session → parent/task record |
| Child `preToolUse` | `activity <parent> --agent <task-name> ...`, which the engine redirects to the child card |
| Child `agentStop` | `agent-stopped <parent> --agent <task-name>`; `ready <parent>` only when the subagent was a background worker |
| `subagentStart` | counts one more outstanding subagent under the parent; no card change |
| `subagentStop` | decrements that count; on reaching zero, retires every child card still tracked for the parent and readies the parent — this is the cancel cleanup |
| Sync `postToolUse:task` | `agent-stopped` completion fallback, no `ready` |
| Background `notification:agent_idle`/`agent_completed` | `agent-stopped`, then `ready` |
| `notification:permission_prompt` | `blocked`; bare `permissionRequest` is not hooked because it fires on auto-approved paths too |
| `notification:shell_completed` | `activity --kind shell` |
| Main `agentStop` | `ready`; the engine refuses it while child agents are still registered |
| `preCompact` | `activity --kind compacting` |
| Non-recoverable `errorOccurred` | `broken` with the nearest closed reason token |
| `sessionEnd:user_exit|abort` | `session-ended --reason finished` |
| `sessionEnd:error|timeout` | `session-ended --reason error` |
| `sessionEnd:complete` | `ready`, not removal — prompt mode can emit an early `complete` while background agents are still running |

Only `postToolUse` for `ask_user|task` is registered. Ordinary tool activity is
published from `preToolUse` alone, avoiding duplicate step entries and one
unnecessary synchronous hook process per tool.

`ready` is a parent turn-end signal, and `atv` refuses it only while other
active agent loci remain — so with a single subagent it always lands. A
**synchronous** subagent's completion resumes the parent turn (it posts the
agent's results and keeps working), so the translator emits only `agent-stopped`
there and lets the parent's own `agentStop` supply `ready` at the true turn end.
A **background** worker finishes after the parent turn has already ended, so its
completion (child `agentStop` or `notification:agent_idle`/`agent_completed`)
does retry `ready`, which the engine gates against any still-running workers.
The subagent's `mode` is read from the correlation record.

## Child correlation

Copilot 1.0.71 exposes two disconnected identities at the command-hook boundary:

```text
Parent task event: parent session + task name + exact child prompt
Child events:      tool-call-id session + exact child prompt
```

The translator joins them without reading Copilot's internal transcript and
without changing `atv`:

1. Before a Task executes, hash `cwd + NUL + exact prompt` with SHA-256 and
   persist a short-lived pending record containing parent session, task name,
   agent type, and mode.
2. The child's first `userPromptSubmitted` repeats the exact prompt. Under a
   named mutex, it claims the sole matching pending record and replaces it with
   an active child-session → parent/task record.
3. Child tool and stop events use that active mapping.
4. Child completion deletes the active record.

Raw prompts are never persisted. Pending records expire after 10 minutes;
active records after 24 hours. State is stored in Copilot's private
`COPILOT_PLUGIN_DATA` directory:

```text
correlation-state.json
translator.log
```

This state is private to the plugin — atv's own product state (sidecar,
config) is untouched by it. If storage is unavailable, the prompt is
missing, or multiple identical pending prompts make the match ambiguous,
Copilot continues normally and the integration falls back to parent/task
lifecycle reporting rather than guessing.

## Cancel cleanup

Interrupting a turn with Esc emits no per-subagent completion the translator
can route: no child `agentStop`, no `agent_idle`, and no parent `agentStop`
either. The only cancel signal is one `subagentStop` per subagent, on the
parent session, carrying just the agent type — too coarse to name a single
card.

So the translator counts instead. `subagentStart` increments an
outstanding-subagent count per parent; `subagentStop` decrements it. Reaching
zero means every subagent that started has stopped, so any child still tracked
for that parent was cancelled — the translator retires all of them and readies
the parent. On a normal turn each child's own `agentStop` already retired its
card, so the zero-crossing sweep finds nothing and stays silent (it never
readies a resuming sync parent early). The count is the one unambiguous
"nothing is running under this parent" moment, so the sweep needs no per-card
identity. Cancelling one subagent of several only decrements the count, so its
card lingers until the siblings stop and the count reaches zero — delayed, not
misattributed.

## Dev dogfood: the `atv-command.txt` override

`translate.ps1` resolves the `atv` command it invokes in this order: the
`ATV_TRANSLATOR_STUB_EXE` test seam, then `atv-command.txt` in the state
root, then the bare `atv` alias. `atv-command.txt` is a hand-written,
single-line file holding the verbatim command to run — typically the
`atv-dev` shim's full path — so a working-tree dogfood routes cards onto the
dev pool instead of the operator's daily retail install. A broken or missing
target no-ops rather than falling back to bare `atv`, so a stale override
can never leak a dev session onto the daily cards.

The state root is the directory holding this plugin's `correlation-state.json`
and `translator.log` — `COPILOT_PLUGIN_DATA` only exists inside the hook
process, so it can't be read from a shell; find the directory by those two
files instead. Drop `atv-command.txt` there for a dogfood session, and
remove it for daily use.

Before dogfooding against a real session, disable the daily install first
(`copilot plugin uninstall atv-integration`, or confirm it isn't installed
for the profile you're dogfooding with `--plugin-dir`) — the same
discipline as any capture-harness run, so the plugin under test is the only
one wired to this session's hooks.

## Failure and security posture

- The translator always exits 0 and never passes `--strict`.
- Hook stdout is always empty, so it never modifies Copilot decisions or model
  context.
- Arbitrary text uses atv's `--flag -` UTF-8 stdin convention.
- No Copilot transcript, session database, or internal event file is read.
- Correlation state stores hashes and identifiers, never raw prompts or tool
  payloads.
- Host errors and missing `atv` are written only to the plugin-local
  `translator.log`; they cannot break a Copilot tool call.

This matters most for `preToolUse`: if that hook process crashes or exits
nonzero, Copilot blocks the tool call — so the translator must exit 0 even
when everything inside it has failed.

## Known limitations

- Child-session ids and notification title/message shapes are observed
  behavior, not documented API. A child (subagent) session id is the parent
  task's tool-call id, never a session GUID, so the translator treats any
  non-GUID session id as a child; the id's prefix varies by model family
  (`call_` for OpenAI-family, `toolu_` for Claude-family, seen through Copilot
  1.0.74). Re-capture when the installed version changes significantly.
- Concurrent identical child prompts are not correlated.
- Cancelling one subagent of several (e.g. via the `/tasks` manager) leaves its
  card Running until the remaining subagents stop and the outstanding count
  reaches zero, at which point it is swept with them.
- A genuine `postToolUseFailure` and `errorOccurred` payload were not induced
  during capture; error mapping retains conservative fallbacks.
- Command hooks are synchronous, so tool event publication adds process-launch
  latency.
- Prompt-mode `sessionEnd:complete` leaves the card Ready for watchdog cleanup
  rather than risking premature removal during background work.
- **Permission recovery is coarse.** Copilot exposes `permission_prompt` only
  on the parent session, with no permission-approved/completion hook. The
  parent remains Blocked until later parent activity or the turn's final
  Ready claim clears it; child activity cannot clear the parent's question.
  Hooking every `postToolUse` would only clear it at tool completion — a
  one-hour build would still remain Blocked for an hour — while adding
  synchronous overhead to every tool call.
