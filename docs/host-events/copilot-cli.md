# GitHub Copilot CLI host events

The Copilot CLI leg of the host-event recorder pattern described in
`docs/host-events/README.md`. Its conduit template, staging step, scripted
driver, and supervised cue script live under
`tools/host-event-recorder/hosts/copilot-cli/`.

## Documentation and host stamp

- Installed host at authoring time: GitHub Copilot CLI 1.0.71
  (`copilot --version`, 2026-07-16).
- Primary references fetched 2026-07-16:
  - <https://docs.github.com/en/copilot/reference/hooks-reference>
  - <https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-plugin-reference>
  - <https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/plugins-creating>

Event-to-state implications below are grounded in the real captures.

## Safe/skip matrix (INFRA-26)

Classification axis: does registering a passive recorder that consumes stdin,
emits no stdout, and exits 0 suppress or replace the host's normal behavior?

All 13 documented Copilot CLI events are safe to camp. Decision-capable hooks
fall through when they emit no output. `preToolUse` has one failure caveat: a
command hook crash or nonzero exit is fail-closed and denies the tool call,
while a timeout is fail-open. The compiled recorder's fixed argv and
stdout-silent behavior are load-bearing for that reason.

| Event | Class | Why passive capture is safe | Runtime posture | Capture beat |
|---|---|---|---|---|
| `sessionStart` | Safe | Empty output injects no context; startup/resume proceeds normally. | Synchronous command hook | Fresh scripted + interactive start |
| `userPromptSubmitted` | Safe | Empty output neither responds for the model nor injects context. | Synchronous command hook | Every prompt |
| `preToolUse` | Safe with fail-closed caveat | Empty output leaves the normal permission/tool flow intact. | Synchronous; nonzero denies, timeout falls through | Successful, failed, and subagent tools |
| `postToolUse` | Safe | Empty output preserves the real successful tool result. | Synchronous command hook | Successful tools |
| `postToolUseFailure` | Safe | Empty output adds no recovery context and leaves the failure unchanged. | Synchronous command hook | Targeted with a nonzero child command; did not fire because the PowerShell tool itself returned a success envelope |
| `permissionRequest` | Safe | Empty output falls through to rules, existing approvals, or the real user prompt. | Synchronous command hook | Real permission prompt |
| `notification` | Safe | Observational; documented as fire-and-forget. | Host-asynchronous | Permission, shell, and background-agent notifications; elicitation targeted but absent |
| `agentStop` | Safe | Empty output allows the main agent turn to stop. | Synchronous command hook | Every completed turn |
| `subagentStart` | Safe | Empty output injects no extra subagent context. | Synchronous command hook | Parallel named subagents |
| `subagentStop` | Safe | Empty output allows the subagent to stop. | Synchronous command hook | Parallel named subagents |
| `errorOccurred` | Safe | Output is not processed. | Synchronous command hook | Incidental only; no synthetic host crash |
| `preCompact` | Safe | Output is not processed; compaction proceeds. | Synchronous command hook | Manual `/compact` |
| `sessionEnd` | Safe | Purely observational cleanup point. | Synchronous, `timeout: 10` | Normal `/exit` |

## Conduit implementation

The capture uses a local Copilot plugin (`plugin.json` + `hooks/hooks.json`)
loaded with `copilot --plugin-dir <path>`. It is never installed and never
touches user-level settings or hooks.

The template uses Copilot's native flat hook format and camelCase event names.
On Windows, each `powershell` command invokes the recorder as the sole child
process:

```text
& '<absolute host-event-recorder.exe>' --host copilot-cli --event <eventName>
```

PowerShell does not read or transform the hook payload; the recorder inherits
the hook's stdin handle directly. A real prompt-mode conduit probe on Copilot
CLI 1.0.71 confirmed that the full JSON payload reaches the recorder and that
`sessionEnd` lands before process exit. Unlike Claude Code's async-by-default
observer posture, current Copilot command hooks are synchronous; the template
contains no `async` property. `notification` alone is asynchronous by host
definition.

Prompt mode disables project extensions by default. The scripted driver opts
in only for its child process with
`GITHUB_COPILOT_PROMPT_MODE_EXTENSIONS=true`; interactive `--plugin-dir`
sessions need no such opt-in.

## Findings

**Verified against:** GitHub Copilot CLI 1.0.71, captured 2026-07-16.
Five gitignored capture files contain 112 records:

