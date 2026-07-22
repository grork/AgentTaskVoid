using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.Semantics;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Semantics;

/// <summary>
/// ERGO-34 (phase 22) Part 3 at the <see cref="Codevoid.AgentTaskVoid.Semantics.SemanticEngine"/>
/// level: AC5's determinism guarantee through a real create/clear/recreate
/// cycle, and AC6's chain-position matrix -- the repo-hash default fires ONLY
/// when nothing explicit resolves anywhere (flag/env/repo-file/user-file each
/// suppress it), and the no-path floor still yields the plain Robot default.
/// Complements <see cref="IconTokensCombinedPoolTests"/>'s pure-function
/// pick-recipe coverage and <see cref="SemanticEngineRepoDefaultsTests"/>'s
/// existing override-precedence tests.
/// </summary>
[TestClass]
public sealed class SemanticEngineIconDefaultTests
{
    private static readonly Uri DefaultIconUri = SemanticEngineHarness.IconUri;
    private static readonly Uri Link = SemanticEngineHarness.DeepLink;
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    private static RepoDiscoveryResult FakeDiscovery(
        IReadOnlyDictionary<string, string>? allowed = null,
        string? repoRootDir = @"C:\repo-icon-default-test",
        string? repoName = "repo",
        string? anchorPath = @"C:\repo-icon-default-test")
        => new(anchorPath ?? @"C:\repo-icon-default-test", AnchorSource.CwdFlag, null, anchorPath ?? @"C:\repo-icon-default-test", RepoConfigParseStatus.NotFound,
            allowed ?? new Dictionary<string, string>(), [], repoRootDir, repoName, "main");

    // ==== AC5: determinism guarantee (clear + recreate) ==========================

    [TestMethod]
    public void ClearAndRecreate_SameRepo_PicksTheSameIcon()
    {
        var discovery = FakeDiscovery();
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        var first = h.Engine.Working("session", "T", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
        byte[] firstBytes = File.ReadAllBytes(first.View!.IconUri.LocalPath);

        h.Ops.Remove("session", Now.AddMinutes(1));
        Assert.IsEmpty(h.Store.FindAll());

        var second = h.Engine.Working("session", "T", "S", DefaultIconUri, Link, "goal", Now.AddMinutes(2), iconToken: IconTokens.Default, iconExplicit: false);
        byte[] secondBytes = File.ReadAllBytes(second.View!.IconUri.LocalPath);

        CollectionAssert.AreEqual(firstBytes, secondBytes, "clearing and recreating a card in the same repo must yield the identical icon -- the card never 'loses its identity'.");
    }

    // ==== AC6: chain position -- each layer suppresses the repo-hash default =====

    [TestMethod]
    public void ExplicitFlagIcon_SuppressesTheRepoHashDefault()
    {
        var discovery = FakeDiscovery();
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);
        IconToken explicitToken = IconToken.Segoe(IconTokens.CuratedSegoe["Heart"]);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, Link, "goal", Now, iconToken: explicitToken, iconExplicit: true);

