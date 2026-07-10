using Atv.Diagnostics;
using Atv.Store;

namespace Atv.LogicTests.Cli;

/// <summary>
/// AC1's `step`/`state`/`attention`/`done`/`fail` coverage: ERGO-19's
/// no-sweep-on-step, C7's running|paused-only restriction, required-args
/// enforcement, `--json` shapes, and `--unsafe` bypassing the ERGO-10
/// validator end to end through the Dispatcher.
/// </summary>
[TestClass]
public sealed class DispatcherUpdateVerbsTests
{
    // ---- step ---------------------------------------------------------------

    [TestMethod]
    public void Step_AdvancesExecutingStep()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "start", "h1");

        int exit = h.Run(dispatcher, "step", "h1", "Working on it");

        Assert.AreEqual(0, exit);
        var view = h.Store.FindAll().Single();
        Assert.AreEqual("Working on it", view.ExecutingStep);
    }

    [TestMethod]
    public void Step_NeverTriggersTheHiddenSweep()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "start", "h1");
        h.Run(dispatcher, "start", "h2");
        var h2Id = h.Sidecar.Read("h2")!.Id;
        h.Store.SetHiddenByUser(h2Id, true);

        h.Run(dispatcher, "step", "h1", "keep going");

        Assert.IsNotNull(h.Store.Find(h2Id), "ERGO-19: update-class verbs (step) never sweep -- h2 must survive.");
        Assert.IsNotNull(h.Sidecar.Read("h2"));
    }

    [TestMethod]
    public void Step_MissingMessage_NonStrictSilentZero_StrictExitFour()
    {
        using var h = new DispatcherHarness();
        h.Run(h.BuildDispatcher(), "start", "h1");

        int nonStrict = h.Run(h.BuildDispatcher(), "step", "h1");
        Assert.AreEqual(0, nonStrict);

        int strict = h.Run(h.BuildDispatcher(strict: true), "step", "h1");
        Assert.AreEqual((int)FailureKind.InvalidArguments, strict);
    }

    [TestMethod]
    public void Step_UnknownHandle_NonStrictSilentZero_WithLogEntry()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "step", "never-seen", "msg");

        Assert.AreEqual(0, exit);
        Assert.HasCount(1, h.Log.ReadAll());
    }

    // ---- state (C7) -----------------------------------------------------------

    [TestMethod]
    [DataRow("running")]
    [DataRow("paused")]
    [DataRow("RUNNING")]
    public void State_RunningOrPaused_CaseInsensitive_Succeeds(string arg)
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "start", "h1");

        int exit = h.Run(dispatcher, "state", "h1", arg);

        Assert.AreEqual(0, exit);
    }

    [TestMethod]
    [DataRow("done")]
    [DataRow("completed")]
    [DataRow("failed")]
    [DataRow("banana")]
    public void State_AnythingElse_IsInvalidArguments_C7(string arg)
    {
        using var h = new DispatcherHarness();
        h.Run(h.BuildDispatcher(), "start", "h1");

        int strict = h.Run(h.BuildDispatcher(strict: true), "state", "h1", arg);

        Assert.AreEqual((int)FailureKind.InvalidArguments, strict);
    }

    [TestMethod]
    public void State_MissingArg_NonStrictSilentZero()
    {
        using var h = new DispatcherHarness();
        h.Run(h.BuildDispatcher(), "start", "h1");

        int exit = h.Run(h.BuildDispatcher(), "state", "h1");

        Assert.AreEqual(0, exit);
    }

    // ---- attention --------------------------------------------------------------

    [TestMethod]
    public void Attention_SetsQuestion_ReachesNeedsAttention()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "start", "h1");

        int exit = h.Run(dispatcher, "attention", "h1", "Continue?");

        Assert.AreEqual(0, exit);
        var view = h.Store.FindAll().Single();
        Assert.AreEqual(AppTaskState.NeedsAttention, view.State);
    }

    [TestMethod]
    public void Attention_Json_SuccessShape()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(json: true);
        h.Run(dispatcher, "start", "h1");
        h.Stdout.GetStringBuilder().Clear();

        h.Run(dispatcher, "attention", "h1", "Continue?");

        var stdout = h.Stdout.ToString();
        StringAssert.Contains(stdout, "\"ok\":true");
        StringAssert.Contains(stdout, "\"reason\"");
    }

    // ---- done / fail --------------------------------------------------------------

    [TestMethod]
    public void Done_Bare_ReachesCompleted()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "start", "h1");

        int exit = h.Run(dispatcher, "done", "h1");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(AppTaskState.Completed, h.Store.FindAll().Single().State);
    }

    [TestMethod]
    public void Done_WithSummary_ReachesCompleted()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "start", "h1");

        int exit = h.Run(dispatcher, "done", "h1", "--summary", "All finished.");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(AppTaskState.Completed, h.Store.FindAll().Single().State);
    }

    [TestMethod]
    public void Fail_Bare_ReachesError()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "start", "h1");

        int exit = h.Run(dispatcher, "fail", "h1");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(AppTaskState.Error, h.Store.FindAll().Single().State);
    }

    [TestMethod]
    public void Fail_WithSummary_ReachesError()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "start", "h1");

        int exit = h.Run(dispatcher, "fail", "h1", "--summary", "boom");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(AppTaskState.Error, h.Store.FindAll().Single().State);
    }

    [TestMethod]
    public void Done_MissingHandle_Strict_ReturnsExitFour()
    {
        using var h = new DispatcherHarness();
        int exit = h.Run(h.BuildDispatcher(strict: true), "done");
        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
    }

    // ---- --unsafe pass-through (ERGO-10) -----------------------------------------

    [TestMethod]
    public void Step_OnNeedsAttentionCard_RefusedWithoutUnsafe_AcceptedWithUnsafe()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "start", "h1");
        h.Run(dispatcher, "attention", "h1", "Continue?");

        // Refused: SequenceOfSteps x NeedsAttention x no-question is outside the safe set.
        int refusedExit = h.Run(dispatcher, "step", "h1", "next");
        Assert.AreEqual(0, refusedExit, "non-strict: refusal is still silent zero.");
        var afterRefused = h.Store.FindAll().Single();
        Assert.AreEqual(AppTaskState.NeedsAttention, afterRefused.State, "refused write must not have applied.");
        Assert.AreNotEqual("next", afterRefused.ExecutingStep, "refused write must not have changed the executing step.");

        // With --unsafe: the same combination is bypassed and written anyway.
        int unsafeExit = h.Run(dispatcher, "step", "h1", "next", "--unsafe");
        Assert.AreEqual(0, unsafeExit);
        var afterUnsafe = h.Store.FindAll().Single();
        Assert.AreEqual("next", afterUnsafe.ExecutingStep, "--unsafe must have let the write through.");
    }

    [TestMethod]
    public void Step_OnNeedsAttentionCard_RefusedWithoutUnsafe_Strict_ReturnsExitFour()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(strict: true);
        h.Run(dispatcher, "start", "h1");
        h.Run(dispatcher, "attention", "h1", "Continue?");

        int exit = h.Run(dispatcher, "step", "h1", "next");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
    }
}