| Capture id | Records | Purpose |
|---|---:|---|
| `copilot-flat-probe` | 4 | Native-plugin conduit proof |
| `copilot-20260716-100256` | 43 | Scripted tools, failure attempt, and background fan-out |
| `copilot-interactive-1` | 13 | Basic interactive turn, permission baseline, clean exit |
| `copilot-interactive-2` | 48 | `ask_user`, real permission, sync fan-out, background shell, compaction, interrupt, clean exit |
| `copilot-resume-1` | 4 | Resume stability |

The main interactive session id was
`3bbb0815-bd30-435c-bb3f-dc077af65aae`; resuming it preserved that exact id.

### Per-event results

| Event | Result | Payload/behavior notes |
|---|---|---|
| `sessionStart` | **Fired** | Fields: `sessionId`, numeric-ms `timestamp`, `cwd`, `source`, optional `initialPrompt`. `source` was `new` or `resume`. It fired **after** `userPromptSubmitted`, not first. |
| `userPromptSubmitted` | **Fired** | Carries exact `prompt`. Also fires for Copilot's own `<system_notification>...</system_notification>` wake-up messages, so forwarding every payload as the user's goal would expose raw plumbing text. |
| `preToolUse` | **Fired** | Fields: `toolName`, `toolArgs` (a JSON string). Fires inside subagents, but those calls use a child `sessionId` such as `call_...` and carry no parent id or agent name. |
| `postToolUse` | **Fired** | Adds `toolResult { resultType, textResultForLlm }`. A PowerShell command exiting 7 still produced `resultType:"success"` because the shell tool itself completed and reported the child exit code in text. |
| `postToolUseFailure` | **Did not fire (targeted)** | The intentional exit-7 script was not a host-tool failure. A genuine tool-host failure remains unobserved. |
| `permissionRequest` | **Fired** | Fields: `toolName`, parsed `toolInput`, `permissionSuggestions`. It fired even under `--allow-all`/already-approved flows, so it does not mean a human is blocked. |
| `notification` | **Fired** | Observed `permission_prompt`, `shell_completed`, and `agent_idle`. A real `ask_user` dialog emitted no `elicitation_dialog` notification. |
| `agentStop` | **Fired** | Fires for both the main agent and subagents. Subagent instances used child `call_...` session ids and an empty `transcriptPath`; the main agent used the parent session id and real transcript path. The main agent can stop while background subagents are still active. |
| `subagentStart` | **Fired** | Carries parent `sessionId`, `agentName`/display/description, and the parent transcript path. It omits the caller-supplied task `name` and any unique instance id. |
| `subagentStop` | **Fired** | Same low-resolution identity as start (`agentName` only). Parallel differently-named agent types were distinguishable; concurrent same-type instances would not be. |
| `errorOccurred` | **Not exercised** | No genuine Copilot runtime/model error occurred; no synthetic host crash was induced. |
| `preCompact` | **Fired** | Manual `/compact` produced `trigger:"manual"`, transcript path, and empty `customInstructions`. |
| `sessionEnd` | **Fired** | Reasons observed: `complete` (prompt mode) and `user_exit` (interactive `/exit`). The synchronous hook landed on every real process exit. Prompt mode with background agents also emitted an early `complete` while workers were active, then another `complete` later for the same session id. |

### Key structural findings and translator implications

1. **Native flat hooks are the confirmed conduit.** The initial open-plugin
   nested exec form loaded, but Copilot ignored its `args` array and launched
   the recorder without `--host`/`--event`. The flat `powershell` form above
   preserved the raw payload and worked for every observed event.
2. **Startup is not the first event.** New, prompt-mode, interactive, and
   resumed sessions all emitted `userPromptSubmitted` before `sessionStart`.
   The first upserting semantic call must therefore be safe from any event.
3. **Resume identity is stable.** The resumed session retained
   `3bbb0815-bd30-435c-bb3f-dc077af65aae` and emitted
   `sessionStart.source:"resume"`. ERGO-25's idempotent upsert requirement is
   empirically confirmed for Copilot.
4. **Blocked has two proven sources, neither of which is a blanket
   `permissionRequest`.**
   - A genuine permission UI is proven by
     `notification.notification_type:"permission_prompt"`; its message contains
     the human-readable operation (`Fetch URL: ...`, `Run command: ...`).
   - `ask_user` is proven by `preToolUse.toolName:"ask_user"`; its JSON
     `toolArgs` carries the actual question and choices. Its matching
     `postToolUse` lands only after the operator answers.
   - `permissionRequest` alone is pre-service and fires on auto-approved paths;
     mapping it directly to Blocked would create false alarms.
