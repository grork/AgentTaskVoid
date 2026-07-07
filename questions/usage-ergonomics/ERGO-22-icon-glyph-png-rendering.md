# ERGO-22: Icon glyph -> PNG rendering
**Status:** DECIDED
**Decision:** Render glyph -> PNG with DirectWrite + Direct2D + WIC via a SOFTWARE WIC
bitmap render target (zero-GPU: no D3D device, no driver dependency, deterministic),
using source-generated COM interop for NativeAOT (INFRA-3, "Writing the tool in C#
targeting NativeAOT"). One path renders both color emoji and monochrome Segoe glyphs;
zero added binary size (all system DLLs). Skia rejected (~8MB native dep vs INFRA-2,
"Minimizing on-disk size").

Structure -- the rendering lives in a DISCRETE project (project-to-project reference
from main), quarantining the interop:
- Rendering project = pure mechanism. Input: glyph spec (emoji char / Segoe codepoint +
  pixel size). Output: PNG bytes (or a "glyph not present" signal). Owns the
  D2D/DWrite/WIC interop, color+mono rendering, the glyph-presence probe, and the
  primitive drawn-shape fallback renderer. No filesystem, no handles, no caching, no
  policy. Has its OWN unit tests for basic generation (emoji -> valid PNG NxN, Segoe
  codepoint -> PNG, missing glyph -> NotFound, drawn-shape -> PNG).
- Main project owns everything contextual: the canonical render-once cache (keyed by
  (glyph, size) hash), per-handle copies + grouping/placement (ERGO-13/15), where PNGs
  live (package app-data), lifecycle (create/remove/sweep), and the fallback POLICY
  (chosen glyph -> ERGO-12 default glyph -> ask rendering for the drawn shape). Render
  failure falls back + logs (FAIL-3), non-disruptive (FAIL-1).

Sizes: one PNG per glyph, generously sized (~48-64px; Shell downscales). Exact px and
any DPI-scale awareness deferred to implementation -- the platform likely won't do
MRT-style `scale-150` lookups, so we may need to render DPI-aware (flagged for impl).

ERGO-20 decided callers pick a built-in icon by token (emoji or a curated Segoe
Fluent/MDL2 glyph); the API needs an image URI, so the CLI must render the chosen
glyph to a PNG. That rendering path was waved through -- design it:
- Rendering tech: emoji (color) and Segoe monochrome glyphs -> PNG via
  DirectWrite/Direct2D, GDI+/System.Drawing, or SkiaSharp -- must be
  NativeAOT-compatible (INFRA-3) and keep binary size down (INFRA-2). Windows-only,
  so Win32/DirectX paths are fair game.
- Sizes: taskbar icon dimensions (e.g. 44x44 and friends); pre-render a set or one?
- Caching + grouping: render once per glyph, then place copies at per-handle /
  per-group paths so identical glyphs still separate into distinct taskbar icons
  (ERGO-13/15). Where the PNGs live (package app-data).
- Fallback when a chosen glyph/codepoint is unavailable on the target build.
