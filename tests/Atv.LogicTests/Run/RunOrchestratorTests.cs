using Atv.Cli.Verbs;
using Atv.Config;
using Atv.Store;

namespace Atv.LogicTests.Run;

/// <summary>
/// AC2's lifecycle mapping, one level below <c>RunVerbDispatchTests</c> (no
/// <c>CommandLine</c>/<c>Dispatcher</c> plumbing -- just <c>RunOrchestrator.Execute</c>
/// over a scripted <see cref="FakeChildProcess"/>): launch -&gt; start; exit
/// 0 -&gt; done; exit N -&gt; fail AND the returned exit code is exactly N.
/// <c>RunOrchestrator</c> never sees <c>--strict</c> at all (it isn't one of
/// its parameters) -- that is itself the structural proof that the child's
/// exit code always wins regardless of it.
/// </summary>
[TestClass]
public sealed class RunOrchestratorTests
{
    private static readonly Uri IconUri = new("file:///icon.png");
    private static readonly Uri DeepLink = new("file:///deep-link");
    private static readonly Settings FastSettings = Settings.Default with
    {
        RunUpdateDebounce = TimeSpan.FromMilliseconds(10),
        RunKeepAliveInterval = TimeSpan.FromSeconds(30),
    };

    [TestMethod]
    public void Execute_ChildExitsZero_StartsThenMarksDone_ReturnsZero()
    {
        using var h = new RunTestHarness();
        var child = new FakeChildProcess();
        child.WriteStdout("step one\nstep two\n");
        child.Exit(0);

        int exitCode = RunOrchestrator.Execute(
            h.Ops, FastSettings, () => RunTestHarness.Now, sleep: _ => Thread.Sleep(1),
            "run-handle-a", "Build", IconUri, DeepLink, child,
            new MemoryStream(), new MemoryStream(), RunTestHarness.Now);

        Assert.AreEqual(0, exitCode);
        var entry = h.Ops.List().Single();
        Assert.AreEqual(AppTaskState.Completed, entry.State);
        Assert.AreEqual("Build", entry.Title);
    }

    [TestMethod]
    public void Execute_ChildExitsNonzero_MarksFail_ReturnedCodeIsExact()
    {
        using var h = new RunTestHarness();
        var child = new FakeChildProcess();
        child.WriteStderr("compile error\n");
        child.Exit(42);

        int exitCode = RunOrchestrator.Execute(
            h.Ops, FastSettings, () => RunTestHarness.Now, sleep: _ => Thread.Sleep(1),
            "run-handle-b", "Build", IconUri, DeepLink, child,
            new MemoryStream(), new MemoryStream(), RunTestHarness.Now);

        Assert.AreEqual(42, exitCode);
        Assert.AreEqual(AppTaskState.Error, h.Ops.List().Single().State);
    }

    [TestMethod]
    public void Execute_FinishedCardLingers_NeverRemoved()
    {
        using var h = new RunTestHarness();
        var child = new FakeChildProcess();
        child.Exit(0);

        RunOrchestrator.Execute(
            h.Ops, FastSettings, () => RunTestHarness.Now, sleep: _ => Thread.Sleep(1),
            "run-handle-c", "Build", IconUri, DeepLink, child,
            new MemoryStream(), new MemoryStream(), RunTestHarness.Now);

        Assert.HasCount(1, h.Ops.List()); // still present as a completed card -- run never calls Remove.
    }

    [TestMethod]
    public void Execute_LastLinesBeforeExit_SurviveIntoTheFinishedCard()
    {
        using var h = new RunTestHarness();
        var child = new FakeChildProcess();
        // Written and immediately exited -- proves FlushFinal catches lines
        // that never got a chance to hit a regular debounce tick.
        child.WriteStdout("only line, no tick in between\n");
        child.Exit(0);

        RunOrchestrator.Execute(
            h.Ops, FastSettings, () => RunTestHarness.Now, sleep: _ => Thread.Sleep(1),
            "run-handle-d", "Build", IconUri, DeepLink, child,
            new MemoryStream(), new MemoryStream(), RunTestHarness.Now);

        Assert.AreEqual("only line, no tick in between", h.Ops.List().Single().ExecutingStep);
    }
}
