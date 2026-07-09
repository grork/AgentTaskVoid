using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;

namespace Atv.IconRendering;

/// <summary>
/// ERGO-22's last-resort fallback: a primitive drawn shape that never depends
/// on a font being present, for when even the default glyph
/// (<c>Atv.Icons.IconTokens.Default</c>) can't be rendered. A filled circle,
/// solid black on transparent -- same <see cref="SoftwareCanvas"/> pipeline
/// as <see cref="GlyphRenderer"/>, just <c>FillEllipse</c> instead of
/// <c>DrawText</c>, so it shares the zero-GPU/deterministic guarantee and can
/// never itself hit a "glyph not found" outcome (there is no font lookup at
/// all).
/// </summary>
public static unsafe class ShapeRenderer
{
    /// <summary>Fraction of the canvas the circle's radius targets -- leaves the same margin <see cref="GlyphRenderer"/> uses for glyphs, so the fallback shape reads at a consistent visual weight.</summary>
    private const float RadiusFraction = 0.38f;

    /// <summary>Renders the primitive fallback shape. Always succeeds -- there is no "not present" outcome for a drawn shape.</summary>
    public static RenderResult RenderDefaultShape(int sizePx)
    {
        if (sizePx <= 0) throw new ArgumentOutOfRangeException(nameof(sizePx));

        byte[] rgba = SoftwareCanvas.Render(sizePx, (renderTarget, brush) =>
        {
            float center = sizePx / 2f;
            var ellipse = new D2D1_ELLIPSE
            {
                point = new D2D_POINT_2F { x = center, y = center },
                radiusX = sizePx * RadiusFraction,
                radiusY = sizePx * RadiusFraction,
            };
            renderTarget->FillEllipse(in ellipse, (ID2D1Brush*)brush);
        });

        return RenderResult.Ok(PngEncoder.Encode(rgba, sizePx, sizePx), sizePx);
    }
}
