# GitHub Copilot CLI integration

Wires GitHub Copilot CLI sessions to Agentaskvoid (`atv`) taskbar cards through
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
| `postToolUse:ask_user` | same-locus `activity` to clear Blocked after the answer |
| `preToolUse:task` | `agent-started <parent> --agent <task-name> --name <agent-type>` plus a pending correlation record |
| Child `userPromptSubmitted` | atomically claims the matching pending prompt hash into `call_* -> parent/task` |
| Child `preToolUse` | `activity <parent> --agent <task-name> ...`, which the engine redirects to the child card |
| Child `agentStop` | `agent-stopped <parent> --agent <task-name>`, then `ready <parent>` |
| Sync `postToolUse:task` | idempotent lifecycle-completion fallback |
| Background `notification:agent_idle`/`agent_completed` | `agent-stopped`, then `ready` |
| `notification:permission_prompt` | `blocked`; bare `permissionRequest` is not hooked because it fires on auto-approved paths too |
| `notification:shell_completed` | `activity --kind shell` |
| Main `agentStop` | `ready`; the engine refuses it while registered child loci remain |
| `preCompact` | `activity --kind compacting` |
| Non-recoverable `errorOccurred` | `broken` with the nearest closed reason token |
| `sessionEnd:user_exit|abort` | `session-ended --reason finished` |
| `sessionEnd:error|timeout` | `session-ended --reason error` |
| `sessionEnd:complete` | `ready`, not removal — prompt mode can emit an early `complete` while background agents are still running |

Only `postToolUse` for `ask_user|task` is registered. Ordinary tool activity is
published from `preToolUse` alone, avoiding duplicate step entries and one
unnecessary synchronous hook process per tool.

## Child correlation

Copilot 1.0.71 exposes two disconnected identities at the command-hook boundary:

```text
Parent task event: parent session + task name + exact child prompt
Child events:      call_* session id + exact child prompt
```

The translator joins them without reading Copilot's internal transcript and
without changing `atv`:

1. Before a Task executes, hash `cwd + NUL + exact prompt` with SHA-256 and
   persist a short-lived pending record containing parent session, task name,
   agent type, and mode.
2. The child's first `userPromptSubmitted` repeats the exact prompt. Under a
   named mutex, it claims the sole matching pending record and replaces it with
   an active `call_* -> parent/task` record.
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

- The `call_*` child-session prefix and notification title/message shapes are
  observed Copilot 1.0.71 behavior, not documented API. Re-capture when the
  installed version changes significantly.
- Concurrent identical child prompts are not correlated.
- A genuine `postToolUseFailure` and `errorOccurred` payload were not induced
  during capture; error mapping retains conservative fallbacks.
- Command hooks are synchronous, so tool event publication adds process-launch
  latency.
- Prompt-mode `sessionEnd:complete` leaves the card Ready for watchdog cleanup
  rather than risking premature removal during background work.
- **Permission recovery is coarse.** Copilot exposes `permission_prompt` only
  on the parent session, with no permission-approved/completion hook. The
  parent remains Blocked until later parent activity or the turn's final
  Ready claim clears it; child activity cannot clear that parent locus.
  Hooking every `postToolUse` would only clear it at tool completion — a
  one-hour build would still remain Blocked for an hour — while adding
  synchronous overhead to every tool call.
