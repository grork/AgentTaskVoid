using Windows.Win32;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace Atv.IconRendering;

/// <summary>Why a <see cref="RasterNormalizer.Normalize"/> call did not produce a usable PNG. Distinct reasons so <c>Atv.Icons.IconService</c> can log something more useful than "failed" (FAIL-3).</summary>
public enum NormalizeStatus
{
    /// <summary><see cref="NormalizeResult.PngBytes"/> holds a valid, normalized 64px PNG.</summary>
    Ok,

    /// <summary>The source bytes don't start with a recognized PNG/JPEG/ICO magic number -- the format allowlist (ERGO-29).</summary>
    DisallowedFormat,

    /// <summary>The decoded frame's declared pixel dimensions exceed <see cref="RasterNormalizer.MaxSourceDimensionPx"/> -- refused BEFORE any pixel data is copied (decompression-bomb defense: a tiny file can declare an enormous width/height).</summary>
    DimensionsTooLarge,

    /// <summary>WIC could not decode the image at all (corrupt/truncated/adversarial bytes past the magic-number check), or the decoded frame was otherwise unusable (e.g. zero-sized).</summary>
    Malformed,
}

/// <summary>Result of one <see cref="RasterNormalizer.Normalize"/> call. <see cref="PngBytes"/> is non-null iff <see cref="Status"/> is <see cref="NormalizeStatus.Ok"/>; <see cref="Detail"/> is a human-readable reason, non-null iff <see cref="Status"/> is NOT <see cref="NormalizeStatus.Ok"/>.</summary>
public readonly record struct NormalizeResult(NormalizeStatus Status, byte[]? PngBytes, string? Detail)
{
    public static NormalizeResult Ok(byte[] pngBytes) => new(NormalizeStatus.Ok, pngBytes, null);

    public static NormalizeResult Fail(NormalizeStatus status, string detail) => new(status, null, detail);
}

