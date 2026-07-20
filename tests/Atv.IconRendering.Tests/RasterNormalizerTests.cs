using System.Buffers.Binary;
using System.Text;
using Codevoid.AgentTaskVoid.IconRendering;

namespace Codevoid.AgentTaskVoid.IconRendering.Tests;

/// <summary>
/// Phase-16 acceptance criteria 2-3: PNG/JPG/ICO -&gt; 64px PNG normalization
/// (downscale, aspect-preserving transparent padding, transparency
/// flattened into one consistent straight-alpha channel), and the
/// validation/trust-surface requirements (format allowlist by magic number
/// -- not file extension, malformed-data rejection, a decompression-bomb
/// defense against a tiny file declaring an enormous frame size). Real WIC
/// decode throughout -- no fakes, matching <c>GlyphRenderingTests</c>'
/// precedent (nothing to fake against in this quarantined project).
///
/// Fixture bytes below are real, valid PNG/JPEG/ICO files generated once via
/// <c>System.Drawing</c> (a dev-machine prep step, not a build dependency of
/// this project) and embedded as base64 so the suite has zero external file
/// dependencies.
/// </summary>
[TestClass]
public sealed class RasterNormalizerTests
{
    private const int TargetSizePx = 64;

    // 10x6 solid-orange baseline JPEG (no alpha channel at all).
    private const string JpegBase64 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAGAAoDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDkqKKK/FD+qD//2Q==";

    // 16x16 solid-blue classic (BMP-in-ICO) icon.
    private const string IcoBase64 =
        "AAABAAEAEBAQAAAAAACoAQAAFgAAACgAAAAQAAAAIAAAAAEABAAAAAAAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAAACAAAAAgIAAgAAAAIAAgACAgAAAgICAAMDAwAAAAP8AAP8AAAD//wD/AAAA/wD/AP//AAD///8AZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmZmYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    // 40x10 non-square, fully-opaque PNG (System.Drawing default alpha=180 fill).
    private const string NonSquarePngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAACgAAAAKCAYAAADGmhxQAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAlSURBVDhPY+A8HrVlMGMGdIHBhkcdSCkedSCleNSBlOJRB1KKAeaP6v952ZMOAAAAAElFTkSuQmCC";

    // 20x20 PNG, left half opaque red (alpha 255), right half translucent red (alpha 64).
    private const string AlphaPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAYAAACNiR0NAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAA7SURBVDhP7cyhFQAgDAPR6k7F/iNhYIAceZWIiC/vanefoTVREL5ITDK0JCYZWhKTDC2JSYaWxOT/4QVh6J83Gfpw4QAAAABJRU5ErkJggg==";

    // A structurally valid PNG (real signature, real IHDR, a genuinely valid
    // zlib-wrapped IDAT) declaring a 50000x50000 frame but carrying only 2
    // scanlines' worth of actual compressed data -- nowhere near enough to
    // cover the declared frame. The decompression-bomb probe: must be
    // rejected on the DECLARED size alone, before any pixel data is copied.
    private const string BombPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAw1AAAMNQCAYAAABLrz3KAAABm0lEQVR4nO3BMQEAAADCoPVPbQsvoAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKCDARrcAAFI/hMWAAAAAElFTkSuQmCC";

    // ---- format allowlist (magic number, not extension) -----------------

    [TestMethod]
    public void HasAllowedMagic_Png_ReturnsTrue()
        => Assert.IsTrue(RasterNormalizer.HasAllowedMagic(Convert.FromBase64String(NonSquarePngBase64)));

    [TestMethod]
    public void HasAllowedMagic_Jpeg_ReturnsTrue()
        => Assert.IsTrue(RasterNormalizer.HasAllowedMagic(Convert.FromBase64String(JpegBase64)));

    [TestMethod]
    public void HasAllowedMagic_Ico_ReturnsTrue()
        => Assert.IsTrue(RasterNormalizer.HasAllowedMagic(Convert.FromBase64String(IcoBase64)));

    [TestMethod]
    public void HasAllowedMagic_ArbitraryText_ReturnsFalse()
        => Assert.IsFalse(RasterNormalizer.HasAllowedMagic(Encoding.ASCII.GetBytes("not an image, just text pretending to be one")));

