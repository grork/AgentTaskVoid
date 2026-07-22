using System.Security.Cryptography;
using System.Text;
using Codevoid.AgentTaskVoid.IconRendering;
using Codevoid.AgentTaskVoid.Icons;

namespace Codevoid.AgentTaskVoid.LogicTests.Icons;

/// <summary>
/// ERGO-34 (phase 22) Part 3: the combined default-icon pool's integrity
/// (AC7) and the deterministic SHA-256 pick recipe (AC5). The recipe is
/// pinned exactly (<c>plan/phase-22-create-anchored-card-defaults.md</c>), so
/// this file re-derives the expected pick INDEPENDENTLY (its own SHA-256 over
/// the documented normalization), rather than peeking at
/// <see cref="IconTokens.TryPickRepoIcon"/>'s internals, and asserts
/// agreement.
/// </summary>
[TestClass]
public sealed class IconTokensCombinedPoolTests
{
    // ==== AC7: pool integrity ===================================================

    [TestMethod]
    public void CombinedPool_HasAtLeast100Entries()
    {
        Assert.IsGreaterThanOrEqualTo(100, IconTokens.CombinedDefaultPool.Count);
    }

    [TestMethod]
    public void CombinedPool_NoDuplicates()
    {
        var seen = new HashSet<(IconTokenKind Kind, string Value, int Codepoint)>();
        foreach (var token in IconTokens.CombinedDefaultPool)
        {
            var key = (token.Kind, token.Value, token.Codepoint);
            Assert.IsTrue(seen.Add(key), $"duplicate pool entry: {IconTokens.Describe(token)}");
        }
    }

    [TestMethod]
    public void CombinedPool_EveryEmojiEntry_IsASingleUnicodeScalar_RoundTripsTryParse()
    {
        foreach (int codepoint in IconTokens.CuratedEmojiCodepoints)
        {
            string text = char.ConvertFromUtf32(codepoint);
            Assert.IsTrue(text.Length is 1 or 2, $"U+{codepoint:X} is not encodable as a single UTF-16 char or surrogate pair.");

            bool ok = IconTokens.TryParse(text, out IconToken token, out string? error);
            Assert.IsTrue(ok, $"U+{codepoint:X} failed to round-trip TryParse: {error}");
            Assert.AreEqual(IconTokenKind.Emoji, token.Kind, $"U+{codepoint:X} was not parsed back as Emoji (got {token.Kind}) -- likely not a single scalar.");
            Assert.AreEqual(codepoint, token.Codepoint);
        }
    }

    [TestMethod]
    public void CombinedPool_EveryEntry_PassesGlyphProbeOnThisBuildMachine()
    {
        List<string> missing = [];
        foreach (var (name, codepoint) in IconTokens.CuratedSegoe)
        {
            if (!GlyphProbe.IsPresent(GlyphRenderer.SegoeIconFontFamily, codepoint))
                missing.Add($"Segoe:{name} (U+{codepoint:X4})");
        }
        foreach (int codepoint in IconTokens.CuratedEmojiCodepoints)
        {
            if (!GlyphProbe.IsPresent(GlyphRenderer.EmojiFontFamily, codepoint))
                missing.Add($"Emoji U+{codepoint:X}");
        }

        Assert.IsEmpty(missing, $"entries missing from this build machine's font collection (review obligation -- the 26100 curation bar -- not necessarily wrong, but worth a look): {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void CuratedEmojiCodepoints_NoOverlapWithCuratedSegoe()
    {
        var segoeCodepoints = new HashSet<int>(IconTokens.CuratedSegoe.Values).ToArray();
        foreach (int cp in IconTokens.CuratedEmojiCodepoints)
            CollectionAssert.DoesNotContain(segoeCodepoints, cp, $"U+{cp:X} appears in both CuratedSegoe and CuratedEmojiCodepoints.");
    }

    // ==== AC5: deterministic SHA-256 pick =======================================

    /// <summary>Independently re-derives the pinned recipe: <c>Path.GetFullPath</c> -&gt; trim trailing separators -&gt; <c>ToUpperInvariant</c> -&gt; SHA-256 the UTF-8 bytes -&gt; first 8 bytes big-endian -&gt; mod pool count.</summary>
    private static int ExpectedIndex(string keyPath)
    {
        string normalized = Path.GetFullPath(keyPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        ulong firstEight = 0;
        for (int i = 0; i < 8; i++)
            firstEight = (firstEight << 8) | hash[i];
        return (int)(firstEight % (ulong)IconTokens.CombinedDefaultPool.Count);
    }

    [TestMethod]
    [DataRow(@"C:\Users\dhopt\Source\AppTaskInfoCli")]
    [DataRow(@"C:\src\some-other-repo")]
    [DataRow(@"C:\work\a-third-one")]
    public void TryPickRepoIcon_MatchesIndependentlyComputedSha256Index(string keyPath)
    {
        int expectedIndex = ExpectedIndex(keyPath);
        IconToken expectedToken = IconTokens.CombinedDefaultPool[expectedIndex];

        bool ok = IconTokens.TryPickRepoIcon(keyPath, out IconToken actual);

        Assert.IsTrue(ok);
        Assert.AreEqual(expectedToken, actual);
    }

    [TestMethod]
    public void TryPickRepoIcon_TrailingSeparator_CaseInsensitive_PicksIdentically()
    {
        Assert.IsTrue(IconTokens.TryPickRepoIcon(@"C:\repo\project", out IconToken a));
        Assert.IsTrue(IconTokens.TryPickRepoIcon(@"C:\repo\project\", out IconToken b));
        Assert.IsTrue(IconTokens.TryPickRepoIcon(@"c:\REPO\PROJECT", out IconToken c));

        Assert.AreEqual(a, b, "a trailing directory separator must not change the pick.");
        Assert.AreEqual(a, c, "case must not change the pick.");
    }

    [TestMethod]
    public void TryPickRepoIcon_DistinctPaths_HitDistinctExpectedIndices()
    {
        // Not a universal guarantee (collisions are accepted and documented),
        // but these three pinned paths are chosen to land on distinct indices
        // -- proving the pick genuinely varies by key, not a constant.
        string[] paths = [@"C:\Users\dhopt\Source\AppTaskInfoCli", @"C:\src\some-other-repo", @"C:\work\a-third-one"];
        var indices = paths.Select(ExpectedIndex).ToArray();

        Assert.HasCount(indices.Length, indices.Distinct().ToArray(), "the three pinned paths were expected to hit distinct pool indices -- if this fails, swap in different pinned paths (a collision is not itself a bug).");

        foreach (string path in paths)
        {
            Assert.IsTrue(IconTokens.TryPickRepoIcon(path, out IconToken token));
            Assert.AreEqual(IconTokens.CombinedDefaultPool[ExpectedIndex(path)], token);
        }
    }

    [TestMethod]
    public void TryPickRepoIcon_RelativeForm_NormalizesToSameAbsolutePick()
    {
        string absolute = Path.GetFullPath(@"C:\repo-relative-test\project");
        // A relative path resolved from the SAME base directory as GetFullPath
        // uses (the current process cwd) must normalize to the identical
        // absolute form -- constructing the relative form FROM the absolute
        // one keeps this test independent of whatever the real process cwd is.
        string relative = Path.GetRelativePath(Environment.CurrentDirectory, absolute);

        Assert.IsTrue(IconTokens.TryPickRepoIcon(absolute, out IconToken viaAbsolute));
        Assert.IsTrue(IconTokens.TryPickRepoIcon(relative, out IconToken viaRelative));

        Assert.AreEqual(viaAbsolute, viaRelative);
    }
}