5. **Copilot repeats system notifications as submitted prompts.** Both
   background-agent and background-shell notifications reappeared immediately
   as `userPromptSubmitted.prompt` wrapped in `<system_notification>`. The
   translator must reject this wrapper before mapping a prompt to
   `working --goal -`. This independently reproduces the Claude Code
   goal-pollution loose end (phase 19) on a second host.
6. **The lifecycle events alone are low-resolution, but the `task` tool exposes
   a richer correlation path.**
   - Parent `preToolUse:task`/`postToolUse:task` `toolArgs` contains the
     caller-supplied unique `name`, `agent_type`, `mode`, and exact child prompt.
   - The child's first `userPromptSubmitted` repeats that exact prompt under a
     unique child `sessionId` (`call_...`), allowing a stateful translator to
     correlate child session → parent + task name.
   - Synchronous task completion is visible in parent `postToolUse:task`.
     Background completion is visible in `notification:agent_idle`, whose
     message/title carries the caller-supplied unique name.
   - Raw `subagentStart`/`subagentStop` alone still cannot distinguish parallel
     same-type instances.
7. **Ready and cleanup must be gated.** Main `agentStop` and
   `sessionEnd.reason:"complete"` can occur while background workers are still
   active — the Copilot equivalent of the premature-`Stop` fan-out defect
   found on Claude Code. A translator that wants correct background fan-out
   needs its own tiny per-session correlation/count state, or it must
   deliberately degrade by ignoring these completion signals and relying on
   later cleanup. Interactive `sessionEnd.reason:"user_exit"` was a reliable
   terminal signal.
8. **Interrupt has no distinguishing completion event.** The interrupted
   PowerShell beat produced `preToolUse` (plus its permission events), then no
   `postToolUse`, `postToolUseFailure`, `agentStop`, or `errorOccurred`; the next
   terminal signal was `/exit`'s `sessionEnd:user_exit`. As with Claude Code,
   an orphaned pre-event is only an absence, not a routable hook.
9. **The synchronous process cost is material.** Across the 112 records, the
   payload timestamp to recorder append delta had a median of about 0.6 s,
   p95 about 2.3 s, and max about 4.3 s (host dispatch time is included,
   so this is not a pure process-start benchmark). A production plugin should
   minimize the number of synchronous hook lines and avoid mirroring both
   pre/post events unless the second event carries required semantics.

### Remaining uncertainty

- `errorOccurred` and a genuine `postToolUseFailure` payload are still
  unobserved; map conservatively and retain a `fatal` fallback.
- Cancellation of a background Copilot subagent was not exercised.
- The production plugin under `integrations/copilot-cli/` hashes the exact
  task prompt into a short-lived pending record, atomically claims it when
  the child repeats that prompt under `call_*`, and retains only the
  resulting `call_* -> parent/task` mapping through child completion. It
  never reads the internal Copilot transcript, never stores raw prompts, and
  refuses ambiguous matches rather than guessing.

### Production-plugin live dogfood (2026-07-16)

The finished plugin under `integrations/copilot-cli/` was loaded directly with
`copilot --plugin-dir` against the real dev `atv` package:

- Parent session plus two background workers rendered as three cards grouped
  together under the parent's exact icon URI.
- Prompt-hash claims resolved both child `call_*` streams: each child card
  independently showed `Running Start-Sleep -Seconds 30`, then its own
  `Reading ...` file activity.
- The first child retired while the second remained; the engine refused the
  premature Ready retry exactly as designed.
- The second child retired, the next Ready succeeded, and the parent displayed
  Completed.
- `/exit` delivered `sessionEnd:user_exit`; the parent disappeared.
- `atv list --json` and the plugin's `correlation-state.json` were both empty
  afterward.

The correlation bridge is proven against real hook ordering, real child-card
routing, the real taskbar, and real cleanup — not just the stub-`atv` harness.

One live limitation remains: a child-raised permission notification carries
only the parent session id, and Copilot publishes no permission-completed
hook. The plugin therefore blocks the parent locus; approving the prompt does
not clear it until later parent activity or final Ready. Claude's better
child attribution still only clears at `PostToolUse` (tool completion), so
widening Copilot's synchronous post-tool hooks would not solve a
long-running build and would add cost to every tool. The plugin does not use
a timer, read the internal transcript, or optimistically clear the block.
