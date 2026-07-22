# Phase 25: Glyph ink-box centering on the accent tile

**Depends on:** phase 16 (the ERGO-28 theme-neutral accent tile + `GlyphRenderer`
/ `TileCompositor` this corrects). **Sequenced to EXECUTE NEXT — before phase 23** —
even though its number is higher: it is phase-22 AC12 dogfood fallout, and phase 23's
dogfood kit should ship centered glyphs. `progress.md` carries the true execution order.
**Unblocks:** nothing structurally; phase 23 soft-follows so the kit inherits the fix.

## Goal

Segoe Fluent Icons glyphs must sit **visually centered** on the accent tile. Today they
ride high: `GlyphRenderer.Render` centers with DirectWrite
`SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER)`, which centers the **line box**
(ascent + descent), not the glyph's **ink**. Segoe Fluent Icons glyphs reserve descent
space their ink does not fill, so the drawn glyph sits above the tile's true center. Found
live during phase 22's AC12 dogfood (the `Segoe:Error` "!" glyph, and by inspection every
curated Segoe glyph including the Robot default). Color emoji are unaffected — they render
bare (`onTile: false`), full-bleed, and fill their em box — and must stay exactly as they
are.

## Context (no open design decision — this is a defect fix)

"The glyph should be centered on the plate" is the obviously-correct behavior; there is no
fork to ratify. This corrects ERGO-28's tile compositing (phase 16) only in the vertical
(and, to be safe, horizontal) placement of the monochrome glyph — the accent color, the
white glyph color, the tile shape, the emoji path, and the `GlyphProbe`-first contract are
all unchanged.

## Approach (contract, not mechanism)

Center the glyph by its **ink bounding box**, not the font line box. Two viable mechanisms;
the executor picks one and justifies it:

1. **Metrics-driven offset (preferred if clean):** build an `IDWriteTextLayout` for the
   single glyph, read `DWRITE_TEXT_METRICS` (`top`/`height`/`left`/`width` — the drawn line
   box within the layout) and `DWRITE_OVERHANG_METRICS` (how far ink spills past each layout
   edge) to derive the true ink bounding box, then draw at an origin/rect translated so the
   ink box's center lands on the tile center. Keep `DrawText`/`DrawTextLayout` on the same
   zero-GPU `SoftwareCanvas` + `ENABLE_COLOR_FONT` path.
2. **Alpha-scan recenter:** render the glyph to a transparent scratch buffer, compute the
   ink bounding box by scanning non-transparent pixels, then composite that sub-image
   centered onto the tile. Deterministic and mechanism-simple; costs a second raster.

Either is acceptable. The single fixed 64px-per-glyph output, the PNG encoder, the cache
key, and `IconService`'s callers are untouched — only the pixels within the tile move.

## Files affected

```
src/Atv.IconRendering/GlyphRenderer.cs       # ink-box centering for the on-tile Segoe path
src/Atv.IconRendering/TileCompositor.cs      # only if the chosen mechanism needs a compositing helper
tests/Atv.IconRendering.Tests/*              # ink-centering assertion + emoji-unchanged regression
```

No main-project, manifest, or config change. The rendered-tile bytes change, so any test
that pinned exact Segoe-tile bytes updates deliberately (call it out).

## Acceptance criteria (written first)

1. **Ink is centered (the fix).** For a representative set of curated Segoe glyphs (at
   minimum: the `Error`/E783 glyph from the dogfood, the Robot default, plus a visually
   tall and a wide glyph), render the on-tile PNG, compute the bounding box of the **glyph
   pixels** (the white `TileCompositor.GlyphColor` ink against the accent fill), and assert
   its center is within a small tolerance (≈ ±2 px at 64px) of the tile center on **both**
   axes. This test must FAIL against the current `SetParagraphAlignment(CENTER)`
   implementation (show red) and pass after the fix.
2. **Emoji path unchanged.** A color-emoji render is still bare (`onTile: false`),
   full-bleed, and byte-for-byte what it was before this phase (regression pin) — the fix
   touches only the on-tile monochrome path.
3. **No render regressions.** `GlyphProbe`-first behavior, `RenderStatus.GlyphNotFound` for
   absent glyphs, the accent tile fill/shape, and the white glyph color are all unchanged;
   the full `Atv.IconRendering.Tests` suite and `tests/Atv.LogicTests` (which exercise
   `IconService`) stay green (any deliberately-updated byte-pin is called out).
4. **Build clean.** Solution builds 0/0; NativeAOT `win-arm64` publish is trim/AOT-warning
   clean; exe size stays within the documented 3–5 MB band.
5. **LIVE — operator re-eyeball (INFRA-33 conduct: `atv-dev`, never bare `atv`).** Recreate
   a card whose repo-hash pick lands on a Segoe glyph (e.g. anchor the card so `doctor`'s
   would-pick line reports a `Segoe:*` token) and confirm on the taskbar that the glyph is
   now visually centered on the plate. Orchestrator drives; the eyeball is operator-manual.

## Out of scope

- The emoji render path (already correct) and the tile's color/shape (ERGO-28, unchanged).
- Any change to the pick recipe, pool, cache, or `IconService` fallback chain (phase 22 /
  phase 7).
- Sub-pixel perfection or per-glyph optical-centering tuning beyond the ±tolerance — the
  goal is "not visibly off-center," not typographic optical alignment.
