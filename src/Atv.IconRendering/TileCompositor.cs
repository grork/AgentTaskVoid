using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;

namespace Atv.IconRendering;

/// <summary>
/// ERGO-28's theme-neutral tile treatment: a filled rounded-rect (squircle-ish)
/// backdrop in a fixed accent color, drawn BEHIND a monochrome glyph so the
/// result reads on any taskbar theme (light or dark) without ever inspecting
/// the theme -- one static asset, no runtime reaction (rejected: watchdog
/// re-render/detect-at-render both fight icon immutability, ERGO-25, and the
/// URI grouping key, ERGO-13, for marginal gain).
///
/// Build-time details (ERGO-28 explicitly defers these to implementation,
/// "not re-decisions"):
/// <list type="bullet">
/// <item><b>Accent color = <see cref="AccentColor"/></b>, Windows/Fluent's own
/// default "Communication Blue" (#0078D4) -- a recognizable, already-in-the-OS
/// accent that reads well as a filled square regardless of the surrounding
/// taskbar theme (the tile IS the background now, not the taskbar), and gives
/// strong contrast against the white glyph.</item>
/// <item><b>Corner radius = <see cref="CornerRadiusFraction"/></b> of the tile
/// size (20%) -- matches the visual weight of Windows 11's own squircle app
/// icons at a small size without needing a true superellipse (a
/// <c>ID2D1RoundedRectangleGeometry</c>-style rounded rect is the pragmatic
/// software-target equivalent).</item>
/// <item><b>Glyph color = <see cref="GlyphColor"/></b>, solid white -- the
/// strongest available contrast against the fixed accent tile.</item>
/// </list>
///
/// Applies ONLY to monochrome Segoe Fluent Icons glyphs (routed here by
/// <see cref="GlyphRenderer.RenderSegoeGlyph"/>). Color emoji stay BARE
/// (<see cref="GlyphRenderer.RenderEmoji"/> does not call this type) -- they
/// are already theme-safe full-color art, and compositing them onto another
/// fixed color would visually clash rather than help (the ERGO-28
/// "emoji tile-or-bare" build choice: BARE). Caller-supplied raster images
/// (<see cref="RasterNormalizer"/>) also stay bare/full-bleed for the same
/// reason, PLUS we cannot recolor a caller's own brand mark to guarantee
/// contrast against our tile (ERGO-29's "probably bare/full-bleed" steer).
/// </summary>
internal static unsafe class TileCompositor
{
    /// <summary>Windows/Fluent's default accent blue, #0078D4 ("Communication Blue") -- fixed, never theme-detected.</summary>
    public static readonly D2D1_COLOR_F AccentColor = new() { r = 0x00 / 255f, g = 0x78 / 255f, b = 0xD4 / 255f, a = 1f };

    /// <summary>Solid white -- the glyph color drawn on top of <see cref="AccentColor"/>.</summary>
    public static readonly D2D1_COLOR_F GlyphColor = new() { r = 1f, g = 1f, b = 1f, a = 1f };

    /// <summary>Corner radius as a fraction of the tile's edge length.</summary>
    public const float CornerRadiusFraction = 0.2f;

    /// <summary>Fills the full <paramref name="sizePx"/> x <paramref name="sizePx"/> canvas with a rounded-rect in <see cref="AccentColor"/>. Caller draws the glyph on top afterward, in the same <see cref="SoftwareCanvas.Render"/> callback.</summary>
    public static void FillTile(ID2D1RenderTarget* renderTarget, int sizePx)
    {
        ID2D1SolidColorBrush* accentBrush = null;
        try
        {
            renderTarget->CreateSolidColorBrush(AccentColor, null, &accentBrush);

            float radius = sizePx * CornerRadiusFraction;
            var roundedRect = new D2D1_ROUNDED_RECT
            {
                rect = new D2D_RECT_F { left = 0, top = 0, right = sizePx, bottom = sizePx },
                radiusX = radius,
                radiusY = radius,
            };
            renderTarget->FillRoundedRectangle(in roundedRect, (ID2D1Brush*)accentBrush);
        }
        finally
        {
            if (accentBrush != null) accentBrush->Release();
        }
    }
}
