using Codevoid.AgentTaskVoid.Persistence;

namespace Codevoid.AgentTaskVoid.LogicTests.Persistence;

/// <summary>
/// Covers phase-04 acceptance criterion 2's "handle encoding round-trips
/// hostile handles (path separators, unicode, long strings)" at the pure
/// function level. <see cref="SidecarStoreTests"/> covers the same property
/// through actual file round-trips.
/// </summary>
[TestClass]
public sealed class HandleEncodingTests
{
    [TestMethod]
    [DataRow("simple-handle")]
    [DataRow("session/with/slashes")]
    [DataRow(@"C:\Users\dev\transcript.jsonl")]
    [DataRow("weird:chars?*|\"<>")]
    [DataRow("unicode-japanese-and-emoji-\U0001F680-cafe-with-accent-é")]
    [DataRow("percent%literal%signs")]
    [DataRow("")]
    [DataRow("   leading and trailing spaces   ")]
    [DataRow("trailing-dot.")]
    public void EncodeThenDecode_RoundTripsHostileHandles(string handle)
    {
        string encoded = HandleEncoding.Encode(handle);
        string decoded = HandleEncoding.Decode(encoded);
        Assert.AreEqual(handle, decoded);
    }

    [TestMethod]
    public void EncodeThenDecode_RoundTrips_EmbeddedControlCharacter()
    {
        // Built at runtime (not as a source-literal DataRow) to keep the raw
        // control byte itself out of the .cs source text.
        char controlChar = (char)7;
        string handle = "before" + controlChar + "after";

        string encoded = HandleEncoding.Encode(handle);

        Assert.AreEqual(handle, HandleEncoding.Decode(encoded));
        Assert.DoesNotContain(controlChar, encoded, "the raw control character must not survive into the encoded filename component");
    }

    [TestMethod]
    public void EncodeThenDecode_RoundTrips_LongString()
    {
        // A realistic "long" handle (e.g. a transcript file path) -- long
        // enough to matter, short enough that even fully-escaped it stays
        // under NTFS's 255-UTF-16-code-unit filename-component limit.
        string handle = string.Concat(Enumerable.Repeat("abcdefghij-", 15));
        Assert.IsGreaterThan(100, handle.Length);

        string encoded = HandleEncoding.Encode(handle);
        Assert.IsLessThan(255, encoded.Length, "encoded filename component must stay under NTFS's 255-char limit for this test's input");
        Assert.AreEqual(handle, HandleEncoding.Decode(encoded));
    }

    [TestMethod]
    public void Encode_NeverProducesRawWindowsReservedOrControlCharacters()
    {
        string handle = "weird:chars?*|\"<>/\\%";
        string encoded = HandleEncoding.Encode(handle);

        char[] reserved = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
        foreach (char c in reserved)
            Assert.DoesNotContain(c, encoded, $"encoded output must never contain the raw reserved character '{c}'");

        foreach (char c in encoded)
            Assert.IsFalse(char.IsControl(c), "encoded output must never contain a raw control character");
    }

    [TestMethod]
    public void Encode_EscapesItsOwnEscapeCharacter_SoDistinctHandlesNeverCollide()
    {
        // If '%' weren't itself escaped, the raw handle "a%2Fb" and the
        // handle "a/b" (whose '/' gets escaped to "%2F") would both encode
        // to the literal string "a%2Fb" -- a genuine collision merging two
        // callers' sidecar entries. Escaping '%' makes the encoding
        // injective.
        string[] handles = ["a/b", "a%2Fb", "a\\b", "50%off", "50%25off"];
        string[] encoded = [.. handles.Select(HandleEncoding.Encode)];
        CollectionAssert.AllItemsAreUnique(encoded);
    }
}