    [TestMethod]
    public void HasAllowedMagic_EmptyOrTooShort_ReturnsFalse()
    {
        Assert.IsFalse(RasterNormalizer.HasAllowedMagic([]));
        Assert.IsFalse(RasterNormalizer.HasAllowedMagic([0x89, 0x50]));
    }

    [TestMethod]
    public void Normalize_DisallowedFormat_RejectsBeforeAnyDecodeAttempt()
    {
        NormalizeResult result = RasterNormalizer.Normalize(Encoding.ASCII.GetBytes("definitely not an image"), TargetSizePx);

        Assert.AreEqual(NormalizeStatus.DisallowedFormat, result.Status);
        Assert.IsNull(result.PngBytes);
        Assert.IsNotNull(result.Detail);
    }

    // ---- malformed / adversarial data (AC3, FAIL-1) ----------------------

    [TestMethod]
    public void Normalize_PngMagicButGarbageBody_ReturnsMalformed_NoThrow()
    {
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        byte[] garbage = new byte[64];
        new Random(42).NextBytes(garbage);
        byte[] fake = [.. signature, .. garbage];

        NormalizeResult result = RasterNormalizer.Normalize(fake, TargetSizePx);

        Assert.AreEqual(NormalizeStatus.Malformed, result.Status);
        Assert.IsNull(result.PngBytes);
    }

    [TestMethod]
    public void Normalize_DeclaredDimensionsExceedCap_RejectsBeforePixelDecode()
    {
        NormalizeResult result = RasterNormalizer.Normalize(Convert.FromBase64String(BombPngBase64), TargetSizePx);

        Assert.AreEqual(NormalizeStatus.DimensionsTooLarge, result.Status, result.Detail);
        Assert.IsNull(result.PngBytes);
        StringAssert.Contains(result.Detail, "50000");
    }

