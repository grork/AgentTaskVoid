using Codevoid.AgentTaskVoid.Diagnostics;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Cli;

/// <summary>
/// AC1/AC4/AC7 at the full CLI-dispatch level: <c>CommandLine.Parse</c> -&gt;
/// <c>Dispatcher.Run</c> -&gt; <c>SemanticEngine</c>, fake-backed end to end.
/// Covers what only exists at THIS layer (argument-shape validation for the
/// closed vocabularies, the `-` stdin convention's real wiring through
/// <see cref="DispatcherHarness.Stdin"/>, and AC7's "the parser rejects the
/// six v1 lifecycle verbs") -- the engine's own claim-semantics matrix lives
/// in <c>Codevoid.AgentTaskVoid.LogicTests.Semantics</c>.
/// </summary>
[TestClass]
public sealed class DispatcherSemanticVerbsTests
{
    // ---- AC7: the six v1 verbs are rejected -----------------------------------------

    [TestMethod]
    [DataRow("start")]
    [DataRow("step")]
    [DataRow("state")]
    [DataRow("attention")]
    [DataRow("done")]
    [DataRow("fail")]
    public void RetiredV1Verb_NonStrict_SilentZero_LogsUnknownVerb(string verb)
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, verb, "h1");

        Assert.AreEqual(0, exit);
        Assert.HasCount(1, h.LogEntriesExcludingTrace());
        Assert.IsEmpty(h.Store.FindAll(), "a retired v1 verb token must never reach the engine as a valid claim.");
    }

    [TestMethod]
    [DataRow("start")]
    [DataRow("step")]
    [DataRow("state")]
    [DataRow("attention")]
    [DataRow("done")]
    [DataRow("fail")]
    public void RetiredV1Verb_Strict_ReturnsInvalidArgumentsExitCode(string verb)
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, verb, "h1");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit, $"'{verb}' must be an unknown-verb failure, not routed to any live behavior.");
    }

    // ---- working ----------------------------------------------------------------------

    [TestMethod]
    public void Working_CreatesACard_UpsertsIdentityFlags()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "working", "h1", "--title", "My Title", "--subtitle", "My Sub");

        Assert.AreEqual(0, exit);
        var view = h.Store.FindAll().Single();
        Assert.AreEqual("My Title", view.Title);
        Assert.AreEqual("My Sub", view.Subtitle);
        Assert.AreEqual(AppTaskState.Running, view.State);
    }

    [TestMethod]
    public void Working_GoalFlag_LiteralArgvValue_SetsExecutingStep()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "working", "h1", "--goal", "Fix the login bug");

        Assert.AreEqual("Fix the login bug", h.Store.FindAll().Single().ExecutingStep);
    }

    [TestMethod]
    public void Working_MissingHandle_NonStrictSilentZero_StrictExitFour()
    {
        using var h = new DispatcherHarness();

        Assert.AreEqual(0, h.Run(h.BuildDispatcher(), "working"));
        Assert.AreEqual((int)FailureKind.InvalidArguments, h.Run(h.BuildDispatcher(strict: true), "working"));
    }

    // ---- activity ----------------------------------------------------------------------

    [TestMethod]
    public void Activity_ValidKind_AdvancesExecutingStep()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int exit = h.Run(dispatcher, "activity", "h1", "--kind", "edit", "--label", "auth.ts");

        Assert.AreEqual(0, exit);
        Assert.AreEqual("Editing auth.ts", h.Store.FindAll().Single().ExecutingStep);
    }

    [TestMethod]
    public void Activity_MissingKind_NonStrictSilentZero_StrictExitFour()
    {
        using var h = new DispatcherHarness();
        h.Run(h.BuildDispatcher(), "working", "h1");

        Assert.AreEqual(0, h.Run(h.BuildDispatcher(), "activity", "h1", "--label", "x"));
        Assert.AreEqual((int)FailureKind.InvalidArguments, h.Run(h.BuildDispatcher(strict: true), "activity", "h1", "--label", "x"));
    }

    [TestMethod]
    public void Activity_UnmappedKindToken_IsInvalidArguments()
    {
        using var h = new DispatcherHarness();

        int exit = h.Run(h.BuildDispatcher(strict: true), "activity", "h1", "--kind", "delegate");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
    }

    [TestMethod]
    public void Activity_AgentAndNameFlags_AreAccepted()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int exit = h.Run(dispatcher, "activity", "h1", "--kind", "tool", "--name", "mcp__jira__create_ticket", "--label", "Fix bug", "--agent", "worker-1");

        Assert.AreEqual(0, exit);
    }

    // ---- blocked ------------------------------------------------------------------------

    [TestMethod]
    public void Blocked_WithQuestion_ReachesNeedsAttention()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int exit = h.Run(dispatcher, "blocked", "h1", "--question", "Continue?");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(AppTaskState.NeedsAttention, h.Store.FindAll().Single().State);
    }

    [TestMethod]
    public void Blocked_MissingQuestion_NonStrictSilentZero_StrictExitFour()
    {
        using var h = new DispatcherHarness();
        h.Run(h.BuildDispatcher(), "working", "h1");

        Assert.AreEqual(0, h.Run(h.BuildDispatcher(), "blocked", "h1"));
        Assert.AreEqual((int)FailureKind.InvalidArguments, h.Run(h.BuildDispatcher(strict: true), "blocked", "h1"));
    }

    [TestMethod]
    public void Blocked_EmptyQuestion_IsInvalidArguments()
    {
        using var h = new DispatcherHarness();

        int exit = h.Run(h.BuildDispatcher(strict: true), "blocked", "h1", "--question", "   ");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
    }

    // ---- ready --------------------------------------------------------------------------

    [TestMethod]
    public void Ready_Bare_ReachesCompleted()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int exit = h.Run(dispatcher, "ready", "h1");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(AppTaskState.Completed, h.Store.FindAll().Single().State);
    }

    [TestMethod]
    public void Ready_WithSummary_ReachesCompleted()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int exit = h.Run(dispatcher, "ready", "h1", "--summary", "All finished.");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(AppTaskState.Completed, h.Store.FindAll().Single().State);
    }

    // ---- broken -------------------------------------------------------------------------

    [TestMethod]
    public void Broken_WithReason_ReachesError()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int exit = h.Run(dispatcher, "broken", "h1", "--reason", "rate-limit");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(AppTaskState.Error, h.Store.FindAll().Single().State);
    }

    [TestMethod]
    public void Broken_MissingReason_NonStrictSilentZero_StrictExitFour()
    {
        using var h = new DispatcherHarness();
        h.Run(h.BuildDispatcher(), "working", "h1");

        Assert.AreEqual(0, h.Run(h.BuildDispatcher(), "broken", "h1"));
        Assert.AreEqual((int)FailureKind.InvalidArguments, h.Run(h.BuildDispatcher(strict: true), "broken", "h1"));
    }

    [TestMethod]
    public void Broken_UnmappedReasonToken_IsInvalidArguments()
    {
        using var h = new DispatcherHarness();

        int exit = h.Run(h.BuildDispatcher(strict: true), "broken", "h1", "--reason", "network-blip");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
    }

    // ---- agent-started / agent-stopped ---------------------------------------------------

    [TestMethod]
    public void AgentStarted_AndAgentStopped_AreAccepted_NoCrash()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int startedExit = h.Run(dispatcher, "agent-started", "h1", "--agent", "a1", "--name", "worker");
        int stoppedExit = h.Run(dispatcher, "agent-stopped", "h1", "--agent", "a1");

        Assert.AreEqual(0, startedExit);
        Assert.AreEqual(0, stoppedExit);
    }

    // ---- session-ended --------------------------------------------------------------------

    [TestMethod]
    public void SessionEnded_Finished_RemovesTheCard()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int exit = h.Run(dispatcher, "session-ended", "h1", "--reason", "finished");

        Assert.AreEqual(0, exit);
        Assert.IsEmpty(h.Store.FindAll());
    }

    [TestMethod]
    public void SessionEnded_Error_ReachesError()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int exit = h.Run(dispatcher, "session-ended", "h1", "--reason", "error");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(AppTaskState.Error, h.Store.FindAll().Single().State);
    }

    [TestMethod]
    public void SessionEnded_MissingReason_NonStrictSilentZero_StrictExitFour()
    {
        using var h = new DispatcherHarness();
        h.Run(h.BuildDispatcher(), "working", "h1");

        Assert.AreEqual(0, h.Run(h.BuildDispatcher(), "session-ended", "h1"));
        Assert.AreEqual((int)FailureKind.InvalidArguments, h.Run(h.BuildDispatcher(strict: true), "session-ended", "h1"));
    }

    [TestMethod]
    public void SessionEnded_NoTitleFlagAccepted_VerbHasNoIdentityFlags()
    {
        // session-ended never accepts --title (ERGO-31 §1 intro) -- CommandLine still
        // parses it generically (flags aren't verb-scoped, phase-08 precedent) but the
        // Dispatcher body never reads it; this proves passing one causes no crash.
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int exit = h.Run(dispatcher, "session-ended", "h1", "--reason", "finished", "--title", "ignored");

        Assert.AreEqual(0, exit);
    }

    // ---- AC4: the "-" stdin convention -----------------------------------------------------

    [TestMethod]
    public void StdinSentinel_Goal_ReadsUtf8ToEof_TrimsTrailingWhitespace()
    {
        using var h = new DispatcherHarness { Stdin = new StringReader("Fix the login bug\n\n  ") };
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "working", "h1", "--goal", "-");

        Assert.AreEqual("Fix the login bug", h.Store.FindAll().Single().ExecutingStep);
    }

    [TestMethod]
    public void StdinSentinel_MultiLineUnicodeQuoteTortureString_LandsIntact()
    {
        string torture = "Fix “the” bug\n日本語のテキスト\nwith 'quotes' 🎉";
        using var h = new DispatcherHarness { Stdin = new StringReader(torture) };
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "working", "h1", "--goal", "-");

        string step = h.Store.FindAll().Single().ExecutingStep;
        StringAssert.Contains(step, "“the”");
        StringAssert.Contains(step, "日本語のテキスト");
        StringAssert.Contains(step, "🎉");
    }

    [TestMethod]
    public void StdinSentinel_Question_UsedForBlocked()
    {
        using var h = new DispatcherHarness { Stdin = new StringReader("Can I proceed?\n") };
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        int exit = h.Run(dispatcher, "blocked", "h1", "--question", "-");

        Assert.AreEqual(0, exit);
        Assert.AreEqual(AppTaskState.NeedsAttention, h.Store.FindAll().Single().State);
    }

    [TestMethod]
    public void NonDashFlagValue_UsedLiterally_NeverTouchesStdin()
    {
        using var h = new DispatcherHarness { Stdin = new StringReader("SHOULD NOT BE READ") };
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "working", "h1", "--goal", "literal goal text");

        Assert.AreEqual("literal goal text", h.Store.FindAll().Single().ExecutingStep);
    }
}
