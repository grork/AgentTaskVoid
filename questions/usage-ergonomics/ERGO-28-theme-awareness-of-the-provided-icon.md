# ERGO-28: Theme-awareness of the icon provided to the platform
**Status:** OPEN

## Question
How should a task's icon adapt (if at all) to the user's Windows theme -- light/dark,
and high-contrast -- given that the icon we hand the platform is a single static bitmap?

## Why this surfaced
The phase-07 icon pipeline (ERGO-22, "Icon glyph -> PNG rendering") renders monochrome
Segoe Fluent Icons glyphs as SOLID BLACK on transparent. On the Windows 11 taskbar --
which is dark by default -- a solid-black glyph can be low-contrast to near-invisible.
The ERGO-12 ("Defaults for parameters that are secretly required") default icon is itself
a monochrome Segoe glyph (Robot), so the out-of-the-box experience is directly exposed to
this. Color emoji (ERGO-20, "Icon representation") are theme-independent and unaffected.
Surfaced by an operator dogfood of the rendered sample PNGs, 2026-07-09.

## What makes it non-trivial (constraints)
- The `IconUri` we set is a PLAIN FILE PATH to one static PNG (phase-07 finding): the
  shell just scales that bitmap. There is NO MRT / `ms-resource` theme-qualifier
  mechanism (like `theme-dark`/`theme-light` asset variants) for the platform to
  auto-select from -- so "ship both variants and let the OS pick" is not available.
- Icons are IMMUTABLE per task (ERGO-22; ERGO-25, "`start` on an already-live handle"):
  `IconUri` is set only at Create. Recoloring an existing card's icon means Remove+Create,
  which LOSES step history -- expensive, and racy against a live task.
- Grouping is keyed on the EXACT icon URI string (ERGO-13, "Empirical: is grouping keyed
  on the exact icon URI string?"). A per-theme icon path would change the grouping key,
  interacting with ERGO-15's ("Default grouping when the consumer specifies nothing")
  separate-by-session model.
- The theme can change at RUNTIME; the watchdog is stateless-over-disk, so reacting to a
  theme switch is not free.

## Options to explore later (NOT deciding now)
1. Theme-neutral rendering: give monochrome glyphs a contrasting treatment that reads on
   any taskbar color -- a subtle outline/stroke or drop-shadow, or a filled rounded-"chip"
   background behind the glyph. One asset, works on light+dark, no runtime reaction needed.
   (Likely the cheapest good-enough answer.)
2. Detect theme at render time (registry `AppsUseLightTheme`) and pick glyph color
   accordingly -- but a static asset won't react to a LATER theme change without re-render.
3. Watchdog-driven re-render on theme-change -> Remove+Create with a recolored icon.
   Reacts correctly but pays the ERGO-25 step-history-loss cost and adds watchdog
   complexity.
4. Prefer color emoji / a mid-tone palette acceptable on both light and dark; document the
   monochrome caveat.
5. Accept solid-black and document it as a known v1 limitation.

## Scope note
Per the operator (2026-07-09): this does NOT change the current v1 build plan and is parked
to be looked at AFTER everything else is wrapped up. Filed OPEN so it is not lost.
