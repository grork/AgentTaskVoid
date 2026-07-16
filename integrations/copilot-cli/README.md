# GitHub Copilot CLI integration

Wires GitHub Copilot CLI sessions to Agentaskvoid (`atv`) taskbar cards through
a native Copilot plugin. The plugin targets only the host-agnostic v2 semantic
API in `docs/integration-api.md`; no Copilot-specific logic lives in `atv.exe`.

**Authored, captured, and live-dogfooded against:** GitHub Copilot CLI
**1.0.71** on 2026-07-16. Raw-event findings are in
`docs/host-events/copilot-cli.md`.

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

## Try the local plugin

From any scratch repository:

```powershell
copilot --plugin-dir D:\path\to\AppTaskInfoCli\integrations\copilot-cli\plugins\atv-integration
```

Copilot caches installed plugins, but `--plugin-dir` loads the working tree
directly and is therefore the preferred development/dogfood path.

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
| `notification:permission_prompt` | `blocked`; bare `permissionRequest` is deliberately not hooked because it fires on auto-approved paths too |
| `notification:shell_completed` | `activity --kind shell` |
| Main `agentStop` | `ready`; the engine refuses it while registered child loci remain |
| `preCompact` | `activity --kind compacting` |
| Non-recoverable `errorOccurred` | `broken` with the nearest closed reason token |
| `sessionEnd:user_exit|abort` | `session-ended --reason finished` |
| `sessionEnd:error|timeout` | `session-ended --reason error` |
| `sessionEnd:complete` | `ready`, not removal: prompt mode can emit an early `complete` while background agents are still running |

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

This is ephemeral integration state, not Agentaskvoid product state. Every
failure is fail-open: if storage is unavailable, the prompt is missing, or
multiple identical pending prompts make the match ambiguous, Copilot continues
normally and the integration degrades to parent/task lifecycle reporting. It
never guesses.

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

This is especially important for `preToolUse`: Copilot fails closed when that
hook process crashes or exits nonzero.

## Known limitations

- The `call_*` child-session prefix and notification title/message shapes are
  empirical Copilot 1.0.71 contracts. Re-capture when the installed version
  drifts materially.
- Concurrent identical child prompts are intentionally not correlated.
- A genuine `postToolUseFailure` and `errorOccurred` payload were not induced
  during capture; error mapping retains conservative fallbacks.
- Command hooks are synchronous. The plugin minimizes its hook set, but tool
  event publication still adds process-launch latency.
- Prompt-mode `sessionEnd:complete` leaves the card Ready for watchdog cleanup
  rather than risking premature removal during background work.
- **Permission recovery is intentionally coarse.** Copilot exposes
  `permission_prompt` only on the parent session and exposes no public
  permission-approved/completed hook. The parent therefore remains Blocked
  until later parent activity or the turn's final Ready claim clears it; child
  activity cannot clear that parent locus. Hooking every `postToolUse` would
  only clear at tool completion (a one-hour build would still remain Blocked
  for an hour), while adding synchronous overhead to every tool. Operator
  decision 2026-07-16: retain the accurate attention signal and accept this
  stale-after-approval window rather than use timers, internal transcripts, or
  disable permission attention entirely.

## Verification status

- Plugin/manifest/hook shape: covered by artifact tests.
- Translator routing and failure posture: real Windows PowerShell process
  against the compiled stub `atv`.
- Correlation: covered for claim, activity routing, completion, UTF-8, raw
  prompt non-persistence, ambiguity, and concurrent claim races.
- Claude Code translator regression suite: run alongside the Copilot tests
  because both integrations share the process harness.
- **Real Copilot-to-taskbar dogfood: confirmed.** Two background subagents
  produced a parent plus two child cards grouped under the exact shared icon;
  each child showed its own `Running Start-Sleep...` and later `Reading ...`
  activity; the first completion left the parent non-Ready while its sibling
  remained active; both child cards retired independently; the parent then
  reached Ready; `/exit` removed it; correlation state ended empty.
