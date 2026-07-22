using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.LogicTests.Persistence;
using Codevoid.AgentTaskVoid.Semantics;

namespace Codevoid.AgentTaskVoid.LogicTests.Semantics;

/// <summary>
/// ERGO-35 (phase 22) Part 2 at the <see cref="Codevoid.AgentTaskVoid.Semantics.SemanticEngine"/>
/// level: AC3's anchor-directory deep-link default (including the monorepo
/// case, where the deep-link tracks the ANCHOR while the icon key tracks the
/// REPO ROOT) and AC4's floors (nonexistent anchor dir, unrepresentable path,
/// no anchor at all) -- every case always resolving to a valid absolute
/// <c>file:</c> URI (FAIL-1).
/// </summary>
[TestClass]
public sealed class SemanticEngineDeepLinkDefaultTests
{
    private static readonly Uri DefaultIconUri = SemanticEngineHarness.IconUri;
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    private static RepoDiscoveryResult FakeDiscoveryAt(
        string anchorPath, string? repoRootDir = null, string? repoName = null, string? branch = null)
        => new(anchorPath, AnchorSource.CwdFlag, null, anchorPath, RepoConfigParseStatus.NotFound,
            new Dictionary<string, string>(), [], repoRootDir, repoName, branch);

    // ==== AC3: anchor deep-link default ==========================================