        byte[] actual = File.ReadAllBytes(outcome.View!.IconUri.LocalPath);
        byte[] heartRef = File.ReadAllBytes(h.Icons!.Place("reference-heart", explicitToken).LocalPath);
        CollectionAssert.AreEqual(heartRef, actual, "an explicit --icon must win outright -- the repo-hash default must never even be consulted.");
    }

    [TestMethod]
    public void EnvIcon_SuppressesTheRepoHashDefault()
    {
        var discovery = FakeDiscovery();
        var env = new Dictionary<string, string> { [RepoSettings.KeyIcon] = "Bug" };
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery, presentationEnv: env);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        byte[] actual = File.ReadAllBytes(outcome.View!.IconUri.LocalPath);
        byte[] bugRef = File.ReadAllBytes(h.Icons!.Place("reference-bug", IconToken.Segoe(IconTokens.CuratedSegoe["Bug"])).LocalPath);
        CollectionAssert.AreEqual(bugRef, actual, "an env icon override must win -- the repo-hash default must never even be consulted.");
    }

    [TestMethod]
    public void RepoFileIcon_SuppressesTheRepoHashDefault()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyIcon] = "Warning" });
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        byte[] actual = File.ReadAllBytes(outcome.View!.IconUri.LocalPath);
        byte[] warningRef = File.ReadAllBytes(h.Icons!.Place("reference-warning", IconToken.Segoe(IconTokens.CuratedSegoe["Warning"])).LocalPath);
        CollectionAssert.AreEqual(warningRef, actual, "a repo-file icon override must win -- the repo-hash default must never even be consulted.");
    }

    [TestMethod]
    public void UserFileIcon_SuppressesTheRepoHashDefault()
    {
        var discovery = FakeDiscovery();
        var userFile = new Dictionary<string, string> { [RepoSettings.KeyIcon] = "Globe" };
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery, presentationUserFile: userFile);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        byte[] actual = File.ReadAllBytes(outcome.View!.IconUri.LocalPath);
        byte[] globeRef = File.ReadAllBytes(h.Icons!.Place("reference-globe", IconToken.Segoe(IconTokens.CuratedSegoe["Globe"])).LocalPath);
        CollectionAssert.AreEqual(globeRef, actual, "a user-file icon override must win -- the repo-hash default must never even be consulted.");
    }

    [TestMethod]
    public void NothingExplicitResolves_TheRepoHashDefaultActuallyFires_NotRobot()
    {
        var discovery = FakeDiscovery();
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        Assert.IsTrue(IconTokens.TryPickRepoIcon(discovery.RepoRootDir!, out IconToken expectedToken));
        byte[] actual = File.ReadAllBytes(outcome.View!.IconUri.LocalPath);
        byte[] expected = File.ReadAllBytes(h.Icons!.Place("reference-expected", expectedToken).LocalPath);
        byte[] robotRef = File.ReadAllBytes(h.Icons.Place("reference-robot", IconTokens.Default).LocalPath);

        CollectionAssert.AreEqual(expected, actual, "with nothing explicit anywhere, the repo-hash pick must actually apply.");
        if (!expectedToken.Equals(IconTokens.Default))
            CollectionAssert.AreNotEqual(robotRef, actual, "sanity: the pick for this key must not coincidentally be Robot (would make this test vacuous).");
    }

    // ==== AC6: no-path floor yields Robot ========================================

    [TestMethod]
    public void NoPathResolves_FloorsToPlainRobotDefault()
    {
        // Both AnchorPath and RepoRootDir empty/absent -- the theoretical
        // "literally no path resolves" case (RepoSettings.Discover never
        // actually produces an empty AnchorPath in production; this is the
        // engine's own defensive floor).
        var discovery = new RepoDiscoveryResult("", AnchorSource.ProcessCwd, null, "", RepoConfigParseStatus.NotFound,
            new Dictionary<string, string>(), [], null, null, null);
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        byte[] actual = File.ReadAllBytes(outcome.View!.IconUri.LocalPath);
        byte[] robotRef = File.ReadAllBytes(h.Icons!.Place("reference-robot", IconTokens.Default).LocalPath);
        CollectionAssert.AreEqual(robotRef, actual, "no path to key off -- the pick is skipped and the plain Robot default stands.");
    }

    // ==== AC6: grouped repo's owner card also gets the repo-hash icon ============

    [TestMethod]
    public void GroupedRepo_OwnerCard_GetsTheRepoHashIcon_NotRobot()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyGroup] = "true" });
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        var owner = h.Engine.Working("session-1", "T1", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        Assert.IsTrue(IconTokens.TryPickRepoIcon(discovery.RepoRootDir!, out IconToken expectedToken));
        byte[] actual = File.ReadAllBytes(owner.View!.IconUri.LocalPath);
        byte[] expected = File.ReadAllBytes(h.Icons!.Place("reference-expected", expectedToken).LocalPath);
        CollectionAssert.AreEqual(expected, actual, "the grouping owner card must be placed with the repo-hash icon, not the plain Robot default -- the two features key on the same repo root and compose.");
    }
}
