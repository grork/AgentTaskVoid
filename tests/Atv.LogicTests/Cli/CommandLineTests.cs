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
        var result = CommandLine.Parse(["--json", "--strict", "--verbose", "--unsafe", "start", "h1"]);

        Assert.AreEqual("start", result.Verb);
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
        var result = CommandLine.Parse(["step", "h1", "message", "--json", "--strict"]);

        Assert.AreEqual("step", result.Verb);
        CollectionAssert.AreEqual(new[] { "h1", "message" }, result.Positionals.ToArray());
        Assert.IsTrue(result.Global.Json);
        Assert.IsTrue(result.Global.Strict);
    }

    [TestMethod]
    public void Parse_GlobalFlags_InterleavedWithPerVerbFlags_AllRecognized()
    {
        var result = CommandLine.Parse(["start", "h1", "--title", "T", "--json", "--reset", "--unsafe"]);

        Assert.AreEqual("start", result.Verb);
        CollectionAssert.AreEqual(new[] { "h1" }, result.Positionals.ToArray());
        Assert.AreEqual("T", result.Flags["title"]);
        Assert.IsTrue(result.Reset);
        Assert.IsTrue(result.Global.Json);
        Assert.IsTrue(result.Global.Unsafe);
    }

    [TestMethod]
    public void Parse_WatchdogMode_RecognizedAnywhere_CapturesRawValue()
    {
        var before = CommandLine.Parse(["--watchdog-mode", "inproc", "start", "h1"]);
        Assert.AreEqual("inproc", before.Global.WatchdogModeRaw);

        var after = CommandLine.Parse(["start", "h1", "--watchdog-mode", "off"]);
        Assert.AreEqual("off", after.Global.WatchdogModeRaw);
    }

    [TestMethod]
    public void Parse_WatchdogMode_MissingValue_Errors()
    {
        var result = CommandLine.Parse(["start", "h1", "--watchdog-mode"]);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Parse_WaitForDebugger_Recognized()
    {
        var result = CommandLine.Parse(["--wait-for-debugger", "start", "h1"]);
        Assert.IsTrue(result.Global.WaitForDebugger);
    }

    // ---- per-verb flags: only after the verb ------------------------------------

    [TestMethod]
    public void Parse_PerVerbFlag_BeforeVerb_Errors()
    {
        var result = CommandLine.Parse(["--title", "T", "start", "h1"]);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Parse_UnknownFlag_AfterVerb_Errors()
    {
        var result = CommandLine.Parse(["start", "h1", "--bogus"]);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Parse_PerVerbValueFlag_MissingValue_Errors()
    {
        var result = CommandLine.Parse(["start", "h1", "--title"]);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Parse_StartFlags_AllCaptured()
    {
        var result = CommandLine.Parse([
            "start", "h1",
            "--title", "My Title",
            "--subtitle", "My Subtitle",
            "--icon", "Robot",
            "--deep-link", "https://example.com",
            "--reset",
        ]);

        Assert.AreEqual("start", result.Verb);
        CollectionAssert.AreEqual(new[] { "h1" }, result.Positionals.ToArray());
        Assert.AreEqual("My Title", result.Flags["title"]);
        Assert.AreEqual("My Subtitle", result.Flags["subtitle"]);
        Assert.AreEqual("Robot", result.Flags["icon"]);
        Assert.AreEqual("https://example.com", result.Flags["deep-link"]);
        Assert.IsTrue(result.Reset);
    }

    [TestMethod]
    public void Parse_DoneWithSummary_Captured()
    {
        var result = CommandLine.Parse(["done", "h1", "--summary", "All finished."]);
        Assert.AreEqual("done", result.Verb);
        CollectionAssert.AreEqual(new[] { "h1" }, result.Positionals.ToArray());
        Assert.AreEqual("All finished.", result.Flags["summary"]);
    }

    // ---- positionals for multi-arg verbs -----------------------------------------

    [TestMethod]
    public void Parse_StepPositionals_HandleAndMessage()
    {
        var result = CommandLine.Parse(["step", "h1", "Working on it"]);
        Assert.AreEqual("step", result.Verb);
        CollectionAssert.AreEqual(new[] { "h1", "Working on it" }, result.Positionals.ToArray());
    }

    [TestMethod]
    public void Parse_StatePositionals_HandleAndState()
    {
        var result = CommandLine.Parse(["state", "h1", "paused"]);
        CollectionAssert.AreEqual(new[] { "h1", "paused" }, result.Positionals.ToArray());
    }

    [TestMethod]
    public void Parse_AttentionPositionals_HandleAndQuestion()
    {
        var result = CommandLine.Parse(["attention", "h1", "Continue?"]);
        CollectionAssert.AreEqual(new[] { "h1", "Continue?" }, result.Positionals.ToArray());
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
        var result = CommandLine.Parse(["START", "h1"]);
        Assert.AreEqual("start", result.Verb);
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
