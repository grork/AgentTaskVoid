# LIFE-9: Per-host hook surface inventory
**Status:** EXPANDED
**Expanded into:** LIFE-12, LIFE-13, LIFE-14
**Parent:** LIFE-2

For each in-scope host (LIFE-8): what hook/event surface exists -- events
offered, payloads, session identifiers, and whether a reliable session-end
signal exists. Needs expanding into one question per host once LIFE-8 fixes
the list.

Expansion note (2026-07-02): expanded into one inventory question per host
already named in the brief / LIFE-2 (Claude Code, GitHub Copilot CLI, Codex)
rather than waiting on LIFE-8 -- those three are in scope under any plausible
LIFE-8 outcome; hosts LIFE-8 adds later spawn further sibling questions. Each
child captures the same six dimensions, chosen because existing questions
consume them:

1. Events: which hooks exist and when they fire -- especially session
   start/end, tool-call request/approval, and attention/notification moments
   (feeds LIFE-10; LIFE-4/LIFE-7 need to know whether a reliable session-end
   signal exists).
2. Payload & transport: what data each event carries and how it is delivered
   (stdin JSON, env vars, args) (feeds LIFE-10, ERGO-6).
3. Session identity: is there a stable session ID usable as a task handle or
   group key (feeds ERGO-6, ERGO-15).
4. Blocking semantics: does the host wait on the hook, what timeouts apply,
   and can a hook return data/decisions to the host (feeds INTER-2/INTER-3,
   INFRA-12).
5. Failure handling: what the host does when a hook exits nonzero or hangs
   (feeds FAIL-1).
6. Configuration: how hooks are wired in -- file format, global vs per-project
   (feeds LIFE-11).
