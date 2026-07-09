using System.Buffers.Binary;
using Atv.IconRendering;

namespace Atv.IconRendering.Tests;

/// <summary>
/// Phase-07 acceptance criterion 1: emoji -&gt; valid PNG NxN; Segoe codepoint
/// -&gt; PNG; missing glyph -&gt; NotFound signal (no throw); drawn shape -&gt; PNG.
/// Deterministic (software target), green with no GPU assumptions -- every
/// test here runs the REAL D2D/DWrite/WIC software pipeline (no fakes: there
/// is nothing to fake against in a quarantined, policy-free rendering
/// project), so a failure here is a genuine interop/runtime problem, not a
/// mock gap.
/// </summary>
[TestClass]
public sealed class GlyphRenderingTests
{
    // U+E99A "Robot" -- verified against the official Segoe Fluent Icons font
    // icon list (learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font).
    private const int RobotCodepoint = 0xE99A;

    [TestMethod]
    public void RenderEmoji_KnownEmoji_ReturnsValidPngAtRequestedSize()
    {
        RenderResult result = GlyphRenderer.RenderEmoji("\U0001F916", 48); // robot face emoji

        Assert.AreEqual(RenderStatus.Ok, result.Status);
        Assert.IsNotNull(result.PngBytes);
        AssertValidPng(result.PngBytes, 48, 48);
    }

    [TestMethod]
    public void RenderSegoeGlyph_KnownCodepoint_ReturnsValidPng()
    {
        RenderResult result = GlyphRenderer.RenderSegoeGlyph(RobotCodepoint, 64);

        Assert.AreEqual(RenderStatus.Ok, result.Status);
        Assert.IsNotNull(result.PngBytes);
        AssertValidPng(result.PngBytes, 64, 64);
    }

    [TestMethod]
    public void RenderSegoeGlyph_CodepointNotInIconFont_ReturnsNotFound_NoThrow()
    {
        // 'A' (U+0041): Segoe Fluent Icons is a symbol font restricted to its
        // documented PUA-ish ranges (E7xx/EAxx/F0xx/...); ordinary Latin
        // letters are not mapped, so GetGlyphIndices returns glyph 0.
        RenderResult result = GlyphRenderer.RenderSegoeGlyph('A', 48);

        Assert.AreEqual(RenderStatus.GlyphNotFound, result.Status);
        Assert.IsNull(result.PngBytes);
    }

    [TestMethod]
    public void GlyphProbe_NonexistentFontFamily_ReturnsFalse_NoThrow()
    {
        bool present = GlyphProbe.IsPresent("Definitely Not A Real Font XYZ123", 'A');

        Assert.IsFalse(present);
    }

    [TestMethod]
    public void GlyphProbe_KnownGlyphInKnownFont_ReturnsTrue()
    {
        Assert.IsTrue(GlyphProbe.IsPresent(GlyphRenderer.SegoeIconFontFamily, RobotCodepoint));
    }

    [TestMethod]
    public void RenderDefaultShape_ReturnsValidPng()
    {
        RenderResult result = ShapeRenderer.RenderDefaultShape(48);

        Assert.AreEqual(RenderStatus.Ok, result.Status);
        Assert.IsNotNull(result.PngBytes);
        AssertValidPng(result.PngBytes, 48, 48);
    }

    [TestMethod]
    public void RenderDefaultShape_NeverReturnsNotFound_EvenForDegenerateSize()
    {
        // No font lookup at all -- there is no "glyph absent" outcome for a drawn shape.
        RenderResult result = ShapeRenderer.RenderDefaultShape(1);

        Assert.AreEqual(RenderStatus.Ok, result.Status);
    }

    [TestMethod]
    public void Render_SameGlyphSameSize_IsByteForByteDeterministic()
    {
        RenderResult first = GlyphRenderer.RenderSegoeGlyph(RobotCodepoint, 48);
        RenderResult second = GlyphRenderer.RenderSegoeGlyph(RobotCodepoint, 48);

        CollectionAssert.AreEqual(first.PngBytes, second.PngBytes);
    }

    [TestMethod]
    [DataRow(32)]
    [DataRow(48)]
    [DataRow(64)]
    public void RenderSegoeGlyph_VariousSizes_DimensionsMatchRequest(int sizePx)
    {
        RenderResult result = GlyphRenderer.RenderSegoeGlyph(RobotCodepoint, sizePx);

        Assert.AreEqual(RenderStatus.Ok, result.Status);
        Assert.AreEqual(sizePx, result.Width);
        Assert.AreEqual(sizePx, result.Height);
        AssertValidPng(result.PngBytes, sizePx, sizePx);
    }

    /// <summary>
    /// Structural PNG validation without a decode dependency: signature bytes
    /// + the IHDR chunk's width/height fields, read directly per the PNG
    /// spec's fixed byte layout (8-byte signature, then a 4-byte length + 4-byte
    /// "IHDR" type + width(4)/height(4) big-endian, matching what
    /// <c>PngEncoder</c> (internal to Atv.IconRendering, not visible here) writes).
    /// </summary>
    private static void AssertValidPng(byte[]? png, int expectedWidth, int expectedHeight)
    {
        Assert.IsNotNull(png);
        byte[] expectedSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        CollectionAssert.AreEqual(expectedSignature, png[..8]);

        Assert.AreEqual("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        Assert.AreEqual(expectedWidth, width);
        Assert.AreEqual(expectedHeight, height);

        // IEND must be the final chunk.
        Assert.AreEqual("IEND", System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }
}
