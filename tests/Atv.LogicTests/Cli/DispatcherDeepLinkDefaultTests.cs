using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Diagnostics;

namespace Codevoid.AgentTaskVoid.LogicTests.Cli;

/// <summary>
/// ERGO-35 (phase 22) Part 1/Part 2 at the full CLI-dispatch level:
/// <c>Dispatcher.TryResolveDeepLink</c>'s <c>deepLinkExplicit</c> threading
/// (AC2/AC3), on top of <c>SemanticEngineDeepLinkDefaultTests</c>'s exhaustive
/// engine-level coverage.
/// </summary>
[TestClass]
public sealed class DispatcherDeepLinkDefaultTests
{
    [TestMethod]
    public void Working_NoDeepLinkFlag_UnderARealAnchor_ResolvesToTheAnchorDirectory()
    {
        string anchor = Path.Combine(Path.GetTempPath(), "atv-tests", "dispatcher-deep-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(anchor);
        try
        {
            using var h = new DispatcherHarness
            {
                DiscoverRepo = () => new RepoDiscoveryResult(anchor, AnchorSource.CwdFlag, null, anchor, RepoConfigParseStatus.NotFound,
                    new Dictionary<string, string>(), [], anchor, "repo", "main"),
            };
            var dispatcher = h.BuildDispatcher();

            int exit = h.Run(dispatcher, "working", "h1", "--goal", "g");

            Assert.AreEqual(0, exit);
            Uri expected = new(Path.GetFullPath(anchor));
            Assert.AreEqual(expected, h.Store.FindAll().Single().DeepLink);
        }
        finally
        {
            Directory.Delete(anchor, recursive: true);
        }
    }

    [TestMethod]
    public void Working_ExplicitDeepLinkFlag_AlwaysWins_RegardlessOfAnchor()
    {
        string anchor = Path.Combine(Path.GetTempPath(), "atv-tests", "dispatcher-deep-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(anchor);
        try
        {
            using var h = new DispatcherHarness
            {
                DiscoverRepo = () => new RepoDiscoveryResult(anchor, AnchorSource.CwdFlag, null, anchor, RepoConfigParseStatus.NotFound,
                    new Dictionary<string, string>(), [], anchor, "repo", "main"),
            };
            var dispatcher = h.BuildDispatcher();

            int exit = h.Run(dispatcher, "working", "h1", "--deep-link", "https://example.invalid/explicit", "--goal", "g");

            Assert.AreEqual(0, exit);
            Assert.AreEqual(new Uri("https://example.invalid/explicit"), h.Store.FindAll().Single().DeepLink);
        }
        finally
        {
            Directory.Delete(anchor, recursive: true);
        }
    }

    [TestMethod]
    public void Working_UpdatingALiveCard_AnchorDeepLinkNeverReResolved()
    {
        string anchor = Path.Combine(Path.GetTempPath(), "atv-tests", "dispatcher-deep-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(anchor);
        try
        {
            using var h = new DispatcherHarness
            {
                DiscoverRepo = () => new RepoDiscoveryResult(anchor, AnchorSource.CwdFlag, null, anchor, RepoConfigParseStatus.NotFound,
                    new Dictionary<string, string>(), [], anchor, "repo", "main"),
            };
            var dispatcher = h.BuildDispatcher();

            h.Run(dispatcher, "working", "h1", "--goal", "g1");
            Uri createTimeDeepLink = h.Store.FindAll().Single().DeepLink;

            int exit = h.Run(dispatcher, "activity", "h1", "--kind", "read", "--label", "reading");

            Assert.AreEqual(0, exit);
            Assert.AreEqual(createTimeDeepLink, h.Store.FindAll().Single().DeepLink, "an update with no --deep-link must never re-resolve (or change) the deep-link.");
        }
        finally
        {
            Directory.Delete(anchor, recursive: true);
        }
    }

    [TestMethod]
    public void Working_InvalidDeepLinkOnUpdate_StillErrors()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(strict: true);
        h.Run(h.BuildDispatcher(), "working", "h1", "--goal", "g1");

        int exit = h.Run(dispatcher, "working", "h1", "--deep-link", "not a valid uri", "--goal", "g2");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
    }

    [TestMethod]
    public void Working_NoDeepLinkFlag_NoDiscoverRepoWired_FloorsToTheAppDataUri()
    {
        using var h = new DispatcherHarness(); // DiscoverRepo left null.
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "working", "h1", "--goal", "g");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(new Uri(h.AppDataRoot), h.Store.FindAll().Single().DeepLink);
    }
}
