# LIFE-12: Claude Code hook surface inventory
**Status:** DECIDED
**Decision:** Rich hook system. Session id: YES (`session_id`, changes on
`--resume`). Session-END signal: UNRELIABLE (`SessionEnd` fires on clean/explicit
exit only, not Ctrl+C/kill/closed terminal). (Sonnet host research, 2026-07-02.)
**Parent:** LIFE-9

Inventory Claude Code's hook system against the six dimensions listed in
LIFE-9's expansion note (events, payload/transport, session identity,
blocking/timeouts, failure handling, configuration).

Inventory (research 2026-07-02, ~v2.1.x; sources code.claude.com/docs/en/hooks):
1. Events: core 9 = SessionStart, SessionEnd, UserPromptSubmit, PreToolUse (can
   block), PostToolUse, Notification, Stop, SubagentStop, PreCompact; a larger
   newer set exists (PostToolUseFailure, PermissionRequest, ...) -- verify vs
   installed version. "Needs user" = Notification (permission_prompt /
   idle_prompt). Turn-done = Stop (NOT on interrupt).
2. Payload/transport: JSON on stdin. Every event carries session_id,
   transcript_path, cwd, hook_event_name, permission_mode; tool events add
   tool_name/tool_input (+ tool_response post).
3. Session identity: session_id (UUID) in every payload; NOT stable across
   `--resume` (use transcript_path for cross-resume continuity).
4. Blocking: synchronous unless async:true; ~600s default timeout; exit 2 blocks
   on blockable events; stdout JSON can carry decisions.
5. Failure: nonzero(non-2)/timeout/bad-JSON -> non-blocking error, continues.
6. Config: hooks block in settings.json, layered user/project/local/managed;
   matching hooks merge and run in parallel.
