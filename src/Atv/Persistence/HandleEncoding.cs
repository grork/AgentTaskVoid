using System.Globalization;
using System.Text;

namespace Codevoid.AgentTaskVoid.Persistence;

/// <summary>
/// Reversible percent-encoding of a caller-supplied handle (ERGO-6, "The
/// identifier a caller holds") into a Windows-filename-safe string, and back.
/// Shared by <see cref="SidecarStore"/> and <see cref="RecycleBin"/> -- both
/// key a per-handle file by this encoding (ERGO-21's "REVERSIBLE
/// percent-encoding ... never a one-way hash" ratification, 2026-07-07:
/// <c>list</c> and recycle-bin records must recover the handle from an
/// entry).
///
/// Deliberately narrower than <c>Uri.EscapeDataString</c>: that would
/// percent-encode every non-unreserved character, including ordinary Unicode
/// letters that are perfectly legal in an NTFS filename -- inflating length
/// ~3x per such character for no reason. Instead, only the characters
/// Windows actually forbids in a path SEGMENT (the nine reserved characters
/// below), ASCII control characters, and the escape character itself ('%')
/// are encoded; everything else -- including arbitrary Unicode, including
/// characters outside the BMP (encoded as UTF-16 surrogate pairs) -- passes
/// through as literal characters, copied verbatim.
///
/// Known accepted limitations (deliberately not solved here -- no test in
/// this codebase depends on either, and ERGO-21 rules out the usual fix for
/// both: hashing):
/// - NTFS is case-preserving but case-INSENSITIVE by default, so two handles
///   differing only by ASCII case could collide on lookup. Rare in practice
///   (handles are typically session ids/GUIDs/paths).
/// - Extremely long handles (low hundreds of characters, worse if full of
///   reserved characters) can still exceed NTFS's 255-UTF-16-code-unit
///   filename component limit once escaped. A build-phase-tunable truncation
///   scheme would fix this; not attempted here.
/// </summary>
public static class HandleEncoding
{
    private const char EscapeChar = '%';
    private static readonly char[] ReservedChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*', EscapeChar];

    /// <summary>Encodes an arbitrary handle into a string safe to use as a single Windows filename component (once an extension is appended).</summary>
    public static string Encode(string handle)
    {
        var sb = new StringBuilder(handle.Length);
        foreach (char c in handle)
        {
            if (c < 0x20 || Array.IndexOf(ReservedChars, c) >= 0)
                sb.Append(EscapeChar).Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Inverse of <see cref="Encode"/>. Recovers the original handle from an encoded filename component.</summary>
    public static string Decode(string encoded)
    {
        var sb = new StringBuilder(encoded.Length);
        for (int i = 0; i < encoded.Length; i++)
        {
            char c = encoded[i];
            if (c == EscapeChar && i + 2 < encoded.Length && TryParseHexByte(encoded, i + 1, out int value))
            {
                sb.Append((char)value);
                i += 2;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static bool TryParseHexByte(string s, int index, out int value)
        => int.TryParse(s.AsSpan(index, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
}
