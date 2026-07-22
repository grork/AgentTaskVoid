using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.LogicTests.Cli;

namespace Codevoid.AgentTaskVoid.LogicTests.Run;

/// <summary>
/// AC10, Part 1 item 6's <c>run</c> adoption of the engine path at the full
/// CLI-dispatch level (<see cref="DispatcherHarness"/>, same rig
/// <c>RunVerbDispatchTests</c> uses): <c>run --icon</c> keeps that icon
/// through the terminal <c>ready</c>/<c>broken</c> transition; <c>run</c>
/// without <c>--icon</c> gets the repo-hash default for its cwd; the card's
/// deep-link is the anchor (process cwd) at create and survives the terminal
/// update; exit-code passthrough is unaffected (already exhaustively covered
/// by <c>RunVerbDispatchTests</c>/<c>RunOrchestratorTests</c>, unmodified and
/// still green).
/// </summary>
[TestClass]
public sealed class RunIconDeepLinkDefaultTests
{
    [TestMethod]
    public void Run_ExplicitIcon_SurvivesTheTerminalReadyTransition_FileBytesAndIconUriStable()
    {
        using var h = new DispatcherHarness();
        h.ScriptChild = child =>
        {
            child.WriteStdout("building...\n");
            child.Exit(0);
        };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "run", "--title", "T", "--icon", "Heart", "--", "dummy-cmd");

        Assert.AreEqual(0, exit);
        var view = h.Store.FindAll().Single();
        byte[] actual = File.ReadAllBytes(view.IconUri.LocalPath);
        byte[] heartRef = File.ReadAllBytes(h.Icons.Place("reference-heart", IconToken.Segoe(IconTokens.CuratedSegoe["Heart"])).LocalPath);
        CollectionAssert.AreEqual(heartRef, actual, "run --icon Heart must still be Heart after the terminal ready transition -- never stomped back to Robot/repo-hash.");
    }

    [TestMethod]
    public void Run_ExplicitIcon_SurvivesTheTerminalBrokenTransition_OnNonzeroExit()
    {
        using var h = new DispatcherHarness();
        h.ScriptChild = child => child.Exit(1);
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "run", "--title", "T", "--icon", "Bug", "--", "dummy-cmd");

        Assert.AreEqual(1, exit, "the child's exit code always wins.");
        var view = h.Store.FindAll().Single();
        byte[] actual = File.ReadAllBytes(view.IconUri.LocalPath);
        byte[] bugRef = File.ReadAllBytes(h.Icons.Place("reference-bug", IconToken.Segoe(IconTokens.CuratedSegoe["Bug"])).LocalPath);
        CollectionAssert.AreEqual(bugRef, actual, "run --icon Bug must survive the terminal broken transition too.");
    }

    [TestMethod]
    public void Run_NoIconFlag_GetsTheRepoHashDefaultForItsCwd()
    {
        string anchor = Path.Combine(Path.GetTempPath(), "atv-tests", "run-repo-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(anchor);
        try
        {
            using var h = new DispatcherHarness
            {
                DiscoverRepo = () => new RepoDiscoveryResult(anchor, AnchorSource.CwdFlag, null, anchor, RepoConfigParseStatus.NotFound,
                    new Dictionary<string, string>(), [], anchor, "repo", "main"),
            };
            h.ScriptChild = child => child.Exit(0);
            var dispatcher = h.BuildDispatcher();

            int exit = h.Run(dispatcher, "run", "--title", "T", "--", "dummy-cmd");

            Assert.AreEqual(0, exit);
            var view = h.Store.FindAll().Single();
            Assert.IsTrue(IconTokens.TryPickRepoIcon(anchor, out IconToken expectedToken));
            byte[] expected = File.ReadAllBytes(h.Icons.Place("reference-expected", expectedToken).LocalPath);
            byte[] actual = File.ReadAllBytes(view.IconUri.LocalPath);
            CollectionAssert.AreEqual(expected, actual, "run with no --icon must get the repo-hash default for its cwd, not the plain Robot default.");
        }
        finally
        {
            Directory.Delete(anchor, recursive: true);
        }
    }

    [TestMethod]
    public void Run_DeepLink_IsTheAnchorAtCreate_AndSurvivesTheTerminalUpdate()
    {
        string anchor = Path.Combine(Path.GetTempPath(), "atv-tests", "run-deep-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(anchor);
        try
        {
            using var h = new DispatcherHarness
            {
                DiscoverRepo = () => new RepoDiscoveryResult(anchor, AnchorSource.CwdFlag, null, anchor, RepoConfigParseStatus.NotFound,
                    new Dictionary<string, string>(), [], anchor, "repo", "main"),
            };
            h.ScriptChild = child =>
            {
                child.WriteStdout("line\n");
                child.Exit(0);
            };
            var dispatcher = h.BuildDispatcher();

            int exit = h.Run(dispatcher, "run", "--title", "T", "--", "dummy-cmd");

            Assert.AreEqual(0, exit);
            var view = h.Store.FindAll().Single();
            Assert.AreEqual(new Uri(Path.GetFullPath(anchor)), view.DeepLink, "run's card deep-link must be the anchor (process cwd) at create -- and it's still there after the terminal ready update, proving it survived.");
        }
        finally
        {
            Directory.Delete(anchor, recursive: true);
        }
    }

    [TestMethod]
    public void Run_ExitCodePassthrough_StillWorks_AfterTheEngineAdoption()
    {
        // Guards against the Part 1 item 6 adoption (dropping run's own
        // pre-place, threading iconToken/iconExplicit through
        // RunOrchestrator.Execute) having silently broken ERGO-27 C2's core
        // guarantee -- exhaustively covered already by RunVerbDispatchTests/
        // RunOrchestratorTests (unmodified, still green); this is a
        // belt-and-suspenders check colocated with the rest of this file.
        using var h = new DispatcherHarness();
        h.ScriptChild = child => child.Exit(7);
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "run", "--title", "T", "--icon", "Bug", "--", "dummy-cmd");

        Assert.AreEqual(7, exit);
    }
}
