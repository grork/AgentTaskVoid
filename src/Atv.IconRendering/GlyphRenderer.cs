using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;

namespace Atv.IconRendering;

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
/// Pure mechanism only (ERGO-22): no filesystem, no caching, no fallback
/// policy -- those are the main project's job (<c>Atv.Icons.IconService</c>).
/// </summary>
public static unsafe class GlyphRenderer
{
    /// <summary>The one color-emoji font every supported Windows 11 build ships.</summary>
    public const string EmojiFontFamily = "Segoe UI Emoji";

    /// <summary>The Windows 11 icon font (superset of the older Segoe MDL2 Assets codepoint ranges the curated list in <c>Atv.Icons.IconTokens</c> draws from).</summary>
    public const string SegoeIconFontFamily = "Segoe Fluent Icons";

    /// <summary>Fraction of the canvas the font size targets -- leaves a small margin so glyphs with generous side-bearings (common in both emoji and icon fonts) aren't edge-clipped at taskbar size.</summary>
    private const float FontSizeFraction = 0.78f;

    /// <summary>Renders a single literal emoji character (or a single non-BMP emoji encoded as a surrogate pair) via <see cref="EmojiFontFamily"/>.</summary>
    public static RenderResult RenderEmoji(string emoji, int sizePx)
    {
        ArgumentException.ThrowIfNullOrEmpty(emoji);
        int codepoint = char.ConvertToUtf32(emoji, 0);
        return Render(emoji, EmojiFontFamily, sizePx, codepoint);
    }

    /// <summary>Renders a single Segoe Fluent Icons codepoint (from the main project's curated list) via <see cref="SegoeIconFontFamily"/>.</summary>
    public static RenderResult RenderSegoeGlyph(int codepoint, int sizePx)
        => Render(char.ConvertFromUtf32(codepoint), SegoeIconFontFamily, sizePx, codepoint);

    private static RenderResult Render(string text, string fontFamily, int sizePx, int probeCodepoint)
    {
        if (sizePx <= 0) throw new ArgumentOutOfRangeException(nameof(sizePx));

        if (!GlyphProbe.IsPresent(fontFamily, probeCodepoint))
            return RenderResult.NotFound(sizePx);

        byte[] rgba = SoftwareCanvas.Render(sizePx, (renderTarget, brush) =>
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
                    (ID2D1Brush*)brush,
                    D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT | D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NO_SNAP,
                    DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL);
            }
            finally
            {
                if (format != null) format->Release();
            }
        });

        return RenderResult.Ok(PngEncoder.Encode(rgba, sizePx, sizePx), sizePx);
    }
}
