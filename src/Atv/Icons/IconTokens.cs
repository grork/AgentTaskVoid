using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace Codevoid.AgentTaskVoid.Icons;

/// <summary>
/// How an <see cref="IconToken"/> was specified. <see cref="Emoji"/> and
/// <see cref="SegoeGlyph"/> both render via <c>Codevoid.AgentTaskVoid.IconRendering.GlyphRenderer</c>
/// (bypassed entirely for <see cref="RawPath"/>, which points at an existing
/// image file). Phase 16 (ERGO-29): <see cref="RawPath"/> is a supported,
/// validated, NORMALIZED input -- both <c>--icon-file &lt;path&gt;</c>
/// (explicit) and <c>--icon &lt;anything that isn't a curated name or a
/// single character&gt;</c> (this type's third parse tier, unchanged) produce
/// it, and <c>Codevoid.AgentTaskVoid.Icons.IconService</c> reads/validates/normalizes the target
/// file via <c>Codevoid.AgentTaskVoid.IconRendering.RasterNormalizer</c> before caching it --
/// no longer a raw, unvalidated byte-copy.
/// </summary>
public enum IconTokenKind
{
    Emoji,
    SegoeGlyph,
    RawPath,
}

/// <summary>
/// A parsed <c>--icon</c> value (ERGO-20). <see cref="Value"/> is the literal
/// text to render (the emoji character, or the Segoe glyph as a one-character
/// string) for <see cref="IconTokenKind.Emoji"/>/<see cref="IconTokenKind.SegoeGlyph"/>,
/// or the raw file path for <see cref="IconTokenKind.RawPath"/>.
/// <see cref="Codepoint"/> is meaningful only for <see cref="IconTokenKind.SegoeGlyph"/>
/// (the value <c>Codevoid.AgentTaskVoid.IconRendering.GlyphRenderer.RenderSegoeGlyph</c> and
/// <c>Codevoid.AgentTaskVoid.IconRendering.GlyphProbe</c> key off).
/// </summary>
public readonly record struct IconToken(IconTokenKind Kind, string Value, int Codepoint = 0)
{
    public static IconToken Emoji(string text) => new(IconTokenKind.Emoji, text, char.ConvertToUtf32(text, 0));

    public static IconToken Segoe(int codepoint) => new(IconTokenKind.SegoeGlyph, char.ConvertFromUtf32(codepoint), codepoint);

    public static IconToken RawPath(string path) => new(IconTokenKind.RawPath, path);
}

