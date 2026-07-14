# Phase 16: Icon pipeline v2 — theme-neutral tile + caller-supplied images

**Depends on:** phase 07 (rendering project + icon management), phase 15 (the
`--icon-file` flag attaches to the v2 upserting verbs — sequencing after 15 avoids
landing it on verbs 15 deletes). The rendering work itself is 15-independent.
**Unblocks:** phase 17 (`icon-file` as a repo-config allowlist key).

## Goal

Fix the out-of-box contrast problem (solid-black glyphs on a dark taskbar) with a
theme-neutral tile treatment, and promote bring-your-own-image to a supported,
normalized, validated first-class input — extending `Atv.IconRendering` and the
`IconService` ownership model, changing nothing about grouping physics or icon
immutability.

## Decisions implemented

### Theme-neutral tile (ERGO-28, "Theme-awareness of the icon provided to the platform")

- Render monochrome glyphs as a **contrasting glyph on a filled rounded-rect /
  squircle tile** in a fixed accent color — ONE static asset that reads on any
  taskbar theme, matching how real app icons look. **No runtime theme reaction**:
  detect-at-render and watchdog re-render (Remove+Create) are rejected — they fight
  icon immutability (ERGO-25) and the URI grouping key (ERGO-13) for marginal gain.
- The **default Robot glyph (ERGO-12) becomes a white robot on the accent tile**, so
  the out-of-box case is fixed.
- **Build details, not re-decisions** (pick during implementation, record the
  choice): exact accent color + corner radius; whether color emoji (already
  theme-safe) also get the tile for visual consistency or render bare;
  high-contrast mode is a documented v1 caveat.
- Caller-supplied raster logos can't be recolored by us — contrast there is the
  caller's; at most padded onto the same tile (build detail: probably bare/full-bleed).

### Bring-your-own image (ERGO-29, "Caller-supplied (external) icons")

- A dedicated **`--icon-file <path>`** flag on the v2 upserting verbs — unambiguous
  against the `--icon` token/emoji space at parse time. Supplying both `--icon` and
  `--icon-file` on one call is a usage error.
- Promotes the hidden `RawPath` hatch (`IconTokenKind.RawPath` in
  `src/Atv/Icons/IconTokens.cs`) into a supported, **normalized** input: accept
  common raster formats (**PNG/JPG/ICO**), fit to the pipeline's 64px PNG (downscale,
  aspect-preserving pad), flatten transparency, and **cache per-handle exactly like
  rendered glyphs** — lifecycle-twinned with the sidecar entry, single-owner move
  model through the recycle bin (ERGO-23; the phase-07 ownership machinery, one new
  source).
- **Bounds + validation** (the trust surface): format allowlist, dimension/byte-size
  caps, malformed-image rejection, path handling that never traverses or writes
  outside the icon store. Failures follow the non-disruptive posture: fall back down
  the ERGO-12/ERGO-22 chain (default glyph → drawn shape), log durably (FAIL-1,
  FAIL-3), exit 0.
- **Grouping consequence, documented not changed:** the cached per-handle path is the
  grouping key (ERGO-13, ERGO-15) — two callers supplying the "same" logo by
  different paths do not glom.
- **Sub-question 1.1 (extract an icon from an exe/AUMID) stays DEFERRED** — no
  `--icon-from-exe`/`--icon-from-app` surface this phase.

## Files affected

```
src/Atv.IconRendering/TileCompositor.cs (new)   # squircle tile + glyph compositing (D2D, software target)
src/Atv.IconRendering/RasterNormalizer.cs (new) # WIC decode PNG/JPG/ICO → fit 64px → flatten → PNG bytes
src/Atv.IconRendering/GlyphRenderer.cs          # route monochrome glyphs through the tile treatment
src/Atv/Icons/IconTokens.cs                     # RawPath hatch folded into the supported input
src/Atv/Icons/IconService.cs                    # icon-file source: validate → normalize → per-handle cache
src/Atv/Cli/CommandLine.cs, Dispatcher.cs       # --icon-file on the upserting verbs; conflict rule
tests/Atv.IconRendering.Tests/*                 # tile + normalization rendering tests
tests/Atv.LogicTests/Icons/*, Cli/*             # validation, caching/ownership, parse tests
docs/…                                          # README/config docs: --icon-file, contrast/theming notes, high-contrast caveat
```

## Acceptance criteria (written first)

1. **Tile rendering:** a monochrome Segoe glyph composites onto the accent tile
   (deterministic software target, valid PNG at the pipeline size); the default
   Robot token produces the white-on-accent-tile asset; the emoji tile-or-bare
   choice is made and covered by a test recording it.
2. **Normalization:** PNG, JPG, and ICO inputs each yield a 64px PNG; oversized
   images downscale; non-square images fit with aspect-preserving padding;
   transparency is flattened.
3. **Validation:** files over the byte cap, disallowed formats, and malformed image
   data are rejected → fallback chain engages, durable log entry written, exit 0
   (non-disruptive posture). Path handling proven safe (no traversal out of the
   icon store; source file only ever read).
4. **Ownership parity:** per-handle copies sourced from `--icon-file` behave exactly
   like glyph copies through remove-reap, expiry tombstone move, resurrection
   move-back, and TTL purge (extend the phase-07 ownership tests to the new source).
5. **CLI surface:** `--icon-file` parses on every upserting verb; `--icon` +
   `--icon-file` together is a usage error; the hidden RawPath behavior is gone or
   folded per the implementation choice, with tests matching.
6. **NativeAOT publish clean**; binary size delta recorded (WIC decoders are system
   DLLs — expect ≈0; INFRA-2's accepted 3–5 MB band applies).
7. **Manual dogfood:** the operator eyeballs on the real taskbar (a) a default-icon
   card showing the robot-on-tile treatment legibly on the current theme, and (b) a
   `--icon-file` card wearing a supplied logo. Light-theme spot check if convenient;
   otherwise the tile's theme-neutrality rests on the design (record which).

## Out of scope

Exe/AUMID icon extraction (ERGO-29 sub-question 1.1, DEFERRED); any runtime
theme-change reaction (rejected in ERGO-28); repo-supplied `icon-file` defaults
(phase 17); high-contrast support beyond the documented caveat.
