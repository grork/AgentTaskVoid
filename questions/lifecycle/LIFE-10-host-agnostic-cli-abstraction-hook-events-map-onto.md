# LIFE-10: The host-agnostic CLI abstraction hook events map onto
**Status:** DECIDED
**Decision:** The verb set from ERGO-9 -- `start` / `step` / `state` / `done` /
`fail` / `attention` / `remove` -- is the host-agnostic abstraction. Every
in-scope host's events map onto these verbs with no host-specific logic in the
CLI; the mapping itself lives in per-host wiring (LIFE-11).
**Parent:** LIFE-2

What minimal set of CLI operations must exist so that every in-scope host's
events can drive the task lifecycle (create / update / needs-attention /
complete / remove) without host-specific logic living inside the CLI?

Decision detail (2026-07-02): confirmed by the host research (LIFE-12/13/14) --
all three expose the moments these verbs need: session start -> `start`;
pre/post-tool -> `step`; "needs user" (Notification / permissionRequest /
PermissionRequest) -> `attention`; turn/session done -> `done`/`fail`. The CLI
carries no host-specific branching; each host gets a thin hook config (LIFE-11)
translating its events into these verbs. (LIFE-11 -- the concrete per-host
artifacts -- remains the deferred 'later, not existential' part.)
