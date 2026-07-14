using System.Buffers.Binary;
using System.IO.Compression;

namespace Atv.IconRendering.Tests;

/// <summary>
/// A minimal test-only PNG pixel reader, the decode-side mirror of the
/// product's own <c>PngEncoder</c> (internal to <c>Atv.IconRendering</c>, not
/// visible here -- this is a fresh, independent implementation so a test
/// failure here means the PRODUCT'S bytes are wrong, not that a shared
/// encode/decode bug canceled out). Only needs to handle exactly what
/// <c>PngEncoder</c> ever emits: 8-bit RGBA, filter type "None" on every
/// scanline, a single IDAT chunk -- so this is deliberately not a
/// general-purpose PNG decoder.
/// </summary>
internal static class PngTestReader
{
    public static (int Width, int Height, byte[] Rgba) Decode(byte[] png)
    {
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!png.AsSpan(0, 8).SequenceEqual(signature))
            throw new ArgumentException("Not a PNG (bad signature).", nameof(png));

        int width = 0, height = 0;
        using var idat = new MemoryStream();

        int offset = 8;
        while (offset < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            int dataStart = offset + 8;

            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(dataStart, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(dataStart + 4, 4));
                byte colorType = png[dataStart + 9];
                if (colorType != 6)
                    throw new NotSupportedException($"Only RGBA (color type 6) is supported by this test reader; got {colorType}.");
            }
            else if (type == "IDAT")
            {
                idat.Write(png, dataStart, length);
            }
            else if (type == "IEND")
            {
                break;
            }

            offset = dataStart + length + 4; // + CRC
        }

        idat.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
            zlib.CopyTo(inflated);

        byte[] rawScanlines = inflated.ToArray();
        int stride = width * 4;
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * (stride + 1);
            byte filter = rawScanlines[rowStart];
            if (filter != 0)
                throw new NotSupportedException($"Only filter type 0 (None) is supported by this test reader; row {y} used {filter}.");
            Array.Copy(rawScanlines, rowStart + 1, rgba, y * stride, stride);
        }

        return (width, height, rgba);
    }

    public static (byte R, byte G, byte B, byte A) PixelAt(byte[] rgba, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }
}
