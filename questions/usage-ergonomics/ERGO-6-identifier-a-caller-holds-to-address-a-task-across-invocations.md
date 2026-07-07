# ERGO-6: The identifier a caller holds to address a task across invocations
**Status:** DECIDED
**Decision:** A caller-supplied handle (Fork B). Agent harnesses pass their own
session id; scripts pass their own string. **[AMENDED 2026-07-05 (ERGO-27, "The consolidated
v1 command surface"): the earlier "or omit it to address a single shared global default task"
affordance is REJECTED -- a caller-supplied handle is REQUIRED on every lifecycle verb; the tool
never creates or addresses a task without one, and `start` with no handle is an error.]** No
PID/cwd auto-derivation. The CLI need not return an id,
which frees stdout/exit-code for error signaling.
**Parent:** ERGO-1

Is it the API's own `AppTaskInfo.Id` passed through, or a caller-chosen
key/handle the CLI resolves for them? And how is it returned to the caller --
stdout (plain? JSON?), given process exit codes are too narrow to carry an ID?

Decision detail (2026-07-02):
- The handle is caller-supplied, not the API's opaque `Id`. Agent hosts pass the
  session id they already have -- contingent on each host actually exposing one
  (the session-identity dimension of LIFE-12/13/14; Fork B rests on this holding
  for Claude Code / GHCP CLI / Codex, with a fallback needed for any host that
  lacks it).
- Scripts calling the CLI directly supply their own handle. A constant string is
  fine for non-concurrent scripts (installers, copies) even though it is
  effectively global; a script wanting uniqueness generates its own -- a
  conscious choice. (Omitting the handle is no longer permitted -- see the amendment above; a
casual script supplies a constant string.)
- Rejected: deriving the handle from calling-process PID (+cwd). Modern agents
  and tools bounce through wrapper/launcher processes, so the durable PID is
  unknowable -- unreliable as an identity key.
- Because the caller owns the handle, `create`/`update` need not emit an id for
  the caller to capture. This minimises scripts holding state and frees the exit
  code to carry error status (FAIL-2).
- Wrapper mode (ERGO-5) is the exception: there the CLI owns id generation, a
  distinct scenario handled under ERGO-5.

Validation (Sonnet host research, 2026-07-02): Fork B's load-bearing assumption
holds -- Claude Code, Copilot CLI, and Codex each expose a stable session id in
every hook payload (LIFE-12/13/14). Caveat: Claude Code's `session_id` changes
across `--resume`; if cross-resume continuity ever matters, key off its
`transcript_path` instead. Codex's and Copilot's ids survive resume.
