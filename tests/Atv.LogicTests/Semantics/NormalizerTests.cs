using Atv.Semantics;

namespace Atv.LogicTests.Semantics;

/// <summary>
/// AC4: the shared engine normalizer (LIFE-24 S2-walk item 2) -- collapse
/// whitespace runs (including embedded newlines) -&gt; strip light markdown
/// (<c>**</c>/backticks/<c>#</c>) -&gt; truncate with ellipsis per field
/// budget. Proven once here, reused by every field (goal/question/summary/
/// label) via <see cref="SemanticEngine"/>'s own use of it -- see
/// <see cref="SemanticEngineTransitionTests"/> for the "reused, not
/// reimplemented per field" half of this proof.
/// </summary>
[TestClass]
public sealed class NormalizerTests
{
    [TestMethod]
    public void Normalize_CollapsesMultipleSpacesIntoOne()
    {
        Assert.AreEqual("a b c", Normalizer.Normalize("a   b     c", 100));
    }

    [TestMethod]
    public void Normalize_CollapsesEmbeddedNewlinesIntoOneLine()
    {
        Assert.AreEqual("Fix the bug across all pages", Normalizer.Normalize("Fix the bug\nacross\nall pages", 100));
    }

    [TestMethod]
    public void Normalize_MultiLineUnicodeQuoteTortureString_LandsIntactAsOneLine()
    {
        // LIFE-24 S2-walk's torture scenario: multi-line unicode prompt with quotes.
        string raw = "Fix “the” bug\n日本語のテキスト\nwith 'quotes' and emoji 🎉\r\nacross lines";
        string result = Normalizer.Normalize(raw, 200);

        Assert.DoesNotContain('\n', result, "no embedded newline should survive normalization.");
        Assert.DoesNotContain('\r', result, "no embedded carriage return should survive normalization.");
        StringAssert.Contains(result, "“the”");
        StringAssert.Contains(result, "日本語のテキスト");
        StringAssert.Contains(result, "🎉");
        StringAssert.Contains(result, "'quotes'");
    }

    [TestMethod]
    public void Normalize_StripsBoldMarkdown_KeepsText()
    {
        Assert.AreEqual("this is important text", Normalizer.Normalize("this is **important** text", 100));
    }

    [TestMethod]
    public void Normalize_StripsInlineCode_KeepsText()
    {
        Assert.AreEqual("run npm test now", Normalizer.Normalize("run `npm test` now", 100));
    }

    [TestMethod]
    public void Normalize_StripsHeaderMarkers()
    {
        string result = Normalizer.Normalize("# Fix bug\nDo the thing", 100);
        Assert.DoesNotContain('#', result, "the leading '#' header marker must be stripped.");
        StringAssert.Contains(result, "Fix bug");
        StringAssert.Contains(result, "Do the thing");
    }

    [TestMethod]
    public void Normalize_TruncatesLongText_WithEllipsis_IncludingEllipsisInBudget()
    {
        string raw = new string('x', 50);
        string result = Normalizer.Normalize(raw, 10);

        Assert.AreEqual(10, result.Length);
        StringAssert.EndsWith(result, Normalizer.Ellipsis);
    }

    [TestMethod]
    public void Normalize_ShortText_UnderBudget_NeverTruncated()
    {
        Assert.AreEqual("short", Normalizer.Normalize("short", 100));
    }

    [TestMethod]
    public void Normalize_NullOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual("", Normalizer.Normalize(null, 100));
        Assert.AreEqual("", Normalizer.Normalize("", 100));
    }

    [TestMethod]
    public void Normalize_NonPositiveMaxLength_IsUnbounded()
    {
        string raw = new string('y', 500);
        Assert.AreEqual(raw, Normalizer.Normalize(raw, 0));
    }
}
