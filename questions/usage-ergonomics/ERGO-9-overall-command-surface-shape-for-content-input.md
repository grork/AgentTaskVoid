# ERGO-9: Overall command-surface shape for content input
**Status:** DECIDED
**Plan:** phase-02
**Decision:** Verb-per-operation subcommands mapping to lifecycle moments:
`start`, `step`, `state`, `done`, `fail`, `attention`, `remove` (+ `list`). Not
one-command-many-flags, not JSON-on-stdin. Content shapes narrow to what those
verbs need; richer shapes are additive later.
**Parent:** ERGO-3

Subcommand per content shape (steps / thumbnail / summary / assets), one
command with many flags, or structured input (e.g. JSON on stdin) for complex
payloads? Four factory shapes plus three mutators make a single flat flag list
awkward -- what shape scales?

Decision detail (2026-07-02):
- v1 surface: `start <h> --title ...`, `step <h> "..."`, `state <h>
  running|paused`, `done <h> [--summary ...]`, `fail <h> [--summary ...]`,
  `attention <h> "question"`, `remove <h>`, `list`.
- Each verb maps to an agent lifecycle moment (SessionStart->start,
  Pre/PostToolUse->step, Notification/permissionRequest->attention,
  Stop/SessionEnd->done, ...), so hooks are one-liners and this verb set doubles
  as the host-agnostic abstraction (LIFE-10).
- Content shapes used: `CreateSequenceOfSteps` (start/step/state/bare done/fail),
  `SetQuestion` on SequenceOfSteps (attention -- safe cell only, ERGO-10),
  `CreateTextSummaryResult` (done/fail --summary). Deferred (additive, no
  breaking change): `show` (PreviewThumbnail), `done --assets`
  (GeneratedAssetsResult).
- Grows by adding verbs, not reshaping existing ones (operator noted this
  future-growth property explicitly).
