# ERGO-25: `start` on an already-live handle (re-entrancy semantics)
**Status:** DECIDED
**Decision:** `start` is upsert/idempotent -- a `start` on a live (or recycle-bin, LIFE-21
"What expiry does") handle adopts that card, re-applies title/subtitle/state, and PRESERVES
step history; it never errors. `--reset` (or explicit `remove` then `start`) forces a clean
slate.

Detail (2026-07-05):
- Rationale: the resume/re-fire case is the dominant, automated one -- Copilot CLI and Codex
  session ids survive resume (LIFE-13, "GitHub Copilot CLI hook surface inventory" / LIFE-14,
  "Codex hook surface inventory") so SessionStart re-fires the same handle, and constant-handle
  scripts (ERGO-6, "The identifier a caller holds") re-run. Upsert makes hooks safe to re-fire
  (LIFE-11, "Whether we ship per-host integration artifacts") and never destroys progress.
  Error/no-op rejected as hostile to resume; unconditional reset rejected as destroying progress
  on every resume.
- Step model (ERGO-8, "Update verbs"): re-`start` does NOT clear `completedSteps`; it only
  updates the fields `start` carries. `--reset` clears steps and returns the card to its initial
  state.
- Icon caveat (ERGO-23, "Clean up of sidecar files"): icons are immutable, so a re-`start` that
  specifies a DIFFERENT icon token than the live card forces a platform Remove+Create and loses
  step history -- unavoidable. The resume case normally reuses the same (host-config) icon, so
  continuity holds.
- Validator (ERGO-10, "Guarding unsupported combinations"): `start`-on-a-live-handle is VALID,
  not an unsupported combination.
- Recycle-bin caveat (clarified 2026-07-05): "preserves step history" applies to the LIVE-handle
  path only. A recycle-binned handle has no stored steps (LIFE-21 stores nothing mutable), so a
  resurrecting `start` yields the restored core info (title, subtitle, icon, deepLink -- per the
  2026-07-05 LIFE-21 record amendment) with a fresh step sequence.

Nothing decides what `start <handle>` does when the handle is already live -- and it will
happen routinely: Copilot CLI and Codex session ids SURVIVE resume (LIFE-13, "GitHub
Copilot CLI hook surface inventory" / LIFE-14, "Codex hook surface inventory"), so a
resumed session fires SessionStart again with the same handle; scripts using a constant
handle (sanctioned by ERGO-6, "The identifier a caller holds") re-run; and LIFE-21's
("What expiry does") resurrection path already defines the adjacent
miss-plus-recycle-bin-hit variant.

Options: reset/replace (fresh card -- loses prior steps; note icon immutability, ERGO-23,
"Clean up of sidecar files", means a changed icon token forces Remove+Create anyway),
update-in-place (treat as re-title/re-state, keep the step history), or error/no-op
(hostile to the resume case). Whatever is chosen must thread through the ERGO-10
("Guarding unsupported combinations") validator, the ERGO-8 ("Update verbs") advance
model (does re-start clear `completedSteps`?), and the LIFE-11 ("Whether we ship per-host
integration artifacts") hook wiring, which must be safe to re-fire.

Surfaced by the 2026-07-05 review pass.