    [TestMethod]
    public void Create_NoDeepLink_UnderTempDirAnchor_ResolvesToTheAnchorDirectoryFileUri()
    {
        using var anchor = new TempDirectory();
        Directory.CreateDirectory(anchor.Path);
        var discovery = FakeDiscoveryAt(anchor.Path, repoRootDir: anchor.Path, repoName: "repo", branch: "main");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery, deepLinkFloor: SemanticEngineHarness.AppDataDeepLinkFloor);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, SemanticEngineHarness.DeepLink, "goal", Now,
            iconToken: IconTokens.Default, iconExplicit: false, deepLinkExplicit: false);

        Assert.IsTrue(outcome.Success);
        Uri expected = new(Path.GetFullPath(anchor.Path));
        Assert.AreEqual(expected, outcome.View!.DeepLink);
    }

    [TestMethod]
    public void Create_ExplicitDeepLink_AlwaysWins_RegardlessOfAnchor()
    {
        using var anchor = new TempDirectory();
        Directory.CreateDirectory(anchor.Path);
        var discovery = FakeDiscoveryAt(anchor.Path, repoRootDir: anchor.Path, repoName: "repo", branch: "main");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        Uri explicitLink = new("https://example.invalid/explicit-caller-link");
        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, explicitLink, "goal", Now,
            iconToken: IconTokens.Default, iconExplicit: false, deepLinkExplicit: true);

        Assert.AreEqual(explicitLink, outcome.View!.DeepLink);
    }

    [TestMethod]
    public void Create_MonorepoCase_DeepLinkTracksAnchor_IconKeyTracksRepoRoot()
    {
        using var repoRoot = new TempDirectory();
        string subprojectPath = Path.Combine(repoRoot.Path, "packages", "web");
        Directory.CreateDirectory(subprojectPath);
        var discovery = FakeDiscoveryAt(subprojectPath, repoRootDir: repoRoot.Path, repoName: "monorepo", branch: "main");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery, deepLinkFloor: SemanticEngineHarness.AppDataDeepLinkFloor);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, SemanticEngineHarness.DeepLink, "goal", Now,
            iconToken: IconTokens.Default, iconExplicit: false, deepLinkExplicit: false);

        Assert.IsTrue(outcome.Success);
        Uri expectedDeepLink = new(Path.GetFullPath(subprojectPath));
        Assert.AreEqual(expectedDeepLink, outcome.View!.DeepLink, "the deep-link must track the ANCHOR (subproject), not the repo root.");

        Assert.IsTrue(IconTokens.TryPickRepoIcon(repoRoot.Path, out IconToken expectedIconToken));
        byte[] expectedIconBytes = File.ReadAllBytes(h.Icons!.Place("reference", expectedIconToken).LocalPath);
        byte[] actualIconBytes = File.ReadAllBytes(outcome.View.IconUri.LocalPath);
        CollectionAssert.AreEqual(expectedIconBytes, actualIconBytes, "the icon key must track the REPO ROOT, not the anchor -- diverging deliberately from the deep-link.");
    }

    // ==== Part 1: updates never re-resolve the anchor deep-link -----------------

    [TestMethod]
    public void UpdatingALiveCard_DeepLinkNeverReResolved_EvenIfAnchorChanges()
    {
        using var anchorA = new TempDirectory();
        using var anchorB = new TempDirectory();
        Directory.CreateDirectory(anchorA.Path);
        Directory.CreateDirectory(anchorB.Path);
        var discoveryA = FakeDiscoveryAt(anchorA.Path, repoRootDir: anchorA.Path, repoName: "repo", branch: "main");
        var discoveryB = FakeDiscoveryAt(anchorB.Path, repoRootDir: anchorB.Path, repoName: "repo", branch: "main");
        RepoDiscoveryResult current = discoveryA;
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => current, deepLinkFloor: SemanticEngineHarness.AppDataDeepLinkFloor);

        var created = h.Engine.Working("session", "T", "S", DefaultIconUri, SemanticEngineHarness.DeepLink, "goal", Now,
            iconToken: IconTokens.Default, iconExplicit: false, deepLinkExplicit: false);
        Uri firstDeepLink = created.View!.DeepLink;

        current = discoveryB; // simulate a --cwd change (e.g. a translator invoked from elsewhere) between calls.
        var updated = h.Engine.Activity("session", "T", "S", DefaultIconUri, SemanticEngineHarness.DeepLink, ActivityKind.Read, "x", null, null, Now.AddMinutes(1),
            iconToken: IconTokens.Default, iconExplicit: false, deepLinkExplicit: false);

        Assert.AreEqual(firstDeepLink, updated.View!.DeepLink, "an update must never re-resolve the anchor deep-link, even if the anchor itself would now resolve differently.");
    }

    // ==== AC4: floors ============================================================

    [TestMethod]
    public void Floor_AnchorDirectoryDoesNotExistOnDisk()
    {
        string nonexistent = Path.Combine(Path.GetTempPath(), "atv-tests", "does-not-exist-" + Guid.NewGuid().ToString("N"));
        var discovery = FakeDiscoveryAt(nonexistent, repoRootDir: nonexistent, repoName: "repo", branch: "main");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery, deepLinkFloor: SemanticEngineHarness.AppDataDeepLinkFloor);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, SemanticEngineHarness.DeepLink, "goal", Now,
            iconToken: IconTokens.Default, iconExplicit: false, deepLinkExplicit: false);

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(SemanticEngineHarness.AppDataDeepLinkFloor, outcome.View!.DeepLink);
    }

    [TestMethod]
    public void Floor_UnrepresentablePath_EmbeddedNulCharacter()
    {
        string malformed = "C:\\repo\0invalid";
        var discovery = FakeDiscoveryAt(malformed, repoRootDir: malformed, repoName: "repo", branch: "main");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery, deepLinkFloor: SemanticEngineHarness.AppDataDeepLinkFloor);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, SemanticEngineHarness.DeepLink, "goal", Now,
            iconToken: IconTokens.Default, iconExplicit: false, deepLinkExplicit: false);

        Assert.IsTrue(outcome.Success, "a malformed anchor path must never throw or fail the create (FAIL-1).");
        Assert.AreEqual(SemanticEngineHarness.AppDataDeepLinkFloor, outcome.View!.DeepLink);
    }

    [TestMethod]
    public void Floor_NoDiscoverRepoWiredAtAll_NoAnchorToResolve()
    {
        using var h = new SemanticEngineHarness(withIcons: true, deepLinkFloor: SemanticEngineHarness.AppDataDeepLinkFloor);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, SemanticEngineHarness.DeepLink, "goal", Now,
            iconToken: IconTokens.Default, iconExplicit: false, deepLinkExplicit: false);

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(SemanticEngineHarness.AppDataDeepLinkFloor, outcome.View!.DeepLink);
    }

    [TestMethod]
    public void EveryFloorCase_ResultIsAValidAbsoluteFileUri()
    {
        using var h = new SemanticEngineHarness(withIcons: true, deepLinkFloor: SemanticEngineHarness.AppDataDeepLinkFloor);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, SemanticEngineHarness.DeepLink, "goal", Now,
            iconToken: IconTokens.Default, iconExplicit: false, deepLinkExplicit: false);

        Assert.IsTrue(outcome.View!.DeepLink.IsAbsoluteUri);
    }

    [TestMethod]
    public void DeepLinkFloorNotWired_FallsBackToTheCallersOwnDeepLinkArgument()
    {
        // Part 1 item 7's compat pin: when the dedicated engine-level floor
        // isn't wired (most test harnesses), the caller's own passed deepLink
        // argument -- in production/DispatcherHarness, always the app-data URI
        // already -- serves as the graceful fallback floor.
        string nonexistent = Path.Combine(Path.GetTempPath(), "atv-tests", "does-not-exist-" + Guid.NewGuid().ToString("N"));
        var discovery = FakeDiscoveryAt(nonexistent, repoRootDir: nonexistent, repoName: "repo", branch: "main");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery); // no deepLinkFloor wired

        Uri callerArg = new("https://example.invalid/caller-supplied-fallback");
        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, callerArg, "goal", Now,
            iconToken: IconTokens.Default, iconExplicit: false, deepLinkExplicit: false);

        Assert.AreEqual(callerArg, outcome.View!.DeepLink);
    }
}
