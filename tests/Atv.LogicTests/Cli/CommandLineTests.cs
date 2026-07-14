using Atv.Cli;

namespace Atv.LogicTests.Cli;

/// <summary>
/// AC2: global options parse in ANY position (before or after the verb,
/// interleaved with positionals); per-verb flags are recognized only AFTER
/// the verb token (ERGO-27 C6). Hand-rolled, AOT-safe tokenizer -- no
/// reflection-driven binder (INFRA-2).
/// </summary>
[TestClass]
public sealed class CommandLineTests
{
    // ---- bare / help / version -------------------------------------------------

    [TestMethod]
    public void Parse_NoArgs_NoVerb_NoError()
    {
        var result = CommandLine.Parse([]);
        Assert.IsNull(result.Verb);
        Assert.IsFalse(result.ShowHelp);
        Assert.IsFalse(result.ShowVersion);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    [DataRow("--help")]
    [DataRow("-h")]
    public void Parse_HelpToken_SetsShowHelp(string token)
    {
        var result = CommandLine.Parse([token]);
        Assert.IsTrue(result.ShowHelp);
    }

    [TestMethod]
    public void Parse_VersionToken_SetsShowVersion()
    {
        var result = CommandLine.Parse(["--version"]);
        Assert.IsTrue(result.ShowVersion);
    }

    // ---- global options: any position --------------------------------------------

    [TestMethod]
    public void Parse_GlobalFlags_BeforeVerb_AllRecognized()
    {
        var result = CommandLine.Parse(["--json", "--strict", "--verbose", "--unsafe", "working", "h1"]);

        Assert.AreEqual("working", result.Verb);
        CollectionAssert.AreEqual(new[] { "h1" }, result.Positionals.ToArray());
        Assert.IsTrue(result.Global.Json);
        Assert.IsTrue(result.Global.Strict);
        Assert.IsTrue(result.Global.Verbose);
        Assert.IsTrue(result.Global.Unsafe);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Parse_GlobalFlags_AfterVerbAndPositionals_AllRecognized()
    {
        var result = CommandLine.Parse(["activity", "h1", "--json", "--strict"]);

        Assert.AreEqual("activity", result.Verb);
        CollectionAssert.AreEqual(new[] { "h1" }, result.Positionals.ToArray());
        Assert.IsTrue(result.Global.Json);
        Assert.IsTrue(result.Global.Strict);
    }

    [TestMethod]
    public void Parse_GlobalFlags_InterleavedWithPerVerbFlags_AllRecognized()
    {
        var result = CommandLine.Parse(["working", "h1", "--title", "T", "--json", "--unsafe"]);

        Assert.AreEqual("working", result.Verb);
        CollectionAssert.AreEqual(new[] { "h1" }, result.Positionals.ToArray());
        Assert.AreEqual("T", result.Flags["title"]);
        Assert.IsTrue(result.Global.Json);
        Assert.IsTrue(result.Global.Unsafe);
    }

    [TestMethod]
    public void Parse_WatchdogMode_RecognizedAnywhere_CapturesRawValue()
    {
        var before = CommandLine.Parse(["--watchdog-mode", "inproc", "working", "h1"]);
        Assert.AreEqual("inproc", before.Global.WatchdogModeRaw);

        var after = CommandLine.Parse(["working", "h1", "--watchdog-mode", "off"]);
        Assert.AreEqual("off", after.Global.WatchdogModeRaw);
    }

    [TestMethod]
    public void Parse_WatchdogMode_MissingValue_Errors()
    {
        var result = CommandLine.Parse(["working", "h1", "--watchdog-mode"]);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Parse_WaitForDebugger_Recognized()
    {
        var result = CommandLine.Parse(["--wait-for-debugger", "working", "h1"]);
        Assert.IsTrue(result.Global.WaitForDebugger);
    }

    // ---- per-verb flags: only after the verb ------------------------------------

    [TestMethod]
    public void Parse_PerVerbFlag_BeforeVerb_Errors()
    {
        var result = CommandLine.Parse(["--title", "T", "working", "h1"]);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Parse_UnknownFlag_AfterVerb_Errors()
    {
        var result = CommandLine.Parse(["working", "h1", "--bogus"]);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Parse_PerVerbValueFlag_MissingValue_Errors()
    {
        var result = CommandLine.Parse(["working", "h1", "--title"]);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Parse_WorkingFlags_AllCaptured()
    {
        var result = CommandLine.Parse([
            "working", "h1",
            "--title", "My Title",
            "--subtitle", "My Subtitle",
            "--icon", "Robot",
            "--deep-link", "https://example.com",
            "--goal", "Fix the bug",
        ]);

        Assert.AreEqual("working", result.Verb);
        CollectionAssert.AreEqual(new[] { "h1" }, result.Positionals.ToArray());
        Assert.AreEqual("My Title", result.Flags["title"]);
        Assert.AreEqual("My Subtitle", result.Flags["subtitle"]);
        Assert.AreEqual("Robot", result.Flags["icon"]);
        Assert.AreEqual("https://example.com", result.Flags["deep-link"]);
        Assert.AreEqual("Fix the bug", result.Flags["goal"]);
    }

    // ---- phase 16 (ERGO-29): --icon-file -----------------------------------------

    [TestMethod]
    public void Parse_IconFile_Captured()
    {
        var result = CommandLine.Parse(["working", "h1", "--icon-file", @"C:\logos\brand.png"]);

        Assert.AreEqual(@"C:\logos\brand.png", result.Flags["icon-file"]);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Parse_IconAndIconFile_BothCaptured_ConflictLeftToDispatcher()
    {
        // CommandLine only tokenizes -- the "--icon and --icon-file together
        // is a usage error" rule is argument-SHAPE validation, which is the
        // Dispatcher's job (matches the existing "verb-name validity isn't a
        // parse Error" precedent).
        var result = CommandLine.Parse(["working", "h1", "--icon", "Robot", "--icon-file", "logo.png"]);

        Assert.AreEqual("Robot", result.Flags["icon"]);
        Assert.AreEqual("logo.png", result.Flags["icon-file"]);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Parse_IconFile_MissingValue_Errors()
    {
        var result = CommandLine.Parse(["working", "h1", "--icon-file"]);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    [DataRow("activity")]
    [DataRow("blocked")]
    [DataRow("ready")]
    [DataRow("broken")]
    [DataRow("agent-started")]
    [DataRow("agent-stopped")]
    public void Parse_IconFile_OnEveryUpsertingVerb_Captured(string verb)
    {
        var result = CommandLine.Parse([verb, "h1", "--icon-file", "logo.png"]);
        Assert.AreEqual("logo.png", result.Flags["icon-file"]);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Parse_ActivityFlags_KindLabelAgentName_AllCaptured()
    {
        var result = CommandLine.Parse(["activity", "h1", "--kind", "edit", "--label", "auth.ts", "--agent", "a1", "--name", "worker"]);

        Assert.AreEqual("activity", result.Verb);
        Assert.AreEqual("edit", result.Flags["kind"]);
        Assert.AreEqual("auth.ts", result.Flags["label"]);
        Assert.AreEqual("a1", result.Flags["agent"]);
        Assert.AreEqual("worker", result.Flags["name"]);
    }

    [TestMethod]
    public void Parse_BlockedFlags_QuestionAndAgent_Captured()
    {
        var result = CommandLine.Parse(["blocked", "h1", "--question", "Continue?", "--agent", "a1"]);
        Assert.AreEqual("Continue?", result.Flags["question"]);
        Assert.AreEqual("a1", result.Flags["agent"]);
    }

    [TestMethod]
    public void Parse_ReadyWithSummary_Captured()
    {
        var result = CommandLine.Parse(["ready", "h1", "--summary", "All finished."]);
        Assert.AreEqual("ready", result.Verb);
        CollectionAssert.AreEqual(new[] { "h1" }, result.Positionals.ToArray());
        Assert.AreEqual("All finished.", result.Flags["summary"]);
    }

    [TestMethod]
    public void Parse_BrokenFlags_ReasonAndDetail_Captured()
    {
        var result = CommandLine.Parse(["broken", "h1", "--reason", "api-error", "--detail", "connection reset"]);
        Assert.AreEqual("api-error", result.Flags["reason"]);
        Assert.AreEqual("connection reset", result.Flags["detail"]);
    }

    [TestMethod]
    public void Parse_SessionEndedFlags_Reason_Captured()
    {
        var result = CommandLine.Parse(["session-ended", "h1", "--reason", "finished"]);
        Assert.AreEqual("session-ended", result.Verb);
        Assert.AreEqual("finished", result.Flags["reason"]);
    }

    [TestMethod]
    public void Parse_StdinSentinel_CapturedAsLiteralDashValue()
    {
        // CommandLine itself never reads stdin -- the "-" sentinel is just an
        // ordinary string value at this layer; the Dispatcher resolves it.
        var result = CommandLine.Parse(["working", "h1", "--goal", "-"]);
        Assert.AreEqual("-", result.Flags["goal"]);
    }

    // ---- positionals for multi-arg verbs -----------------------------------------

    [TestMethod]
    public void Parse_ActivityPositionals_JustHandle()
    {
        var result = CommandLine.Parse(["activity", "h1", "--kind", "shell"]);
        CollectionAssert.AreEqual(new[] { "h1" }, result.Positionals.ToArray());
    }

    [TestMethod]
    public void Parse_RemoveHasNoFlags_JustHandle()
    {
        var result = CommandLine.Parse(["remove", "h1"]);
        CollectionAssert.AreEqual(new[] { "h1" }, result.Positionals.ToArray());
        Assert.IsEmpty(result.Flags);
    }

    [TestMethod]
    public void Parse_VerbIsLowercased()
    {
        var result = CommandLine.Parse(["WORKING", "h1"]);
        Assert.AreEqual("working", result.Verb);
    }

    // ---- phase 10: list / clear / doctor -----------------------------------------

    [TestMethod]
    public void Parse_List_NoPositionalsOrFlags()
    {
        var result = CommandLine.Parse(["list"]);
        Assert.AreEqual("list", result.Verb);
        Assert.IsEmpty(result.Positionals);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Parse_List_Json_GlobalFlagRecognized()
    {
        var result = CommandLine.Parse(["list", "--json"]);
        Assert.AreEqual("list", result.Verb);
        Assert.IsTrue(result.Global.Json);
    }

    [TestMethod]
    public void Parse_Doctor_JsonAndVerbose_GlobalFlagsRecognized()
    {
        var result = CommandLine.Parse(["doctor", "--json", "--verbose"]);
        Assert.AreEqual("doctor", result.Verb);
        Assert.IsTrue(result.Global.Json);
        Assert.IsTrue(result.Global.Verbose);
    }

    [TestMethod]
    public void Parse_Clear_NoFlags_IncludeRecycleBinDefaultsFalse()
    {
        var result = CommandLine.Parse(["clear"]);
        Assert.AreEqual("clear", result.Verb);
        Assert.IsFalse(result.IncludeRecycleBin);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Parse_Clear_IncludeRecycleBin_SetsFlag()
    {
        var result = CommandLine.Parse(["clear", "--include-recycle-bin"]);
        Assert.AreEqual("clear", result.Verb);
        Assert.IsTrue(result.IncludeRecycleBin);
    }

    [TestMethod]
    public void Parse_IncludeRecycleBin_BeforeVerb_Errors()
    {
        // Per-verb flags (like --reset) are only recognized AFTER the verb.
        var result = CommandLine.Parse(["--include-recycle-bin", "clear"]);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Parse_UnknownVerb_NoParseError_LeftForDispatcher()
    {
        // CommandLine only tokenizes; verb-name validity is the Dispatcher's job.
        var result = CommandLine.Parse(["frobnicate", "h1"]);
        Assert.AreEqual("frobnicate", result.Verb);
        Assert.IsNull(result.Error);
    }

    // ---- phase 11: run's "--" child-command separator -----------------------------

    [TestMethod]
    public void Parse_Run_DoubleDashSeparatesChildArgs()
    {
        var result = CommandLine.Parse(["run", "--title", "Build", "--", "dotnet", "build"]);

        Assert.AreEqual("run", result.Verb);
        Assert.AreEqual("Build", result.Flags["title"]);
        CollectionAssert.AreEqual(new[] { "dotnet", "build" }, result.ChildArgs.ToArray());
        Assert.IsEmpty(result.Positionals);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Parse_Run_ChildArgsWithFlagLikeTokens_NotInterpretedAsAtvFlags()
    {
        // A child's own --verbose/--json must never be swallowed as atv's own global flag.
        var result = CommandLine.Parse(["run", "--", "npm", "test", "--verbose", "--json"]);

        CollectionAssert.AreEqual(new[] { "npm", "test", "--verbose", "--json" }, result.ChildArgs.ToArray());
        Assert.IsFalse(result.Global.Verbose);
        Assert.IsFalse(result.Global.Json);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Parse_Run_NoDoubleDash_EmptyChildArgs()
    {
        var result = CommandLine.Parse(["run", "--title", "Build"]);
        Assert.IsEmpty(result.ChildArgs);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Parse_Run_DoubleDashWithNoChildTokens_EmptyChildArgs()
    {
        var result = CommandLine.Parse(["run", "--title", "Build", "--"]);
        Assert.IsEmpty(result.ChildArgs);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Parse_DoubleDash_BeforeVerb_StillErrors()
    {
        // "--" only becomes the child-args separator once a verb is set.
        var result = CommandLine.Parse(["--", "run"]);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Parse_Run_GlobalFlagsBeforeDoubleDash_StillRecognized()
    {
        var result = CommandLine.Parse(["--strict", "run", "--title", "T", "--", "cmd", "/c", "echo", "hi"]);

        Assert.IsTrue(result.Global.Strict);
        Assert.AreEqual("T", result.Flags["title"]);
        CollectionAssert.AreEqual(new[] { "cmd", "/c", "echo", "hi" }, result.ChildArgs.ToArray());
    }
}
