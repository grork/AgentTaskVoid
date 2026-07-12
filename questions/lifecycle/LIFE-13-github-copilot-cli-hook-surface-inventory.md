# LIFE-13: GitHub Copilot CLI hook surface inventory
**Status:** DECIDED
**Plan:** phase-13
**Decision:** Formal 13-event hook system (best session-end of the three).
Session id: YES (`sessionId`/`session_id`, also on disk under
`~/.copilot/session-state/<id>/`). Session-END: RELIABLE-ISH (`sessionEnd` with
reason=complete|error|abort|timeout|user_exit; SIGKILL/hard-crash unconfirmed).
(Sonnet host research, 2026-07-02.)
**Parent:** LIFE-9

Inventory GitHub Copilot CLI's hook/extension surface against the six
dimensions in LIFE-9's expansion note. If no usable hook system exists, that
finding *is* the answer -- and feeds LIFE-3 (whether hook-based coverage is
untenable for some hosts).

Inventory (research 2026-07-02; the agentic `copilot` CLI, npm @github/copilot,
GA 2026-02-25; sources docs.github.com/copilot):
1. Events (13): sessionStart, userPromptSubmitted, preToolUse, postToolUse,
   postToolUseFailure, preCompact, permissionRequest (CLI-only), agentStop,
   subagentStart, subagentStop, errorOccurred, notification (CLI-only),
   sessionEnd. "Needs user" = permissionRequest + notification; turn-done =
   agentStop.
2. Payload/transport: command (stdin JSON), HTTP POST (https required for
   preToolUse/permissionRequest), prompt. Fields in both camelCase (sessionId,
   toolName, toolArgs) and snake_case (session_id, tool_name, tool_input).
3. Session identity: sessionId/session_id in every payload; per-session dir on
   disk (a second, file-based correlation path).
4. Blocking: synchronous; preToolUse/permissionRequest = approve/deny/modify.
5. Failure: exit0 ok, exit2 warn/deny; other/timeout fail-OPEN -- EXCEPT
   preToolUse command hooks fail-CLOSED on crash/nonzero (open on timeout).
6. Config: ~/.copilot/ (user) + .github/copilot/ (project; settings.local.json
   gitignored); enterprise policy.d hooks cannot be disabled.
