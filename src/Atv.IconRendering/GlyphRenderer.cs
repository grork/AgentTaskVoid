using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;

namespace Codevoid.AgentTaskVoid.IconRendering;

/// <summary>
/// ERGO-22's glyph -&gt; PNG mechanism: ONE rendering path (a single
/// <see cref="SoftwareCanvas"/> + <c>ID2D1RenderTarget.DrawText</c> call with
/// <c>D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT</c> set) draws BOTH color
/// emoji and monochrome Segoe glyphs -- that flag is what lets a plain
/// <c>ID2D1RenderTarget</c> (no <c>ID2D1DeviceContext</c>, no D3D device)
/// render color-font glyph layers at all; it is a documented no-op for
/// ordinary monochrome fonts, so the exact same call handles both inputs.
/// <see cref="GlyphProbe"/> is consulted FIRST so a genuinely absent glyph is
/// a clean <see cref="RenderStatus.GlyphNotFound"/> return, never an
/// exception or a silently-blank/".notdef" render.
///
/// ERGO-28 (phase 16): monochrome Segoe Fluent Icons glyphs
/// (<see cref="RenderSegoeGlyph"/>) are composited onto <see cref="TileCompositor"/>'s
/// fixed accent tile, in WHITE -- fixing the out-of-box "solid black glyph on
/// a dark taskbar" contrast problem for the ERGO-12 default (Robot) and every
/// other curated glyph. Color emoji (<see cref="RenderEmoji"/>) stay BARE, on
/// a transparent canvas exactly as before -- already theme-safe full-color
/// art; see <see cref="TileCompositor"/>'s remarks for the full "tile vs bare"
/// reasoning.
///
/// Phase 25 (glyph ink-box centering): <c>DWRITE_PARAGRAPH_ALIGNMENT_CENTER</c>/
/// <c>DWRITE_TEXT_ALIGNMENT_CENTER</c> center the drawn LAYOUT box (font
/// ascent+descent vertically, measured/advance width horizontally) within the
/// draw rect -- not the glyph's actual INK. Segoe Fluent Icons glyphs reserve
/// layout space their ink doesn't fill (most visibly vertically, but some
/// glyphs drift horizontally too), so the on-tile glyph rides off the tile's
/// visual center. <see cref="RenderSegoeGlyph"/> corrects this with an
/// alpha-scan recenter: draw once to a throwaway transparent canvas at the
/// untranslated rect, scan the result for the ink's pixel bounding box (any
/// non-transparent pixel), then draw a SECOND time -- onto the real,
/// tile-filled canvas -- with the draw rect translated so that bounding box's
/// center lands on the tile's center. This costs one extra (cheap, zero-GPU)
/// raster per Segoe render but needs no new interop surface (no
/// <c>IDWriteTextLayout</c>/<c>DWRITE_OVERHANG_METRICS</c>), and the
/// translation is a pure rect-origin shift, so it reuses the exact same
/// <c>CreateTextFormat</c>/<c>DrawText</c> call for both passes. The bare
/// emoji path (<see cref="RenderEmoji"/>, <c>onTile: false</c>) is untouched --
/// its code path below is unchanged from pre-phase-25.
///
/// Pure mechanism only (ERGO-22): no filesystem, no caching, no fallback
/// policy -- those are the main project's job (<c>Codevoid.AgentTaskVoid.Icons.IconService</c>).
/// </summary>
public static unsafe class GlyphRenderer
{
    /// <summary>The one color-emoji font every supported Windows 11 build ships.</summary>
    public const string EmojiFontFamily = "Segoe UI Emoji";

    /// <summary>The Windows 11 icon font (superset of the older Segoe MDL2 Assets codepoint ranges the curated list in <c>Codevoid.AgentTaskVoid.Icons.IconTokens</c> draws from).</summary>
    public const string SegoeIconFontFamily = "Segoe Fluent Icons";

    /// <summary>Fraction of the canvas the font size targets -- leaves a small margin so glyphs with generous side-bearings (common in both emoji and icon fonts) aren't edge-clipped at taskbar size.</summary>
    private const float FontSizeFraction = 0.78f;

    /// <summary>Renders a single literal emoji character (or a single non-BMP emoji encoded as a surrogate pair) via <see cref="EmojiFontFamily"/>, BARE (transparent canvas, no tile -- ERGO-28's emoji build choice).</summary>
    public static RenderResult RenderEmoji(string emoji, int sizePx)
    {
        int codepoint = char.ConvertToUtf32(emoji, 0);
        return Render(emoji, EmojiFontFamily, sizePx, codepoint, onTile: false);
    }

    /// <summary>Renders a single Segoe Fluent Icons codepoint (from the main project's curated list) via <see cref="SegoeIconFontFamily"/>, composited onto <see cref="TileCompositor"/>'s accent tile in white (ERGO-28).</summary>
    public static RenderResult RenderSegoeGlyph(int codepoint, int sizePx)
        => Render(char.ConvertFromUtf32(codepoint), SegoeIconFontFamily, sizePx, codepoint, onTile: true);