/// <summary>
/// ERGO-20's two built-in icon vocabularies -- single source of truth
/// (plan/README.md standing invariant #8: empirical platform knowledge lives
/// as data in one place) for both:
/// <list type="bullet">
/// <item>the curated Segoe Fluent Icons codepoint list this phase builds
/// (glyphs that render well at taskbar size and exist on target Windows 11
/// builds -- verified against the official
/// learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font
/// reference, 2026-07-09, not guessed from memory);</item>
/// <item>the ERGO-12 default icon glyph used when a caller supplies no
/// <c>--icon</c>.</item>
/// </list>
/// A raw file-path fallback ships too (ERGO-20 left it a build-time call;
/// decided in because <see cref="TryParse"/>'s fallback case is trivial: any
/// input that is neither a curated name nor a single literal character is
/// just carried through as a path). Phase 16 (ERGO-29) promoted this from an
/// undocumented hatch to a supported input -- the path is validated and
/// normalized by <c>Codevoid.AgentTaskVoid.Icons.IconService</c> before use, same as the
/// dedicated <c>--icon-file</c> flag.
/// </summary>
public static class IconTokens
{
    /// <summary>
    /// Curated Segoe Fluent Icons glyphs, by name (case-insensitive lookup
    /// key for <see cref="TryParse"/>). Deliberately small and hand-picked --
    /// common, unambiguous, small-size-legible symbols useful for describing
    /// agent task state/kind (status glyphs, dev/bug/robot glyphs, common
    /// actions) rather than an exhaustive dump of the ~1500-glyph font.
    /// Codepoints verified against the official Microsoft Learn glyph table,
    /// not transcribed from memory.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> CuratedSegoe = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Edit"] = 0xE70F,
        ["Add"] = 0xE710,
        ["Cancel"] = 0xE711,
        ["Settings"] = 0xE713,
        ["Mail"] = 0xE715,
        ["Link"] = 0xE71B,
        ["Search"] = 0xE721,
        ["Refresh"] = 0xE72C,
        ["FavoriteStar"] = 0xE734,
        ["FavoriteStarFill"] = 0xE735,
        ["CheckMark"] = 0xE73E,
        ["Delete"] = 0xE74D,
        ["Globe"] = 0xE774,
        ["Error"] = 0xE783,
        ["Warning"] = 0xE7BA,
        ["Flag"] = 0xE7C1,
        ["Home"] = 0xE80F,
        ["Sync"] = 0xE895,
        ["Important"] = 0xE8C9,
        ["Accept"] = 0xE8FB,
        ["Clock"] = 0xE917,
        ["Completed"] = 0xE930,
        ["Robot"] = 0xE99A,
        ["StatusWarning"] = 0xEA84,
        ["Heart"] = 0xEB51,
        ["Bug"] = 0xEBE8,
        ["CompletedSolid"] = 0xEC61,
        ["DeveloperTools"] = 0xEC7A,
        ["StatusDiamondOuter"] = 0xF178,
        ["WarningSolid"] = 0xF736,
    };

    /// <summary>ERGO-12's default icon glyph, used when the caller supplies no <c>--icon</c>: the "Robot" Segoe Fluent Icons glyph -- fits an agent-task-tracking tool unprompted.</summary>
    public static readonly IconToken Default = IconToken.Segoe(CuratedSegoe["Robot"]);

    /// <summary>
    /// ERGO-34's curated emoji pool, by Unicode scalar codepoint (not a
    /// literal glyph character in source -- storing the codepoint, exactly
    /// like <see cref="CuratedSegoe"/>'s hex values, sidesteps any risk of an
    /// invisible trailing variation selector or an accidental multi-codepoint
    /// paste sneaking into the pool; <see cref="IconToken.Emoji(string)"/>
    /// reconstructs the single-scalar string via
    /// <see cref="char.ConvertFromUtf32(int)"/>, which is single-scalar BY
    /// CONSTRUCTION). Curation bar (standing invariant #8's "empirical
    /// platform data lives in one place", extended here): visually distinct
    /// at taskbar size, common, long-established Unicode emoji believed
    /// present in Windows 11 26100's Segoe UI Emoji font -- a review
    /// obligation (not machine-checkable) noted on
    /// <see cref="Semantics.SemanticEngine"/>'s repo-hash pick. Every entry is
    /// independently guaranteed a single Unicode scalar (no ZWJ sequences, no
    /// regional-indicator flag pairs, no keycaps, no skin-tone/variation-selector
    /// forms) -- <c>IconTokensTests</c> proves this mechanically via
    /// <see cref="TryParse"/>/<c>GlyphProbe</c>, not just by this comment.
    /// </summary>
    public static readonly IReadOnlyList<int> CuratedEmojiCodepoints =
    [
        // Faces/emotions
        0x1F600, 0x1F601, 0x1F602, 0x1F603, 0x1F604, 0x1F605, 0x1F606, 0x1F609,
        0x1F60A, 0x1F60B, 0x1F60D, 0x1F60E, 0x1F60F, 0x1F610, 0x1F612, 0x1F618,
        0x1F636, 0x1F642, 0x1F643, 0x1F644, 0x1F914, 0x1F917, 0x1F973, 0x1F60C,
        0x1F62D, 0x1F630, 0x1F631, 0x1F62C, 0x1F634, 0x1F637, 0x1F912, 0x1F92A,
        0x1F638, 0x1F639, 0x1F63B, 0x1F63D, 0x1F47B, 0x1F47D, 0x1F916, 0x1F383,
        // Gestures/body
        0x1F44D, 0x1F44E, 0x1F44F, 0x1F44C, 0x1F44A, 0x1F64C, 0x1F64F, 0x1F91D,
        0x1F44B, 0x270C, 0x1F91E, 0x1F918, 0x1F64B,
        // Animals/nature
        0x1F436, 0x1F431, 0x1F42D, 0x1F439, 0x1F430, 0x1F98A, 0x1F43B, 0x1F43C,
        0x1F428, 0x1F42F, 0x1F42E, 0x1F437, 0x1F438, 0x1F435, 0x1F414, 0x1F427,
        0x1F426, 0x1F41D, 0x1F98B, 0x1F41F, 0x1F433, 0x1F42C, 0x1F419, 0x1F420,
        0x1F994, 0x1F984, 0x1F40D, 0x1F996, 0x1F41E, 0x2600, 0x2601, 0x26C5,
        0x2614, 0x2744, 0x26A1, 0x2B50, 0x2764,
        // Food
        0x1F34E, 0x1F34C, 0x1F34A, 0x1F34B, 0x1F349, 0x1F347, 0x1F353, 0x1F351,
        0x1F352, 0x1F34D, 0x1F345, 0x1F955, 0x1F33D, 0x1F35E, 0x1F369, 0x1F36A,
        0x1F36B, 0x1F36C, 0x1F36D, 0x1F382,
        // Objects/activities/symbols
        0x26BD, 0x1F3C0, 0x1F3C8, 0x26BE, 0x1F3BE, 0x1F3B1, 0x1F3AF, 0x1F3AE,
        0x1F3B2, 0x1F3A8, 0x1F3AC, 0x1F3A4, 0x1F3A7, 0x1F3B8, 0x1F3B9, 0x1F3BA,
        0x1F680, 0x2708, 0x26F5, 0x1F681, 0x1F697, 0x1F695, 0x1F6F5, 0x1F3AA,
        0x26F2, 0x1F3D4, 0x1F30B, 0x1F3D6,
    ];

    /// <summary>ERGO-34's combined default-icon pool: <see cref="CuratedSegoe"/>'s glyphs ∪ <see cref="CuratedEmojiCodepoints"/>'s emoji, as plain data (order matters -- it is part of the pinned SHA-256 pick recipe, see <see cref="TryPickRepoIcon"/>). Target ≥100 combined entries so collisions stay rare at realistic repo counts; collisions are still possible and accepted (the card's title carries the durable identity).</summary>
    public static readonly IReadOnlyList<IconToken> CombinedDefaultPool =
    [
        .. CuratedSegoe.Values.Select(IconToken.Segoe),
        .. CuratedEmojiCodepoints.Select(cp => IconToken.Emoji(char.ConvertFromUtf32(cp))),
    ];

    /// <summary>
    /// ERGO-34's deterministic per-repo pick: normalizes <paramref name="keyPath"/>
    /// (<see cref="Path.GetFullPath(string)"/>, trim trailing directory
    /// separators, <see cref="string.ToUpperInvariant"/>), SHA-256s the UTF-8
    /// bytes, takes the first 8 bytes as a big-endian unsigned integer, and
    /// mods by <see cref="CombinedDefaultPool"/>'s count -- pinned exactly so
    /// callers can precompute the expected pick. Deliberately NEVER
    /// <see cref="object.GetHashCode"/> (per-process randomized, would break
    /// the "same repo -&gt; same icon" determinism guarantee across process
    /// restarts). Any change to this recipe or to the pool's membership/order
    /// can reassign icons on upgrade -- accepted, documented. Never throws
    /// (FAIL-1): a malformed <paramref name="keyPath"/> that fails to
    /// normalize returns <see langword="false"/>, letting the caller floor to
    /// the plain Robot default instead.
    /// </summary>
    public static bool TryPickRepoIcon(string keyPath, out IconToken token)
    {
        try
        {
            string normalized = Path.GetFullPath(keyPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

            ulong firstEightBytes = 0;
            for (int i = 0; i < 8; i++)
                firstEightBytes = (firstEightBytes << 8) | hash[i];

            int index = (int)(firstEightBytes % (ulong)CombinedDefaultPool.Count);
            token = CombinedDefaultPool[index];
            return true;
        }
        catch (Exception)
        {
            token = default;
            return false;
        }
    }

    /// <summary>
    /// Parses a raw <c>--icon</c> value into an <see cref="IconToken"/>.
    /// Three tiers, tried in order, matching ERGO-20's two built-in
    /// vocabularies plus the escape hatch:
    /// <list type="number">
    /// <item>a <see cref="CuratedSegoe"/> name (case-insensitive, exact
    /// match) -&gt; <see cref="IconTokenKind.SegoeGlyph"/>;</item>
    /// <item>a single literal Unicode scalar value -- one BMP character, or a
    /// valid UTF-16 surrogate pair encoding one supplementary-plane character
    /// (covers the vast majority of emoji, which are single codepoints) -&gt;
    /// <see cref="IconTokenKind.Emoji"/>. Deliberately does not attempt full
    /// extended-grapheme-cluster segmentation (multi-codepoint ZWJ sequences
    /// like family/flag combinations) -- that needs ICU-backed text
    /// segmentation, at odds with this project's
    /// <c>InvariantGlobalization</c> AOT setting; "a literal character" in
    /// ERGO-20's own wording already scopes v1 to single characters. Never
    /// validated against <see cref="Codevoid.AgentTaskVoid.IconRendering.GlyphProbe"/> here --
    /// an emoji the font doesn't have is a clean, non-throwing parse success
    /// that the caller's fallback chain (<c>IconService</c>) resolves at
    /// render time;</item>
    /// <item>anything else -&gt; <see cref="IconTokenKind.RawPath"/>, carried
    /// through verbatim and unvalidated (the advanced escape hatch -- whether
    /// it resolves to a real, readable image file is for whatever consumes
    /// it to discover).</item>
    /// </list>
    /// Only fails (returns <see langword="false"/>) for a null/empty/whitespace-only input.
    /// </summary>
    public static bool TryParse(string? raw, out IconToken token, out string? error)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            token = default;
            error = "An icon token cannot be empty.";
            return false;
        }

        if (CuratedSegoe.TryGetValue(raw, out int codepoint))
        {
            token = IconToken.Segoe(codepoint);
            error = null;
            return true;
        }

        if (IsSingleUnicodeScalar(raw))
        {
            token = IconToken.Emoji(raw);
            error = null;
            return true;
        }

        token = IconToken.RawPath(raw);
        error = null;
        return true;
    }

    private static bool IsSingleUnicodeScalar(string s)
        => s.Length == 1 || (s.Length == 2 && char.IsSurrogatePair(s[0], s[1]));

    /// <summary>A short, human-readable description of <paramref name="token"/> -- used by <c>doctor</c>'s Part 4 "would-pick" diagnostic line (a curated Segoe name when one matches, else the raw codepoint/emoji/path).</summary>
    public static string Describe(IconToken token) => token.Kind switch
    {
        IconTokenKind.SegoeGlyph => (CuratedSegoe.Where(kv => kv.Value == token.Codepoint).Select(kv => kv.Key).FirstOrDefault()) is { Length: > 0 } name
            ? $"Segoe:{name}"
            : $"Segoe:U+{token.Codepoint:X4}",
        IconTokenKind.Emoji => $"Emoji:{token.Value}",
        IconTokenKind.RawPath => $"Path:{token.Value}",
        _ => token.Value,
    };
}
