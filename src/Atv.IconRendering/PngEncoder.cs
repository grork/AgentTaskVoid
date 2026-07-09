using System.Buffers.Binary;
using System.IO.Compression;

namespace Atv.IconRendering;

/// <summary>
/// A minimal, dependency-free PNG encoder: straight (non-premultiplied) RGBA8
/// pixels in -> PNG bytes out. No filesystem, no WIC encoder/IStream interop
/// -- ERGO-22 scopes this rendering project to "no filesystem, no handles,
/// no caching, no policy", and the only IMAGE ENCODING primitive the BCL
/// lacks natively is exactly this: PNG's chunk framing (trivial) wrapped
/// around zlib-compressed scanlines, and <see cref="ZLibStream"/>
/// (RFC 1950, .NET 6+) already produces exactly the byte stream a PNG IDAT
/// chunk expects, so no bespoke deflate/Adler32 implementation is needed
/// either.
///
/// Deliberately sidesteps <c>IWICBitmapEncoder</c>/<c>IWICStream</c>: WIC's
/// PNG encoder needs an <c>IStream</c> target, and getting bytes back out of
/// one is real extra COM surface (either a growable-HGLOBAL stream via
/// classic marshaling, or a fixed-capacity <c>IWICStream.InitializeFromMemory</c>
/// buffer) for a step that a ~60-line managed encoder replaces outright,
/// deterministically, with zero additional interop risk. The RENDERING
/// itself (the part ERGO-22 actually cares about being GPU-free/deterministic)
/// still goes through the real D2D/WIC software bitmap pipeline
/// (<see cref="GlyphRenderer"/>/<see cref="ShapeRenderer"/>); this class only
/// serializes whatever raw pixels that pipeline produced.
/// </summary>
internal static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>
    /// Encodes <paramref name="rgba"/> (straight-alpha, row-major, 4 bytes/px,
    /// no padding) as an 8-bit RGBA PNG. <paramref name="rgba"/>.Length must
    /// equal <c>width * height * 4</c>.
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (rgba.Length != checked(width * height * 4))
            throw new ArgumentException($"Expected {width * height * 4} bytes for a {width}x{height} RGBA buffer, got {rgba.Length}.", nameof(rgba));

        using var output = new MemoryStream();
        output.Write(Signature);

        WriteChunk(output, "IHDR", BuildIhdr(width, height));
        WriteChunk(output, "IDAT", BuildIdat(rgba, width, height));
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    private static byte[] BuildIhdr(int width, int height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type: RGBA
        ihdr[10] = 0; // compression method: deflate (the only defined value)
        ihdr[11] = 0; // filter method: adaptive (the only defined value)
        ihdr[12] = 0; // interlace method: none
        return ihdr;
    }

    private static byte[] BuildIdat(ReadOnlySpan<byte> rgba, int width, int height)
    {
        int stride = width * 4;
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var rowFilterByte = (stackalloc byte[1]);
            rowFilterByte[0] = 0; // filter type "None" for every scanline -- simplest correct choice for small icon-sized images
            for (int y = 0; y < height; y++)
            {
                zlib.Write(rowFilterByte);
                zlib.Write(rgba.Slice(y * stride, stride));
            }
        }
        return raw.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBuf, data.Length);
        output.Write(lengthBuf);

        Span<byte> typeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        output.Write(typeBytes);
        output.Write(data);

        uint crc = Crc32.Compute(typeBytes, data);
        Span<byte> crcBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBuf, crc);
        output.Write(crcBuf);
    }
}

/// <summary>The CRC-32 (ISO 3309/ITU-T V.42, polynomial 0xEDB88320) PNG chunks are checksummed with -- the same algorithm zlib/gzip use, just not exposed as a public BCL API.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte x in a) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (byte x in b) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
