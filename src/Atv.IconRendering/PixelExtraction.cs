using Windows.Win32.Graphics.Imaging;

namespace Atv.IconRendering;

/// <summary>
/// Shared premultiplied-BGRA -&gt; straight-alpha RGBA8 pixel extraction, used
/// by both <see cref="SoftwareCanvas"/> (glyph/shape renders, always exactly
/// <c>sizePx</c> square) and <see cref="RasterNormalizer"/> (decoded/scaled
/// caller-supplied images, arbitrary width/height before letterbox padding).
/// Reads via <c>IWICBitmapSource.CopyPixels</c> -- every WIC bitmap-ish COM
/// type (<c>IWICBitmap</c>, <c>IWICFormatConverter</c>, <c>IWICBitmapScaler</c>,
/// ...) implements this interface, so one implementation covers every caller.
/// </summary>
internal static unsafe class PixelExtraction
{
    /// <summary>Copies <paramref name="width"/> x <paramref name="height"/> pixels out of <paramref name="source"/> (expected 32bppPBGRA -- premultiplied, matching every render target/format-converter target in this project) and returns them as straight-alpha RGBA8, row-major, no padding.</summary>
    public static byte[] ExtractStraightRgba(IWICBitmapSource* source, int width, int height)
    {
        int stride = width * 4;
        byte[] bgraPremultiplied = new byte[stride * height];
        fixed (byte* p = bgraPremultiplied)
        {
            source->CopyPixels(null, (uint)stride, (uint)bgraPremultiplied.Length, p);
        }

        byte[] rgba = new byte[bgraPremultiplied.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte b = bgraPremultiplied[i + 0];
            byte g = bgraPremultiplied[i + 1];
            byte r = bgraPremultiplied[i + 2];
            byte a = bgraPremultiplied[i + 3];

            if (a == 0)
            {
                // rgba[i..i+3] already zero-initialized.
                continue;
            }

            rgba[i + 0] = Unpremultiply(r, a);
            rgba[i + 1] = Unpremultiply(g, a);
            rgba[i + 2] = Unpremultiply(b, a);
            rgba[i + 3] = a;
        }
        return rgba;
    }

    private static byte Unpremultiply(byte channel, byte alpha)
        => (byte)Math.Min(255, (channel * 255 + alpha / 2) / alpha);
}