/// <summary>
/// ERGO-29's bring-your-own-image normalization: arbitrary caller-supplied
/// PNG/JPG/ICO bytes -&gt; the pipeline's one 64px PNG shape, via WIC decode
/// (no filesystem access here -- <c>Atv.Icons.IconService</c> owns reading the
/// source file and the byte-size cap; this type only ever sees bytes already
/// in memory, matching ERGO-22's "pure mechanism, no filesystem" project
/// boundary).
///
/// Pipeline: format allowlist (magic-number sniff, not file-extension trust)
/// -&gt; WIC decode -&gt; declared-dimension cap check BEFORE any pixel data is
/// copied (the decompression-bomb defense: <see cref="NormalizeStatus.DimensionsTooLarge"/>
/// fires off <c>IWICBitmapFrameDecode.GetSize</c> alone, which reads only the
/// container header) -&gt; convert to 32bppPBGRA (this is where ICO's AND-mask/
/// indexed transparency and PNG's native alpha both collapse into the SAME
/// straight-RGBA representation the rest of this project already uses --
/// AC2's "transparency is flattened": every source format's own transparency
/// encoding is normalized into one consistent alpha channel, not discarded;
/// a format with no alpha at all, like JPEG, is simply fully opaque
/// throughout) -&gt; aspect-preserving scale to fit inside the target square
/// -&gt; centered onto a fully-transparent target-size canvas (non-square
/// sources get transparent letterbox padding, never accent-tile padding --
/// ERGO-29's "probably bare/full-bleed" build choice: a caller's own logo is
/// never composited onto <see cref="TileCompositor"/>'s tile, only glyphs
/// are) -&gt; <see cref="PngEncoder"/>.
///
/// Every failure mode returns a typed <see cref="NormalizeResult"/>, never an
/// exception (FAIL-1): a broad catch around the whole WIC pipeline maps any
/// unexpected COM/interop failure -- corrupt headers, a decoder WIC itself
/// rejects, an out-of-range dimension it chokes on -- to
/// <see cref="NormalizeStatus.Malformed"/>, so a hostile or simply broken
/// input degrades to the caller's fallback chain instead of taking the whole
/// verb down.
/// </summary>
public static unsafe class RasterNormalizer
{
    /// <summary>
    /// Declared pixel-dimension cap, checked against <c>IWICBitmapFrameDecode.GetSize</c>
    /// BEFORE any pixel data is ever copied out of the decoder -- the
    /// decompression-bomb defense (a few dozen bytes can declare a
    /// 50000x50000 frame; refusing on the declared size alone means we never
    /// attempt to materialize that buffer). 2048px is already 32x the
    /// pipeline's 64px target in each dimension -- generous headroom for any
    /// reasonable source logo, small enough to bound worst-case decode memory
    /// to ~16 MB (2048*2048*4 bytes) rather than gigabytes.
    /// </summary>
    public const int MaxSourceDimensionPx = 2048;

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] IcoMagic = [0x00, 0x00, 0x01, 0x00];

    /// <summary>The format allowlist (ERGO-29): does <paramref name="header"/> start with a recognized PNG, JPEG, or ICO magic number? Checked against the file's own bytes, never its extension -- an attacker-renamed file is still caught.</summary>
    public static bool HasAllowedMagic(ReadOnlySpan<byte> header)
        => StartsWith(header, PngMagic) || StartsWith(header, JpegMagic) || StartsWith(header, IcoMagic);

    private static bool StartsWith(ReadOnlySpan<byte> header, byte[] magic)
        => header.Length >= magic.Length && header[..magic.Length].SequenceEqual(magic);

    /// <summary>Normalizes already-in-memory source image bytes to a <paramref name="sizePx"/> x <paramref name="sizePx"/> PNG. See the type-level remarks for the full pipeline and every failure mode.</summary>
    public static NormalizeResult Normalize(byte[] sourceBytes, int sizePx)
    {
        if (sizePx <= 0) throw new ArgumentOutOfRangeException(nameof(sizePx));

        if (!HasAllowedMagic(sourceBytes))
            return NormalizeResult.Fail(NormalizeStatus.DisallowedFormat, "source bytes are not a recognized PNG, JPEG, or ICO image (magic-number check).");

        try
        {
            return NormalizeCore(sourceBytes, sizePx);
        }
        catch (Exception ex)
        {
            return NormalizeResult.Fail(NormalizeStatus.Malformed, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static NormalizeResult NormalizeCore(byte[] sourceBytes, int sizePx)
    {
        IWICStream* stream = null;
        IWICBitmapDecoder* decoder = null;
        IWICBitmapFrameDecode* frame = null;
        IWICFormatConverter* converter = null;
        IWICBitmapScaler* scaler = null;
        try
        {
            Interop.WicFactory->CreateStream(&stream);
            fixed (byte* p = sourceBytes)
            {
                stream->InitializeFromMemory(p, (uint)sourceBytes.Length);
            }

            // CsWin32 only projects the throwing convenience overload for this
            // method (returns the pointer directly instead of an HRESULT +
            // out-param) -- a decode failure surfaces as an exception, caught
            // by Normalize's broad catch and mapped to NormalizeStatus.Malformed.
            decoder = Interop.WicFactory->CreateDecoderFromStream((IStream*)stream, (Guid*)null, WICDecodeOptions.WICDecodeMetadataCacheOnDemand);

            decoder->GetFrame(0, &frame);

            uint srcWidth, srcHeight;
            frame->GetSize(&srcWidth, &srcHeight);
            if (srcWidth == 0 || srcHeight == 0)
                return NormalizeResult.Fail(NormalizeStatus.Malformed, "decoded frame has zero width or height.");
            if (srcWidth > MaxSourceDimensionPx || srcHeight > MaxSourceDimensionPx)
                return NormalizeResult.Fail(NormalizeStatus.DimensionsTooLarge, $"source image is {srcWidth}x{srcHeight}px, exceeds the {MaxSourceDimensionPx}px cap -- refused before any pixel data was decoded.");

            Interop.WicFactory->CreateFormatConverter(&converter);
            converter->Initialize(
                (IWICBitmapSource*)frame,
                PInvoke.GUID_WICPixelFormat32bppPBGRA,
                WICBitmapDitherType.WICBitmapDitherTypeNone,
                null,
                0.0,
                WICBitmapPaletteType.WICBitmapPaletteTypeCustom);

            double scale = Math.Min((double)sizePx / srcWidth, (double)sizePx / srcHeight);
            int scaledWidth = Math.Max(1, (int)Math.Round(srcWidth * scale));
            int scaledHeight = Math.Max(1, (int)Math.Round(srcHeight * scale));

            byte[] scaledRgba;
            if (scaledWidth == (int)srcWidth && scaledHeight == (int)srcHeight)
            {
                // Already exactly target-fit (e.g. the source is already sizePx square) -- skip the scaler entirely.
                scaledRgba = PixelExtraction.ExtractStraightRgba((IWICBitmapSource*)converter, scaledWidth, scaledHeight);
            }
            else
            {
                Interop.WicFactory->CreateBitmapScaler(&scaler);
                scaler->Initialize((IWICBitmapSource*)converter, (uint)scaledWidth, (uint)scaledHeight, WICBitmapInterpolationMode.WICBitmapInterpolationModeFant);
                scaledRgba = PixelExtraction.ExtractStraightRgba((IWICBitmapSource*)scaler, scaledWidth, scaledHeight);
            }

            byte[] canvas = LetterboxCenter(scaledRgba, scaledWidth, scaledHeight, sizePx);
            byte[] png = PngEncoder.Encode(canvas, sizePx, sizePx);
            return NormalizeResult.Ok(png);
        }
        finally
        {
            if (scaler != null) scaler->Release();
            if (converter != null) converter->Release();
            if (frame != null) frame->Release();
            if (decoder != null) decoder->Release();
            if (stream != null) stream->Release();
        }
    }

    /// <summary>
    /// Centers a <paramref name="srcWidth"/> x <paramref name="srcHeight"/>
    /// straight-RGBA image onto a fully-transparent <paramref name="sizePx"/>
    /// square canvas (AC2's "non-square images fit with aspect-preserving
    /// padding"). The padding is transparent, never the accent tile -- caller
    /// logos stay bare/full-bleed (ERGO-29).
    /// </summary>
    private static byte[] LetterboxCenter(byte[] srcRgba, int srcWidth, int srcHeight, int sizePx)
    {
        byte[] canvas = new byte[sizePx * sizePx * 4]; // zero-initialized: fully transparent
        int offsetX = (sizePx - srcWidth) / 2;
        int offsetY = (sizePx - srcHeight) / 2;
        int srcStride = srcWidth * 4;
        int dstStride = sizePx * 4;

        for (int y = 0; y < srcHeight; y++)
        {
            int dstY = offsetY + y;
            if (dstY < 0 || dstY >= sizePx) continue;
            Buffer.BlockCopy(srcRgba, y * srcStride, canvas, dstY * dstStride + offsetX * 4, srcStride);
        }

        return canvas;
    }
}
