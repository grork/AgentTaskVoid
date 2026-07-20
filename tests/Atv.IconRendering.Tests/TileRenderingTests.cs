using Codevoid.AgentTaskVoid.IconRendering;

namespace Codevoid.AgentTaskVoid.IconRendering.Tests;

/// <summary>
/// Phase-16 acceptance criterion 1: monochrome Segoe Fluent Icons glyphs
/// composite onto <see cref="TileCompositor"/>'s fixed accent tile (ERGO-28)
/// -- the default Robot token becomes a white-on-accent-tile asset, fixing
/// the out-of-box "solid black glyph on a dark taskbar" contrast problem.
/// Color emoji stay BARE (the recorded "emoji tile-or-bare" build choice:
/// bare -- already theme-safe full-color art, see <see cref="TileCompositor"/>'s
/// remarks). Inspects ACTUAL decoded pixel data (via <see cref="PngTestReader"/>),
/// not just "no exception" -- per the phase file's AC7 guidance that
/// compositing correctness must be verified at the pixel level.
/// </summary>
[TestClass]
public sealed class TileRenderingTests
{
    // U+E99A "Robot" -- the ERGO-12 default glyph.
    private const int RobotCodepoint = 0xE99A;
    private const int SizePx = 64;

    private static readonly (byte R, byte G, byte B) AccentRgb = (0x00, 0x78, 0xD4);

    [TestMethod]
    public void RenderSegoeGlyph_ExactCornerPixel_IsTransparent_ProvingARoundedRect_NotASquare()
    {
        RenderResult result = GlyphRenderer.RenderSegoeGlyph(RobotCodepoint, SizePx);
        var (width, _, rgba) = PngTestReader.Decode(result.PngBytes!);

        var corner = PngTestReader.PixelAt(rgba, width, 0, 0);
        Assert.AreEqual(0, corner.A, "the literal (0,0) corner pixel must fall outside the rounded-rect's corner arc -- a plain square fill would make it opaque.");
    }

    [TestMethod]
    public void RenderSegoeGlyph_EdgeMidpoint_IsTheFixedAccentColor_FullyOpaque()
    {
        RenderResult result = GlyphRenderer.RenderSegoeGlyph(RobotCodepoint, SizePx);
        var (width, _, rgba) = PngTestReader.Decode(result.PngBytes!);

        // (width/2, 2): on the straight part of the top edge (well clear of
        // both corner arcs), 2px in from the boundary to dodge antialiasing.
        var edge = PngTestReader.PixelAt(rgba, width, width / 2, 2);

        Assert.AreEqual(AccentRgb.R, edge.R);
        Assert.AreEqual(AccentRgb.G, edge.G);
        Assert.AreEqual(AccentRgb.B, edge.B);
        Assert.AreEqual(255, edge.A);
    }

    [TestMethod]
    public void RenderSegoeGlyph_ContainsSolidWhiteGlyphPixels()
    {
        RenderResult result = GlyphRenderer.RenderSegoeGlyph(RobotCodepoint, SizePx);
        var (width, height, rgba) = PngTestReader.Decode(result.PngBytes!);

        bool foundWhite = false;
        for (int y = 0; y < height && !foundWhite; y++)
        for (int x = 0; x < width && !foundWhite; x++)
        {
            var px = PngTestReader.PixelAt(rgba, width, x, y);
            if (px is { R: 255, G: 255, B: 255, A: 255 })
                foundWhite = true;
        }

        Assert.IsTrue(foundWhite, "expected at least one fully-covered white glyph pixel somewhere on the tile.");
    }

    [TestMethod]
    public void RenderSegoeGlyph_NeverContainsSolidBlack()
    {
        // The pre-phase-16 behavior (solid black on transparent) is exactly
        // the contrast problem ERGO-28 fixes -- guard against a regression
        // back to it.
        RenderResult result = GlyphRenderer.RenderSegoeGlyph(RobotCodepoint, SizePx);
        var (width, height, rgba) = PngTestReader.Decode(result.PngBytes!);

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var px = PngTestReader.PixelAt(rgba, width, x, y);
            Assert.IsFalse(px is { R: 0, G: 0, B: 0, A: 255 }, $"found a solid-black pixel at ({x},{y}) -- the pre-ERGO-28 contrast problem regressed.");
        }
    }

    [TestMethod]
    public void RenderEmoji_StaysBare_NoAccentTilePixelsAnywhere()
    {
        RenderResult result = GlyphRenderer.RenderEmoji("\U0001F916", SizePx); // robot face emoji
        var (width, height, rgba) = PngTestReader.Decode(result.PngBytes!);

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var px = PngTestReader.PixelAt(rgba, width, x, y);
            bool isAccent = px.R == AccentRgb.R && px.G == AccentRgb.G && px.B == AccentRgb.B && px.A == 255;
            Assert.IsFalse(isAccent, $"found an accent-tile-colored pixel at ({x},{y}) -- emoji must render bare (the recorded ERGO-28 build choice), never tiled.");
        }
    }

    [TestMethod]
    public void RenderEmoji_CornerPixel_StaysTransparent_NoBackgroundAtAll()
    {
        RenderResult result = GlyphRenderer.RenderEmoji("\U0001F916", SizePx);
        var (width, _, rgba) = PngTestReader.Decode(result.PngBytes!);

        var corner = PngTestReader.PixelAt(rgba, width, 0, 0);
        Assert.AreEqual(0, corner.A, "bare emoji renders keep a fully transparent canvas outside the glyph itself.");
    }
}
