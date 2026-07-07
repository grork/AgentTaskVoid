# LIFE-14: Codex hook surface inventory
**Status:** DECIDED
**Decision:** Two systems -- legacy single-event `notify` + a modern hooks
framework -- but NO session-end hook at all. Session id: YES (`session_id` /
legacy `thread-id`, survives `codex resume`). Session-END: NONE (no
session-end/shutdown event in source; Stop is per-turn, skipped on Ctrl+C/kill).
(Sonnet host research, 2026-07-02.)
**Parent:** LIFE-9

Inventory Codex's hook/notification surface against the six dimensions in
LIFE-9's expansion note. Same note as LIFE-13: absence of a usable surface is
itself a finding that feeds LIFE-3.

Inventory (research 2026-07-02; Rust `codex`, github.com/openai/codex main ~Jul
2026 -- ahead of most blog posts that only mention `notify`):
1. Events: legacy notify = agent-turn-complete only (fire-and-forget). Modern
   hooks: SessionStart, SubagentStart, UserPromptSubmit, PreToolUse,
   PermissionRequest, PostToolUse, PreCompact, PostCompact, Stop, SubagentStop.
   NO SessionEnd. "Needs user" = PermissionRequest/PreToolUse.
2. Payload/transport: legacy = JSON as a single argv arg, kebab-case (thread-id,
   turn-id, ...). Modern = JSON on stdin, snake_case (session_id, turn_id, cwd,
   transcript_path, model, + tool_name/tool_input/tool_response).
3. Session identity: session_id (ThreadId UUID) in every modern payload;
   thread-id in legacy; reusable via `codex resume`.
4. Blocking: modern hooks block (PreToolUse/PermissionRequest can Deny);
   per-handler timeout_sec; legacy notify is fire-and-forget (cannot block).
5. Failure: HookResult Success/FailedContinue/FailedAbort; spawn/timeout/bad-JSON
   captured, does not crash Codex.
6. Config: layered user/project/session/managed; hooks.json or [hooks] in
   config.toml; /hooks TUI; legacy notify=[...] top-level key.
