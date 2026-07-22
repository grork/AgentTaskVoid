using System.Security.Cryptography;
using Codevoid.AgentTaskVoid.IconRendering;

namespace Codevoid.AgentTaskVoid.IconRendering.Tests;

/// <summary>
/// Phase-25 acceptance criteria 1 and 2. AC1: Segoe Fluent Icons glyphs on the
/// accent tile must be centered by their INK bounding box, not the DirectWrite
/// paragraph line box (ascent+descent) -- the pre-phase-25
/// <c>SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER)</c>-only
/// behavior rides high (and, for some glyphs, drifts horizontally too --
/// <c>DWRITE_TEXT_ALIGNMENT_CENTER</c> centers by measured/advance width,
/// which is the same class of "layout box, not ink box" bug on the other
/// axis) because glyphs reserve layout space their ink never fills. AC2: the
/// bare color-emoji path (<see cref="GlyphRenderer.RenderEmoji"/>,
/// <c>onTile: false</c>) is untouched by the fix -- pinned byte-for-byte via
/// a SHA-256 hash captured from the pre-phase-25 implementation.
/// </summary>
[TestClass]
public sealed class GlyphInkCenteringTests
{
    private const int SizePx = 64;

    // Pixel-predicate rationale (AC1): the tile is filled with TileCompositor's
    // fixed accent blue (R=0x00) and the glyph is drawn in TileCompositor's
    // fixed white (R=0xFF) -- the RED channel alone gives the maximum possible
    // contrast (0 vs 255) between "background" and "ink", including at
    // antialiased edge pixels, which blend somewhere in between. A pixel
    // counts as glyph ink iff its red channel is at least half-way to white;
    // this is robust to the exact AA coverage value and does not depend on
    // guessing an alpha threshold (the tile is opaque everywhere the glyph
    // could plausibly be, so alpha is not a useful discriminator here).
    private const byte InkRedThreshold = 128;

    // Codepoints verified against IconTokens.CuratedSegoe (src/Atv/Icons/IconTokens.cs).
    // Swept the full curated set's pre-fix ink-bbox-center offset (measured
    // with this file's own FindInkBoundingBox predicate) to pick a set that
    // both matches the phase file's "Error, Robot, tall, wide" minimum AND
    // actually demonstrates the defect -- most curated glyphs' pre-fix
    // bbox-center offset happens to fall within ~1.5px, so a representative
    // set built only from "looks tall"/"looks wide" glyphs risked never going
    // red. StatusWarning's pre-fix offset (dx=11.0, dy=12.5 out of a 64px
    // tile) is the sweep's clearest, most unambiguous violation.
    private const int ErrorCodepoint = 0xE783;         // the phase-22 AC12 dogfood glyph -- "!" in a circle.
    private const int RobotCodepoint = 0xE99A;         // ERGO-12's default glyph.
    private const int StatusWarningCodepoint = 0xEA84; // visually tall: a narrow vertical badge glyph (pre-fix ink bbox measures 3px wide x 18px tall) -- the sweep's worst pre-fix offender (~11px x, ~12.5px y off-center at 64px).
    private const int LinkCodepoint = 0xE71B;           // visually wide: a chain-link glyph (pre-fix ink bbox measures 49px wide x 28px tall).

    [TestMethod]
    [DataRow(ErrorCodepoint, DisplayName = "Error (E783, the dogfood glyph)")]
    [DataRow(RobotCodepoint, DisplayName = "Robot (E99A, the default)")]
    [DataRow(StatusWarningCodepoint, DisplayName = "StatusWarning (EA84, visually tall)")]
    [DataRow(LinkCodepoint, DisplayName = "Link (E71B, visually wide)")]
    public void RenderSegoeGlyph_InkBoundingBoxCenter_IsWithinToleranceOfTileCenter_OnBothAxes(int codepoint)
    {
        RenderResult result = GlyphRenderer.RenderSegoeGlyph(codepoint, SizePx);
        Assert.AreEqual(RenderStatus.Ok, result.Status);

        var (width, height, rgba) = PngTestReader.Decode(result.PngBytes!);
        Assert.AreEqual(SizePx, width);
        Assert.AreEqual(SizePx, height);

        (int minX, int minY, int maxX, int maxY) = FindInkBoundingBox(rgba, width, height);
        Assert.IsGreaterThanOrEqualTo(0, maxX, "expected at least one glyph-ink pixel on the tile.");

        float inkCenterX = (minX + maxX + 1) / 2f;
        float inkCenterY = (minY + maxY + 1) / 2f;
        float tileCenter = SizePx / 2f;

        const double ToleranceLimit = 2.0;
        Assert.AreEqual(tileCenter, inkCenterX, ToleranceLimit,
            $"glyph ink horizontal center ({inkCenterX}) is not within {ToleranceLimit}px of the tile center ({tileCenter}); ink bbox x:[{minX},{maxX}].");
        Assert.AreEqual(tileCenter, inkCenterY, ToleranceLimit,
            $"glyph ink vertical center ({inkCenterY}) is not within {ToleranceLimit}px of the tile center ({tileCenter}); ink bbox y:[{minY},{maxY}] -- this is the phase-25 defect (DWRITE paragraph/text-alignment centers the layout box, not the ink).");
    }

    [TestMethod]
    public void RenderEmoji_BareColorPath_IsByteForByteUnchangedByThePhase25Fix()
    {
        // Reference hash captured from the pre-phase-25 implementation (SHA-256
        // of RenderEmoji("\U0001F916", 64).PngBytes, GlyphRenderer.cs as of
        // commit 9b91f8b, before this phase's fix touched the file). The
        // phase-25 fix touches only the onTile Segoe path (GlyphRenderer's
        // internal onTile branch); RenderEmoji's bare, untiled path must
        // produce byte-identical output.
        const string ExpectedSha256Hex = "CDA54E9497196CD4AE9840E192801FE290CEF64F6F6F6862E8AA781A073FD251";

        RenderResult result = GlyphRenderer.RenderEmoji("\U0001F916", SizePx); // robot face emoji
        Assert.AreEqual(RenderStatus.Ok, result.Status);

        string actualSha256Hex = Convert.ToHexString(SHA256.HashData(result.PngBytes!));
        Assert.AreEqual(ExpectedSha256Hex, actualSha256Hex, "bare emoji render bytes changed -- the phase-25 ink-centering fix must not touch the onTile:false path.");
    }

    /// <summary>Scans decoded straight-RGBA pixels for the glyph-ink bounding box using <see cref="InkRedThreshold"/>. Returns <c>MaxX == -1</c> if no ink pixel was found.</summary>
    private static (int MinX, int MinY, int MaxX, int MaxY) FindInkBoundingBox(byte[] rgba, int width, int height)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var px = PngTestReader.PixelAt(rgba, width, x, y);
            if (px.R < InkRedThreshold) continue;

            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        return (minX, minY, maxX, maxY);
    }
}