    [TestMethod]
    public void Normalize_ZeroOrNegativeSizePx_Throws()
    {
        byte[] png = Convert.FromBase64String(NonSquarePngBase64);
        Assert.Throws<ArgumentOutOfRangeException>(() => RasterNormalizer.Normalize(png, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RasterNormalizer.Normalize(png, -1));
    }

    // ---- successful normalization: each allowed format -> a valid TargetSizePx PNG ----

    [TestMethod]
    public void Normalize_AlreadySquarePng_ProducesExactTargetSizePng()
    {
        // Reuse the rendering project's own output as a guaranteed-valid, larger-than-target square PNG source.
        byte[] source = ShapeRenderer.RenderDefaultShape(128).PngBytes!;

        NormalizeResult result = RasterNormalizer.Normalize(source, TargetSizePx);

        Assert.AreEqual(NormalizeStatus.Ok, result.Status);
        var (width, height, _) = PngTestReader.Decode(result.PngBytes!);
        Assert.AreEqual(TargetSizePx, width);
        Assert.AreEqual(TargetSizePx, height);
    }

    [TestMethod]
    public void Normalize_Jpeg_ProducesTargetSizePng()
    {
        NormalizeResult result = RasterNormalizer.Normalize(Convert.FromBase64String(JpegBase64), TargetSizePx);

        Assert.AreEqual(NormalizeStatus.Ok, result.Status);
        var (width, height, _) = PngTestReader.Decode(result.PngBytes!);
        Assert.AreEqual(TargetSizePx, width);
        Assert.AreEqual(TargetSizePx, height);
    }

    [TestMethod]
    public void Normalize_Ico_ProducesTargetSizePng()
    {
        NormalizeResult result = RasterNormalizer.Normalize(Convert.FromBase64String(IcoBase64), TargetSizePx);

        Assert.AreEqual(NormalizeStatus.Ok, result.Status);
        var (width, height, _) = PngTestReader.Decode(result.PngBytes!);
        Assert.AreEqual(TargetSizePx, width);
        Assert.AreEqual(TargetSizePx, height);
    }

    [TestMethod]
    public void Normalize_SameSourceTwice_IsByteForByteDeterministic()
    {
        byte[] source = Convert.FromBase64String(JpegBase64);

        NormalizeResult first = RasterNormalizer.Normalize(source, TargetSizePx);
        NormalizeResult second = RasterNormalizer.Normalize(source, TargetSizePx);

        CollectionAssert.AreEqual(first.PngBytes, second.PngBytes);
    }

    // ---- AC2: non-square -> aspect-preserving TRANSPARENT padding; JPEG (no alpha) -> fully opaque where the image lands ----

    [TestMethod]
    public void Normalize_Jpeg_NoAlphaSource_IsFullyOpaqueInsideScaledBounds_AndTransparentInThePadding()
    {
        // Source is 10x6 (landscape); at a 64px square target the fit is
        // width-bound (scale = 64/10 = 6.4 -> scaledHeight = round(6*6.4) = 38),
        // so there IS letterbox padding above and below.
        NormalizeResult result = RasterNormalizer.Normalize(Convert.FromBase64String(JpegBase64), TargetSizePx);
        Assert.AreEqual(NormalizeStatus.Ok, result.Status);
        var (width, height, rgba) = PngTestReader.Decode(result.PngBytes!);
        Assert.AreEqual(TargetSizePx, width);
        Assert.AreEqual(TargetSizePx, height);

        // Top-left corner: outside the scaled image (letterbox padding) -> fully transparent.
        var padding = PngTestReader.PixelAt(rgba, width, 0, 0);
        Assert.AreEqual(0, padding.A, "letterbox padding around a non-square source must be transparent, not accent-tile-colored (ERGO-29: caller logos stay bare/full-bleed).");

        // Dead center: inside the scaled image (a JPEG source has no alpha channel at all) -> fully opaque.
        var center = PngTestReader.PixelAt(rgba, width, width / 2, height / 2);
        Assert.AreEqual(255, center.A, "a JPEG source has no alpha channel -- normalized output must be fully opaque wherever the source image actually lands.");
    }

    [TestMethod]
    public void Normalize_NonSquarePng_CentersScaledImage_SymmetricPadding()
    {
        NormalizeResult result = RasterNormalizer.Normalize(Convert.FromBase64String(NonSquarePngBase64), TargetSizePx);
        Assert.AreEqual(NormalizeStatus.Ok, result.Status);
        var (width, height, rgba) = PngTestReader.Decode(result.PngBytes!);

        // 40x10 source, width-bound fit -> scaledHeight = round(10 * 64/40) = 16;
        // padding = (64-16)/2 = 24 rows top and bottom, symmetric.
        int paddingRow = 5; // safely inside the top padding band
        var top = PngTestReader.PixelAt(rgba, width, width / 2, paddingRow);
        var bottom = PngTestReader.PixelAt(rgba, width, width / 2, height - 1 - paddingRow);
        Assert.AreEqual(0, top.A);
        Assert.AreEqual(0, bottom.A);

        // Fixture alpha is 180 (System.Drawing.Color.FromArgb(180, ...)), not opaque --
        // just needs to be clearly distinct from the fully-transparent padding.
        var middle = PngTestReader.PixelAt(rgba, width, width / 2, height / 2);
        Assert.IsGreaterThan(150, middle.A, "the scaled source image must occupy the vertical center of the padded canvas.");
    }

    // ---- AC2: transparency flattened into one consistent straight-alpha channel (not discarded) ----

    [TestMethod]
    public void Normalize_SemiTransparentPngSource_PreservesPartialAlpha_DistinctFromOpaqueHalf()
    {
        // 20x20 square source (no letterboxing: scale is exactly 1:1 mapped
        // to 64x64) -- left half alpha 255, right half alpha 64.
        NormalizeResult result = RasterNormalizer.Normalize(Convert.FromBase64String(AlphaPngBase64), TargetSizePx);
        Assert.AreEqual(NormalizeStatus.Ok, result.Status);
        var (width, height, rgba) = PngTestReader.Decode(result.PngBytes!);

        var opaqueSide = PngTestReader.PixelAt(rgba, width, width / 8, height / 2);       // well inside the left (alpha-255) half
        var translucentSide = PngTestReader.PixelAt(rgba, width, width - width / 8, height / 2); // well inside the right (alpha-64) half

        Assert.IsGreaterThan(230, opaqueSide.A, "the fully-opaque half of the source must normalize to (near-)full alpha.");
        Assert.IsLessThan(150, translucentSide.A, "the translucent half must survive normalization as PARTIAL alpha, not be flattened to fully opaque or fully transparent.");
        Assert.IsGreaterThan(0, translucentSide.A, "partial transparency must not be discarded entirely either.");
    }
}
