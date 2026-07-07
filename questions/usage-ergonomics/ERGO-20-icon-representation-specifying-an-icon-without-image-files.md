# ERGO-20: Icon representation -- specifying an icon without image files
**Status:** DECIDED
**Decision:** Two built-in icon vocabularies, both specified by token and
rendered to a PNG by the CLI: (1) **emoji** -- because agents/LLMs know them well
and can pick an apt one unprompted; (2) a **curated subset of Segoe MDL2 / Segoe
Fluent Icons** codepoints (constrained to supported glyphs) -- because they fit
the Windows 11 aesthetic. A raw file path may remain as an advanced escape hatch.

Consumers (hooks, scripts) won't have image files lying around, so raw file
paths are a poor primary interface. The CLI should provide an enumerated set of
built-in icons the caller names by token, and render the chosen glyph to an image
file the API can reference (the API needs an image URI, and grouping is keyed by
that URI path -- ERGO-13/14/15).

Decision detail (2026-07-02): the visual (glyph) is separate from the grouping
key (the path the CLI writes it to) -- two sessions can share the same glyph yet
render as two icons because their paths differ (ERGO-13/15). Residual build-time
detail: exact emoji spec syntax (literal char vs name), the curated Segoe
codepoint list (glyphs that render well at taskbar size and exist on target
builds), and whether the raw file-path hatch ships in v1. Feeds ERGO-12.
