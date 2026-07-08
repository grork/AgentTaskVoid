# Phase 07: Icon pipeline — glyph rendering project + icon management

**Depends on:** phase 01 (solution layout), phase 04 (sidecar/recycle stores, AppPaths)
**Unblocks:** phase 08 (start's `--icon` and the icon default), phase 10 (clear's icon purge)

## Goal

Let callers name an icon by token (emoji or curated Segoe glyph) with no image files:
a quarantined rendering project turns glyphs into PNGs; the main project owns
caching, per-handle placement (the grouping mechanism), fallback policy, and cleanup
ownership.

## Decisions implemented

### Rendering project (ERGO-22, "Icon glyph → PNG rendering")

- A DISCRETE project (project-to-project reference from main) quarantining the
  interop. Pure mechanism: input = glyph spec (emoji char / Segoe codepoint + pixel
  size); output = PNG bytes or a "glyph not present" signal. No filesystem, no
  handles, no caching, no policy.
- Tech: DirectWrite + Direct2D + WIC via a SOFTWARE WIC bitmap render target —
  zero-GPU (no D3D device, no driver dependency, deterministic). One path renders
  both color emoji and monochrome Segoe glyphs. Source-generated COM interop
  (`[GeneratedComInterface]`) for NativeAOT compatibility. Zero added binary size
  (all system DLLs). Skia rejected (~8 MB native dep vs INFRA-2).
- Also owns: the glyph-presence probe and a primitive drawn-shape fallback renderer
  (for when even the default glyph is unavailable).
- Sizes: one PNG per glyph, generously sized ~48–64 px (Shell downscales). Flagged
  for implementation: the platform likely won't do MRT-style `scale-150` lookups, so
  check whether DPI-aware rendering is needed; pick one size and note the finding.

### Token vocabularies (ERGO-20, "Icon representation")

- Two built-in vocabularies, specified by token: (1) **emoji** — literal character
  accepted (agents pick apt ones unprompted); (2) a **curated subset of Segoe
  MDL2 / Segoe Fluent Icons** codepoints — build the curated list during this phase
  (glyphs that render well at taskbar size and exist on target builds). A raw
  file-path escape hatch may ship as advanced input — decide during implementation;
  default to including it only if trivial.
- Default icon (ERGO-12): a built-in default glyph used when the caller supplies no
  `--icon`.

### Icon management in main (ERGO-13, ERGO-15, ERGO-23)

- Grouping is keyed on the exact icon URI string/path (ERGO-13 — operator-asserted),
  and the v1 default is SEPARATE-BY-SESSION (ERGO-15): the CLI writes each task's
  icon to a PER-HANDLE path, so each handle is its own taskbar icon even when glyphs
  are identical. No grouping knob in v1 (ERGO-14).
- Two file populations + the recycle bin third (ERGO-23, single-owner "move" model —
  ownership is positional, no refcounting):
  1. **Canonical render-once cache** keyed by (glyph, size) hash — a pure regenerable
     accelerator: opportunistic age/LRU prune, safe to wipe anytime.
  2. **Per-handle copies** (the grouping keys) — lifecycle-twinned with the sidecar
     entry: reaped on the same events that drop the entry (remove / clear /
     user-hide sweep / reconciliation-drop).
  3. On expiry-tombstone the per-handle copy MOVES into the recycle-bin folder beside
     the record; resurrect moves it back to a live per-handle path; TTL/reboot purge
     deletes record + co-located icon together. Each asset physically lives in ONE
     place at a time (live XOR recycle) — an icon referenced by a live or
     recycle-binned handle can never be freed from under it, structurally.
- Fallback POLICY (main project): chosen glyph → default glyph (ERGO-12) → drawn
  shape (rendering project). Render failure falls back + logs (FAIL-3),
  non-disruptive (FAIL-1).
- Orphan-icon backstop sweep: icon files with no owning handle and no recycle record
  are reaped aggressively (identity-scoped, safe), bulk reaps logged, no guard
  (follows the LIFE-23 ruling).
- Icons are IMMUTABLE per task (no `UpdateIcon`; `IconUri` set only at Create). A
  "change the icon" is Remove+Create under the hood (phase 05 upsert handles it) —
  old copy reaped by the remove path, new copy written by create. No special rule.
- Wiring ownership (ratified 2026-07-07): THIS phase attaches icon cleanup to the
  cleanup paths phases 04/05 built icon-unaware — entry-drop/reconciliation reap,
  remove reap, expiry tombstone move, resurrection move-back. Phases 08–10 wire the
  verb-level triggers as already planned.

## Files affected

```
src/Atv.IconRendering/Atv.IconRendering.csproj
src/Atv.IconRendering/GlyphRenderer.cs        # D2D/DWrite/WIC interop, color+mono
src/Atv.IconRendering/GlyphProbe.cs           # presence check
src/Atv.IconRendering/ShapeRenderer.cs        # drawn-shape fallback
src/Atv/Icons/IconTokens.cs                   # emoji parsing + curated Segoe list + default token
src/Atv/Icons/IconService.cs                  # cache, per-handle placement, fallback policy, move/reap operations
tests/Atv.IconRendering.Tests/ (or a folder in LogicTests)  # rendering unit tests
tests/Atv.LogicTests/Icons/*                  # management tests (temp-dir injected)
```

## Acceptance criteria (written first)

1. Rendering unit tests: emoji → valid PNG N×N; Segoe codepoint → PNG; missing glyph
   → NotFound signal (no throw); drawn shape → PNG. Deterministic (software target)
   and green on a machine with no GPU assumptions.
2. Cache: same (glyph, size) renders once, second request served from cache;
   per-handle copies are distinct files at distinct paths even for the same glyph
   (the separation mechanism).
3. Ownership: remove/drop reaps the per-handle copy; tombstone MOVES it into the
   recycle folder; resurrect moves it back; TTL purge deletes record + icon
   together; wiping the canonical cache breaks nothing (re-render).
4. Fallback chain: unavailable chosen glyph falls to default; unavailable default
   falls to drawn shape; each fallback logs.
5. NativeAOT publish clean (source-gen COM interop only); binary size increase from
   this phase ≈ 0 (system DLLs only).
6. Manual dogfood: a card created with an emoji token and one with a Segoe token
   render correctly on the real taskbar.

## Out of scope

Wiring `--icon` into `start` (phase 08), clear's bulk icon purge (phase 10),
expiry-time moves being invoked (phase 09 calls the move operations built here).
