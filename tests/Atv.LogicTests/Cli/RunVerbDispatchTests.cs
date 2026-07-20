using Codevoid.AgentTaskVoid.Diagnostics;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Cli;

/// <summary>
/// AC2 at the full CLI-dispatch level: <c>CommandLine.Parse</c> -&gt;
/// <c>Dispatcher.Run</c> -&gt; <c>RunVerb.Run</c>, fake-backed end to end
/// (<see cref="DispatcherHarness.ScriptChild"/> scripts the spawned
/// <c>FakeChildProcess</c> synchronously, so the whole dispatch stays on one
/// thread -- no real process). Proves: pre-launch failures go through the
/// non-disruptive posture with the usual `--strict` exit-vocabulary mapping;
/// once a child has "launched", the wrapper's own exit code is the child's,
/// UNCHANGED by `--strict` (ERGO-27 C2) -- the central correctness point of
/// this phase.
/// </summary>
[TestClass]
public sealed class RunVerbDispatchTests
{
    [TestMethod]
    public void Run_NoDoubleDash_NonStrict_ExitsZero_ButLogsAFailure()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "run", "--title", "T");

        Assert.AreEqual(0, exit);
        Assert.IsNull(h.LastSpawnedChild); // never got far enough to spawn.
    }

    [TestMethod]
    public void Run_NoDoubleDash_Strict_ExitsWithInvalidArgumentsCode()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "run", "--title", "T");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
    }

    [TestMethod]
    public void Run_NoIdentity_PreLaunchFailure_NeverSpawnsChild()
    {
        using var h = new DispatcherHarness { HasIdentity = false };
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "run", "--title", "T", "--", "dummy");

        Assert.AreEqual((int)FailureKind.IdentityNotRegistered, exit);
        Assert.IsNull(h.LastSpawnedChild);
    }

    [TestMethod]
    public void Run_ChildExitsZero_WrapperReturnsZero_CardMarkedDone()
    {
        using var h = new DispatcherHarness();
        h.ScriptChild = child =>
        {
            child.WriteStdout("building...\ndone.\n");
            child.Exit(0);
        };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "run", "--title", "My Build", "--", "dummy-cmd");

        Assert.AreEqual(0, exit);
        var entries = h.Ops.List();
        Assert.HasCount(1, entries);
        Assert.AreEqual("My Build", entries[0].Title);
        Assert.AreEqual(AppTaskState.Completed, entries[0].State);
    }

    [TestMethod]
    public void Run_ChildExitsNonzero_WrapperReturnsSameCode_CardMarkedFail_EvenNonStrict()
    {
        using var h = new DispatcherHarness();
        h.ScriptChild = child =>
        {
            child.WriteStderr("boom\n");
            child.Exit(17);
        };
        var dispatcher = h.BuildDispatcher(strict: false);

        int exit = h.Run(dispatcher, "run", "--title", "Flaky", "--", "dummy-cmd");

        Assert.AreEqual(17, exit, "the child's exit code must win even in NON-strict mode -- ERGO-27 C2 is not a --strict-only rule.");
        var entries = h.Ops.List();
        Assert.AreEqual(AppTaskState.Error, entries[0].State);
    }

    [TestMethod]
    public void Run_ChildExitsNonzero_StrictDoesNotOverrideTheChildsExitCode()
    {
        using var h = new DispatcherHarness();
        h.ScriptChild = child => child.Exit(3);
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "run", "--title", "T", "--", "dummy-cmd");

        // 3 happens to collide with FailureKind.IdentityNotRegistered's numeric
        // value -- the point of this assertion IS that it's the child's 3, not
        // a --strict FailureKind mapping that happens to also produce 3.
        Assert.AreEqual(3, exit);
    }

    [TestMethod]
    public void Run_DefaultTitle_IsTheChildCommandLine_WhenNoTitleFlagGiven()
    {
        using var h = new DispatcherHarness();
        h.ScriptChild = child => child.Exit(0);
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "run", "--", "dotnet", "build", "--configuration", "Release");

        var entries = h.Ops.List();
        Assert.AreEqual("dotnet build --configuration Release", entries[0].Title);
    }

    [TestMethod]
    public void Run_ChildArgsWithFlagLikeTokens_PassedToSpawnVerbatim()
    {
        using var h = new DispatcherHarness();
        h.ScriptChild = child => child.Exit(0);
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "run", "--title", "T", "--", "npm", "test", "--verbose");

        CollectionAssert.AreEqual(new[] { "npm", "test", "--verbose" }, h.LastSpawnArgs!.ToArray());
    }

    [TestMethod]
    public void Run_TwoInvocations_MintDifferentHandles()
    {
        using var h = new DispatcherHarness();

        h.ScriptChild = child => child.Exit(0);
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "run", "--title", "First", "--", "dummy");
        string firstHandle = h.Ops.List().Single().Handle!;

        // Clean up so `List()` below only reflects the second run's card.
        h.Ops.ClearAll(includeRecycleBin: false);

        h.ScriptChild = child => child.Exit(0);
        h.Run(dispatcher, "run", "--title", "Second", "--", "dummy");
        string secondHandle = h.Ops.List().Single().Handle!;

        Assert.AreNotEqual(firstHandle, secondHandle);
        StringAssert.Contains(firstHandle, "-run-");
        StringAssert.Contains(secondHandle, "-run-");
    }

    [TestMethod]
    public void Run_MirrorsChildBytesToStdoutAndStderrSinks()
    {
        using var h = new DispatcherHarness();
        h.ScriptChild = child =>
        {
            child.WriteStdout("out line\n");
            child.WriteStderr("err line\n");
            child.Exit(0);
        };
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "run", "--title", "T", "--", "dummy");

        string mirroredOut = System.Text.Encoding.UTF8.GetString(h.StdoutMirror.ToArray());
        string mirroredErr = System.Text.Encoding.UTF8.GetString(h.StderrMirror.ToArray());
        Assert.AreEqual("out line\n", mirroredOut);
        Assert.AreEqual("err line\n", mirroredErr);
    }
}
