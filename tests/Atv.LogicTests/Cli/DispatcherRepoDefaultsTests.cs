using Atv.Config;

namespace Atv.LogicTests.Cli;

/// <summary>
/// ERGO-30 (phase 17) at the full CLI-dispatch level: proves the actual
/// wiring in <c>Dispatcher</c>'s 7 upserting verb bodies (icon-explicitness
/// threading into <c>SemanticEngine</c>) and <c>doctor</c>'s repo-discovery
/// surfacing, on top of the exhaustive engine-level coverage in
/// <c>Atv.LogicTests.Semantics.SemanticEngineRepoDefaultsTests</c>.
/// </summary>
[TestClass]
public sealed class DispatcherRepoDefaultsTests
{
    [TestMethod]
    public void Working_NoExplicitTitle_PicksUpRepoTitleTemplate_OnCreate()
    {
        using var h = new DispatcherHarness
        {
            DiscoverRepo = () => new RepoDiscoveryResult(
                @"C:\repo", AnchorSource.CwdFlag, @"C:\repo\.atv.json", @"C:\repo", RepoConfigParseStatus.Ok,
                new Dictionary<string, string> { [RepoSettings.KeyTitleTemplate] = "{repo}" }, [], @"C:\repo", "myrepo", "main"),
        };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "working", "h1", "--goal", "g");

        Assert.AreEqual(0, exit);
        Assert.AreEqual("myrepo", h.Store.FindAll().Single().Title);
    }

    [TestMethod]
    public void Working_ExplicitTitle_NeverOverriddenByRepoTemplate()
    {
        using var h = new DispatcherHarness
        {
            DiscoverRepo = () => new RepoDiscoveryResult(
                @"C:\repo", AnchorSource.CwdFlag, @"C:\repo\.atv.json", @"C:\repo", RepoConfigParseStatus.Ok,
                new Dictionary<string, string> { [RepoSettings.KeyTitleTemplate] = "{repo}" }, [], @"C:\repo", "myrepo", "main"),
        };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "working", "h1", "--title", "Caller Title", "--goal", "g");

        Assert.AreEqual(0, exit);
        Assert.AreEqual("Caller Title", h.Store.FindAll().Single().Title);
    }

    [TestMethod]
    public void Working_UpdatingALiveCard_RepoTitleTemplateNeverConsulted()
    {
        using var h = new DispatcherHarness
        {
            DiscoverRepo = () => new RepoDiscoveryResult(
                @"C:\repo", AnchorSource.CwdFlag, @"C:\repo\.atv.json", @"C:\repo", RepoConfigParseStatus.Ok,
                new Dictionary<string, string> { [RepoSettings.KeyTitleTemplate] = "{repo}" }, [], @"C:\repo", "myrepo", "main"),
        };
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "working", "h1", "--goal", "g1");
        Assert.AreEqual("myrepo", h.Store.FindAll().Single().Title);

        // A second call against the SAME handle -- an update, no --title.
        int exit = h.Run(dispatcher, "activity", "h1", "--kind", "read", "--label", "reading");

        Assert.AreEqual(0, exit);
        Assert.AreEqual("myrepo", h.Store.FindAll().Single().Title, "an update must never re-consult (or change) the repo title.");
    }

    [TestMethod]
    public void Doctor_SurfacesRepoAnchorAndConfigStatus_HumanOutput()
    {
        using var h = new DispatcherHarness
        {
            DiscoverRepo = () => new RepoDiscoveryResult(
                @"C:\repo", AnchorSource.CwdFlag, null, @"C:\repo", RepoConfigParseStatus.NotFound,
                new Dictionary<string, string>(), [], @"C:\repo", "myrepo", "main"),
        };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "doctor");

        Assert.AreEqual(0, exit);
        string output = h.Stdout.ToString();
        StringAssert.Contains(output, "repo config anchor:");
        StringAssert.Contains(output, "--cwd");
        StringAssert.Contains(output, "none, searched up to");
    }
}
