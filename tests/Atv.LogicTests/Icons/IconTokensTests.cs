using Atv.Icons;

namespace Atv.LogicTests.Icons;

/// <summary>ERGO-20's token vocabulary parsing: curated Segoe name -> SegoeGlyph, a literal single character -> Emoji, anything else -> the RawPath escape hatch.</summary>
[TestClass]
public sealed class IconTokensTests
{
    [TestMethod]
    public void TryParse_CuratedName_ReturnsSegoeGlyphToken()
    {
        bool ok = IconTokens.TryParse("Robot", out IconToken token, out string? error);

        Assert.IsTrue(ok);
        Assert.IsNull(error);
        Assert.AreEqual(IconTokenKind.SegoeGlyph, token.Kind);
        Assert.AreEqual(0xE99A, token.Codepoint);
    }

    [TestMethod]
    public void TryParse_CuratedName_IsCaseInsensitive()
    {
        bool ok = IconTokens.TryParse("rObOt", out IconToken token, out _);

        Assert.IsTrue(ok);
        Assert.AreEqual(IconTokenKind.SegoeGlyph, token.Kind);
        Assert.AreEqual(0xE99A, token.Codepoint);
    }

    [TestMethod]
    public void TryParse_SingleSurrogatePairEmoji_ReturnsEmojiToken()
    {
        string fire = "\U0001F525"; // one supplementary-plane codepoint, two UTF-16 chars
        bool ok = IconTokens.TryParse(fire, out IconToken token, out string? error);

        Assert.IsTrue(ok);
        Assert.IsNull(error);
        Assert.AreEqual(IconTokenKind.Emoji, token.Kind);
        Assert.AreEqual(fire, token.Value);
        Assert.AreEqual(0x1F525, token.Codepoint);
    }

    [TestMethod]
    public void TryParse_SingleBmpEmojiCharacter_ReturnsEmojiToken()
    {
        string star = "⭐"; // BMP emoji ("star"), one UTF-16 char
        bool ok = IconTokens.TryParse(star, out IconToken token, out _);

        Assert.IsTrue(ok);
        Assert.AreEqual(IconTokenKind.Emoji, token.Kind);
        Assert.AreEqual(0x2B50, token.Codepoint);
    }

    [TestMethod]
    public void TryParse_MultiCharacterNonCuratedString_FallsBackToRawPath()
    {
        bool ok = IconTokens.TryParse(@"C:\icons\custom.png", out IconToken token, out string? error);

        Assert.IsTrue(ok);
        Assert.IsNull(error);
        Assert.AreEqual(IconTokenKind.RawPath, token.Kind);
        Assert.AreEqual(@"C:\icons\custom.png", token.Value);
    }

    [TestMethod]
    public void TryParse_UnknownMultiCharacterWord_FallsBackToRawPath()
    {
        // Not a curated name, not a single character -- the escape hatch is
        // deliberately permissive: whatever doesn't match the first two
        // tiers is carried through, unvalidated, as a path.
        bool ok = IconTokens.TryParse("NotACuratedName", out IconToken token, out _);

        Assert.IsTrue(ok);
        Assert.AreEqual(IconTokenKind.RawPath, token.Kind);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void TryParse_NullOrEmptyOrWhitespace_Fails(string? raw)
    {
        bool ok = IconTokens.TryParse(raw, out _, out string? error);

        Assert.IsFalse(ok);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void Default_IsARealCuratedSegoeGlyph()
    {
        Assert.AreEqual(IconTokenKind.SegoeGlyph, IconTokens.Default.Kind);
        Assert.IsTrue(IconTokens.CuratedSegoe.Values.Contains(IconTokens.Default.Codepoint));
    }

    [TestMethod]
    public void CuratedSegoe_EveryEntry_IsAUniqueNonZeroCodepoint()
    {
        var seen = new HashSet<int>();
        foreach (var (name, codepoint) in IconTokens.CuratedSegoe)
        {
            Assert.IsGreaterThan(0, codepoint, $"{name} has a non-positive codepoint.");
            Assert.IsTrue(seen.Add(codepoint), $"{name} duplicates a codepoint another curated entry already uses.");
        }
    }
}
