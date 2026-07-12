# ERGO-23: Clean up of sidecar files
**Status:** DECIDED
**Plan:** phase-07
**Decision:** Option A -- single-owner "move" model; ownership is positional, no refcounting.
- Per-handle index file + its icon copy are lifecycle-twinned: the copy is reaped on the same
  events that drop the index entry (`remove` / `clear` / user-hide / reconciliation-drop,
  ERGO-21, "The sidecar store design").
- On expiry-tombstone (LIFE-21, "What expiry does") the copy MOVES into the recycle-bin folder
  beside the record (which references it there); resurrect moves it back to a live per-handle
  path; TTL/reboot purge deletes record + co-located icon together. Because each asset is
  physically in ONE place at a time (live path XOR recycle folder), an icon referenced by a
  live OR recycle-binned handle can never be freed from under it -- structurally, not by guard.
- Canonical render-once cache (ERGO-22, "Icon glyph -> PNG rendering", keyed by (glyph,size))
  is a pure regenerable accelerator: opportunistic age/LRU prune, safe to wipe anytime (worst
  case = re-render). No ownership rules touch it.
- Orphan-file backstop sweep follows the LIFE-23 ("Entryless-orphan reaping and the
  mass-deletion guard") ruling: reap icon files with no owning handle and no record
  AGGRESSIVELY (identity-scoped, safe), log bulk reaps, no guard.

Icon is IMMUTABLE per task (verified against `docs/windows-ui-shell-tasks/AppTaskInfo.md`):
there is no `UpdateIcon`; `IconUri` is get-only, set only by `Create`, and it is the grouping
key. So there is NO in-place icon-change event to clean up after. A CLI "change the icon" can
only be `Remove()` + `Create()` under the hood -- old copy reaped by the remove path, new copy
written by create -- already covered, no special rule.

When we create the sidecar, it's not just a single file per-handle. There are images
that are copied into unique locations. We need to make sure that we have a strategy
for cleaning up those files -- potentially sweeping them. See ERGO-2

Note (2026-07-04): ERGO-22 ("Icon glyph -> PNG rendering") creates TWO image
populations -- the per-handle copies (the grouping keys, ERGO-13/15) AND the
canonical render-once cache keyed by (glyph, size). Cache pruning is a distinct
wrinkle from per-handle copy cleanup; the answer should cover both.

Note (2026-07-05): LIFE-21 ("What expiry does") adds a THIRD file population -- the recycle-bin
folder of restore-records, each carrying an icon-ref. LIFE-21 already defines its lifecycle
(opportunistic ~1-day TTL purge + reboot-clear); ERGO-23 must ensure a purged record's icon
asset is reaped with it, and reconcile ownership so an icon still referenced by a live OR
recycle-binned handle is never freed from under it.
