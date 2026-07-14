# ERGO-29: Caller-supplied (external) icons — bring-your-own image, and extraction from an exe/app
**Status:** DECIDED (2026-07-13)
**Plan:** unplanned
**Decision:** Support **bring-your-own image** via a dedicated `--icon-file <path>` flag
(unambiguous against the `--icon` token/emoji space, option 2). It promotes today's hidden
`RawPath` hatch into a supported, normalized input: accept common raster formats (PNG/JPG/ICO),
fit to 64px, flatten transparency, cache per-handle like rendered glyphs, with size/format
bounds + validation (path traversal, huge/malformed files). Consumed into a future
icon-pipeline phase (extends `Atv.IconRendering`; pairs with ERGO-28's squircle). Build-time
detail: whether a supplied mark gets the ERGO-28 tile (probably not for a full-bleed logo).

**Sub-question 1.1 (extract icon from an exe / AUMID) — DEFERRED.** The "magic" of a host card
auto-wearing the host's icon is illusory: the integration must already tell us *which* process
owns the card (name the exe/AUMID), so at that point it can just supply the icon directly via
`--icon-file` — extraction buys little over BYO-image for the most interop cost (Shell/Win32,
multi-resolution selection, AOT-safe). Revisit when a concrete need appears (e.g. the platform
fixing the theming story makes bare extracted icons viable again). Operator reasoning,
2026-07-13.

## Question
How should a caller supply their **own, fully-arbitrary icon** for a task card — a brand
logo, a generated image, an existing app's icon — as a first-class input, rather than being
limited to the ERGO-20 ("Icon representation — specifying an icon without image files") set of
emoji + curated Segoe glyph names? A raw-file-path escape hatch already exists
(`IconTokenKind.RawPath` in `src/Atv/Icons/IconTokens.cs`, "advanced, bypasses rendering"),
but it is undocumented-as-supported and does no normalization; the need that surfaced is
proper support for "here is my image / my brand, use it."

### Sub-question 1.1: extraction from an exe / app (AUMID)
Should we let the caller point at an **executable** (or an installed app / AUMID) and have
`atv` **extract that program's icon** to use as the card icon — e.g. `--icon-from-exe
<path>` or `--icon-from-app <aumid>` — so a wrapper (`atv run …`) or a host integration can
automatically wear the wrapped/host program's own icon without the caller producing a PNG?

## Why this surfaced
Operator, 2026-07-10, during the phase-13 integration work: it became clear callers will
want their own identity on the card (a brand, or something they generate per project/session),
not just one of the built-in glyphs. The `run` wrapper (ERGO-5) and per-host integrations make
"wear the target program's icon" a natural, concrete want (1.1).

## What makes it non-trivial (constraints)
- **The platform takes a plain file path to a static bitmap** (phase-07 finding): `IconUri`
  is a filesystem path the Shell scales; there's no format negotiation. Arbitrary caller
  input (PNG/JPG/SVG/ICO, any size, any transparency) must be normalized to the one 64px PNG
  the pipeline emits — resize/pad/flatten — which is real image work beyond today's glyph
  rasterizer (`Atv.IconRendering`).
- **Grouping is keyed on the exact icon URI string** (ERGO-13): a caller-supplied image's
  cached path becomes the grouping key, interacting with the separate-by-session default
  (ERGO-15, "Default grouping when the consumer specifies nothing") and the per-handle icon
  copy model (ERGO-22, "Icon glyph → PNG rendering"). Two callers supplying the "same" logo
  by different paths would not glom; the same logo normalized to identical bytes might.
- **Icons are immutable per task** (ERGO-25, "`start` on an already-live handle"): set only at
  Create. A brand that changes means Remove+Create (loses step history).
- **Theme** (ERGO-28, "Theme-awareness of the icon provided to the platform"): a supplied
  brand bitmap won't auto-adapt to light/dark either — same open problem, now caller-owned.
- **1.1 specifics:** icon extraction from an exe uses Shell/Win32 (`SHGetFileInfo` /
  `ExtractIconEx` / `IShellItemImageFactory`) and from an AUMID uses the package logo via
  `PackageManager`/`AppListEntry`; all must be AOT-safe (CsWin32, like the existing interop)
  and handle multi-resolution/embedded-icon selection, missing icons, and untrusted paths.
- **Security/trust:** reading an arbitrary file or exe the caller names is a mild trust
  surface (path traversal, huge files, malformed images) — needs bounds + validation.

## Options to explore later (NOT deciding now)
1. Promote the existing `RawPath` escape hatch to a supported "bring-your-own-image" input:
   accept common formats, normalize (resize/pad to 64px, flatten transparency), cache
   per-handle like rendered glyphs, document it. Likely the cheapest first step.
2. A distinct flag surface (`--icon-file <path>`) separate from the `--icon <token>` name/emoji
   space, so "a name/emoji" vs "a file" is unambiguous at parse time.
3. (1.1) `--icon-from-exe <path>` / `--icon-from-app <aumid>`: extract + normalize the target
   program's icon. Natural default for `atv run` (wear the wrapped tool's icon).
4. Keep `RawPath` as the only, explicitly-"advanced/unsupported" hatch and document the
   normalization caveats — punt real BYO-image support.

## Scope note
Filed OPEN (operator, 2026-07-10); does not change the current build. Related: ERGO-20/22/28
(icon representation/render/theme), ERGO-13/15 (grouping/glomming), ERGO-5 (`run` wrapper).
Overlaps the repo-defaults question (ERGO-30) — a repo could *supply* the brand icon there.