    private static RenderResult Render(string text, string fontFamily, int sizePx, int probeCodepoint, bool onTile)
    {
        if (sizePx <= 0) throw new ArgumentOutOfRangeException(nameof(sizePx));

        if (!GlyphProbe.IsPresent(fontFamily, probeCodepoint))
            return RenderResult.NotFound(sizePx);

        if (onTile)
            return RenderOnTile(text, fontFamily, sizePx);

        // Bare path (emoji, onTile: false) -- unchanged from pre-phase-25:
        // draw once, straight onto a transparent canvas, no tile, no
        // ink-centering pass. Phase 25's fix is scoped to the onTile branch
        // only (see RenderOnTile below); this path's output must stay
        // byte-for-byte identical to before that phase.
        byte[] bareRgba = SoftwareCanvas.Render(sizePx, (renderTarget, defaultBrush) =>
        {
            IDWriteTextFormat* format = null;
            try
            {
                Interop.DWriteFactory->CreateTextFormat(
                    fontFamily,
                    null,
                    DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
                    DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
                    DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                    sizePx * FontSizeFraction,
                    "en-US",
                    &format);

                format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);
                format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);

                var layoutRect = new D2D_RECT_F { left = 0, top = 0, right = sizePx, bottom = sizePx };
                renderTarget->DrawText(
                    text,
                    (uint)text.Length,
                    format,
                    in layoutRect,
                    (ID2D1Brush*)defaultBrush,
                    D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT | D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NO_SNAP,
                    DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL);
            }
            finally
            {
                if (format != null) format->Release();
            }
        });

        return RenderResult.Ok(PngEncoder.Encode(bareRgba, sizePx, sizePx), sizePx);
    }

    /// <summary>
    /// Phase 25's on-tile (Segoe) path: measure the glyph's ink bounding box
    /// via a throwaway transparent-canvas raster (<see cref="MeasureInkCenteringOffset"/>),
    /// then draw for real onto the tile-filled canvas with the draw rect
    /// translated so that ink box lands centered on the tile.
    /// </summary>
    private static RenderResult RenderOnTile(string text, string fontFamily, int sizePx)
    {
        (float offsetX, float offsetY) = MeasureInkCenteringOffset(text, fontFamily, sizePx);

        byte[] rgba = SoftwareCanvas.Render(sizePx, (renderTarget, defaultBrush) =>
        {
            TileCompositor.FillTile(renderTarget, sizePx);

            ID2D1SolidColorBrush* tileGlyphBrush = null;
            try
            {
                renderTarget->CreateSolidColorBrush(TileCompositor.GlyphColor, null, &tileGlyphBrush);
                DrawGlyphIntoRect(renderTarget, (ID2D1Brush*)tileGlyphBrush, text, fontFamily, sizePx, offsetX, offsetY);
            }
            finally
            {
                if (tileGlyphBrush != null) tileGlyphBrush->Release();
            }
        });

        return RenderResult.Ok(PngEncoder.Encode(rgba, sizePx, sizePx), sizePx);
    }

    /// <summary>
    /// Draws <paramref name="text"/> to a throwaway transparent <paramref name="sizePx"/>
    /// x <paramref name="sizePx"/> canvas at the untranslated rect (matching
    /// <see cref="RenderOnTile"/>'s eventual real draw exactly, offset (0,0)),
    /// scans the result for the ink's pixel bounding box (any non-transparent
    /// pixel -- alpha is unaffected by brush color, so the measurement brush's
    /// exact color doesn't matter), and returns the (x, y) translation that
    /// would move that bounding box's center onto the tile's center. Returns
    /// (0, 0) -- i.e. falls back to the untranslated line-box center -- if no
    /// ink pixel is found (should not happen given the caller already
    /// confirmed <see cref="GlyphProbe.IsPresent"/>, but a real-but-blank
    /// glyph must not throw or divide by zero).
    /// </summary>
    private static (float OffsetX, float OffsetY) MeasureInkCenteringOffset(string text, string fontFamily, int sizePx)
    {
        byte[] rgba = SoftwareCanvas.Render(sizePx, (renderTarget, defaultBrush) =>
            DrawGlyphIntoRect(renderTarget, (ID2D1Brush*)defaultBrush, text, fontFamily, sizePx, offsetX: 0f, offsetY: 0f));

        int minX = sizePx, minY = sizePx, maxX = -1, maxY = -1;
        for (int y = 0; y < sizePx; y++)
        {
            int rowBase = y * sizePx * 4;
            for (int x = 0; x < sizePx; x++)
            {
                if (rgba[rowBase + x * 4 + 3] == 0) continue; // alpha == 0: not ink.

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0) return (0f, 0f); // no ink found -- fall back to the untranslated line-box center.

        float inkCenterX = (minX + maxX + 1) / 2f;
        float inkCenterY = (minY + maxY + 1) / 2f;
        float tileCenter = sizePx / 2f;
        return (tileCenter - inkCenterX, tileCenter - inkCenterY);
    }

    /// <summary>Shared draw call for both <see cref="MeasureInkCenteringOffset"/>'s measurement pass and <see cref="RenderOnTile"/>'s real pass: the same format/alignment/DrawText call as the pre-phase-25 implementation, just with the draw rect translated by (<paramref name="offsetX"/>, <paramref name="offsetY"/>) instead of fixed at the canvas origin.</summary>
    private static void DrawGlyphIntoRect(ID2D1RenderTarget* renderTarget, ID2D1Brush* brush, string text, string fontFamily, int sizePx, float offsetX, float offsetY)
    {
        IDWriteTextFormat* format = null;
        try
        {
            Interop.DWriteFactory->CreateTextFormat(
                fontFamily,
                null,
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
                DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
                DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                sizePx * FontSizeFraction,
                "en-US",
                &format);

            format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);
            format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);

            var layoutRect = new D2D_RECT_F { left = offsetX, top = offsetY, right = sizePx + offsetX, bottom = sizePx + offsetY };
            renderTarget->DrawText(
                text,
                (uint)text.Length,
                format,
                in layoutRect,
                brush,
                D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT | D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NO_SNAP,
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL);
        }
        finally
        {
            if (format != null) format->Release();
        }
    }
}
