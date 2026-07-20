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
}
