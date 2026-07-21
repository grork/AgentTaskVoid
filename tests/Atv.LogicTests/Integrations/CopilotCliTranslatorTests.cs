using System.Text.Json;
using Codevoid.AgentTaskVoid.LogicTests.Persistence;
using static Codevoid.AgentTaskVoid.LogicTests.Integrations.CopilotCliTranslatorHarness;

namespace Codevoid.AgentTaskVoid.LogicTests.Integrations;

[TestClass]
[DoNotParallelize]
public sealed class CopilotCliTranslatorTests
{
    private const string Parent = "3bbb0815-bd30-435c-bb3f-dc077af65aae";
    private const string Child = "call_Vz0D4cK0PRP5dUOtr5FyJh0C";
    private const string Cwd = @"D:\temp\atv-copilot-sandbox";
    private const string Prompt = "Inspect README.md read-only.";

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => EnsureStubBuilt();

    private static string ParentPrompt(string prompt = "Fix the login bug") =>
        $$"""{"sessionId":"{{Parent}}","timestamp":1,"cwd":"{{Json(Cwd)}}","prompt":"{{Json(prompt)}}"}""";

    private static string ParentTask(string name, string prompt, string mode = "sync", string agentType = "explore") =>
        $$"""{"sessionId":"{{Parent}}","timestamp":2,"cwd":"{{Json(Cwd)}}","toolName":"task","toolArgs":"{{Json($$"""{"description":"Inspect a file","prompt":"{{Json(prompt)}}","agent_type":"{{agentType}}","name":"{{name}}","mode":"{{mode}}"}""")}}"}""";

    private static string ChildPrompt(string child = Child, string prompt = Prompt) =>
        $$"""{"sessionId":"{{child}}","timestamp":3,"cwd":"{{Json(Cwd)}}","prompt":"{{Json(prompt)}}"}""";

    private static string ChildTool(string toolName, string toolArgs, string child = Child) =>
        $$"""{"sessionId":"{{child}}","timestamp":4,"cwd":"{{Json(Cwd)}}","toolName":"{{toolName}}","toolArgs":"{{Json(toolArgs)}}"}""";

    private static string Json(string value) => JsonSerializer.Serialize(value)[1..^1];

    private static JsonDocument ReadState(TempDirectory state)
    {
        string path = Path.Combine(state.Path, "correlation-state.json");
        Assert.IsTrue(File.Exists(path), $"Expected correlation state at '{path}'.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static void StartAndClaim(TempDirectory state, string name = "readme-inspector", string prompt = Prompt, string child = Child)
    {
        RunTranslator("preToolUse", ParentTask(name, prompt), state.Path);
        RunTranslator("userPromptSubmitted", ChildPrompt(child, prompt), state.Path);
    }

    [TestMethod]
    public void UserPrompt_MapsToWorking_AndSystemNotificationsAreFiltered()
    {
        using var state = new TempDirectory();
        var calls = RunTranslator("userPromptSubmitted", ParentPrompt(), state.Path);
        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "working", Parent, "--goal", "-", "--cwd", Cwd }, calls[0].Argv);
        Assert.AreEqual("Fix the login bug", calls[0].Stdin?.TrimEnd());

        string systemPrompt = ParentPrompt("<system_notification>\nShell completed\n</system_notification>");
        Assert.IsEmpty(RunTranslator("userPromptSubmitted", systemPrompt, state.Path));
    }

    [TestMethod]
    public void ParentTaskStart_RegistersAgent_AndPersistsOnlyPromptHash()
    {
        using var state = new TempDirectory();
        var calls = RunTranslator("preToolUse", ParentTask("readme-inspector", Prompt), state.Path);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(
            new[] { "agent-started", Parent, "--agent", "readme-inspector", "--name", "explore", "--cwd", Cwd },
            calls[0].Argv);

        string rawState = File.ReadAllText(Path.Combine(state.Path, "correlation-state.json"));
        Assert.DoesNotContain(Prompt, rawState, "Raw child prompts must never be persisted.");
        using JsonDocument doc = JsonDocument.Parse(rawState);
        JsonElement pending = doc.RootElement.GetProperty("pending");
        Assert.HasCount(1, pending.EnumerateArray().ToArray());
        Assert.AreEqual(64, pending[0].GetProperty("promptHash").GetString()!.Length);
        Assert.HasCount(0, doc.RootElement.GetProperty("active").EnumerateArray().ToArray());
    }

    [TestMethod]
    public void ChildPrompt_AtomicallyClaimsPendingCorrelation()
    {
        using var state = new TempDirectory();
        RunTranslator("preToolUse", ParentTask("readme-inspector", Prompt), state.Path);

        Assert.IsEmpty(RunTranslator("userPromptSubmitted", ChildPrompt(), state.Path));

        using JsonDocument doc = ReadState(state);
        Assert.HasCount(0, doc.RootElement.GetProperty("pending").EnumerateArray().ToArray());
        JsonElement active = doc.RootElement.GetProperty("active");
        Assert.HasCount(1, active.EnumerateArray().ToArray());
        Assert.AreEqual(Child, active[0].GetProperty("childSession").GetString());
        Assert.AreEqual(Parent, active[0].GetProperty("parentSession").GetString());
        Assert.AreEqual("readme-inspector", active[0].GetProperty("taskName").GetString());
    }

    [TestMethod]
    public void ChildToolActivity_RoutesToParentAndAgent()
    {
        using var state = new TempDirectory();
        StartAndClaim(state);

        var calls = RunTranslator("preToolUse", ChildTool("view", """{"path":"D:\\temp\\atv-copilot-sandbox\\README.md"}"""), state.Path);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(
            new[] { "activity", Parent, "--kind", "read", "--label", "-", "--agent", "readme-inspector", "--cwd", Cwd },
            calls[0].Argv);
        Assert.AreEqual(@"D:\temp\atv-copilot-sandbox\README.md", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void ChildAskUser_UsesSameLocusForBlockedAndRecovery()
    {
        using var state = new TempDirectory();
        StartAndClaim(state);
        string askArgs = """{"question":"Which color?","choices":["Blue","Green"]}""";

        var blocked = RunTranslator("preToolUse", ChildTool("ask_user", askArgs), state.Path);
        Assert.HasCount(1, blocked);
        CollectionAssert.AreEqual(
            new[] { "blocked", Parent, "--question", "-", "--agent", "readme-inspector", "--cwd", Cwd },
            blocked[0].Argv);
        Assert.AreEqual("Which color?", blocked[0].Stdin?.TrimEnd());

        var recovery = RunTranslator("postToolUse", ChildTool("ask_user", askArgs), state.Path);
        Assert.HasCount(1, recovery);
        CollectionAssert.AreEqual(
            new[] { "activity", Parent, "--kind", "tool", "--name", "ask_user", "--label", "-", "--agent", "readme-inspector", "--cwd", Cwd },
            recovery[0].Argv);
    }

    [TestMethod]
    public void ChildAgentStop_RetiresAgent_RetriesReady_AndDeletesActiveMapping()
    {
        using var state = new TempDirectory();
        StartAndClaim(state);
        string payload = $$"""{"sessionId":"{{Child}}","timestamp":5,"cwd":"{{Json(Cwd)}}","transcriptPath":"","stopReason":"end_turn"}""";

        var calls = RunTranslator("agentStop", payload, state.Path);

        Assert.HasCount(2, calls);
        CollectionAssert.AreEqual(new[] { "agent-stopped", Parent, "--agent", "readme-inspector", "--cwd", Cwd }, calls[0].Argv);
        CollectionAssert.AreEqual(new[] { "ready", Parent, "--cwd", Cwd }, calls[1].Argv);
        using JsonDocument doc = ReadState(state);
        Assert.HasCount(0, doc.RootElement.GetProperty("active").EnumerateArray().ToArray());

        Assert.IsEmpty(
            RunTranslator("postToolUse", ParentTask("readme-inspector", Prompt), state.Path),
            "The parent-side sync completion fallback must not emit duplicate lifecycle claims after child agentStop already retired the correlation.");
    }

    [TestMethod]
    public void SyncTaskPost_IsIdempotentCompletionFallback()
    {
        using var state = new TempDirectory();
        RunTranslator("preToolUse", ParentTask("readme-inspector", Prompt), state.Path);

        var calls = RunTranslator("postToolUse", ParentTask("readme-inspector", Prompt), state.Path);

        Assert.HasCount(2, calls);
        Assert.AreEqual("agent-stopped", calls[0].Argv[0]);
        Assert.AreEqual("ready", calls[1].Argv[0]);
        using JsonDocument doc = ReadState(state);
        Assert.HasCount(0, doc.RootElement.GetProperty("pending").EnumerateArray().ToArray());
    }

    [TestMethod]
    public void BackgroundTaskPost_DoesNotRetireBeforeNotification()
    {
        using var state = new TempDirectory();
        RunTranslator("preToolUse", ParentTask("background-agent", Prompt, mode: "background"), state.Path);

        Assert.IsEmpty(RunTranslator("postToolUse", ParentTask("background-agent", Prompt, mode: "background"), state.Path));

        using JsonDocument doc = ReadState(state);
        Assert.HasCount(1, doc.RootElement.GetProperty("pending").EnumerateArray().ToArray());
    }

    [TestMethod]
    public void BackgroundAgentIdle_RetiresByTaskName_AndRetriesReady()
    {
        using var state = new TempDirectory();
        RunTranslator("preToolUse", ParentTask("background-agent", Prompt, mode: "background"), state.Path);
        string payload = $$"""{"sessionId":"{{Parent}}","timestamp":6,"cwd":"{{Json(Cwd)}}","message":"Agent \"background-agent\" (explore) has finished processing and is now idle.","title":"Agent background-agent idle","hook_event_name":"Notification","notification_type":"agent_idle"}""";

        var calls = RunTranslator("notification", payload, state.Path);

        Assert.HasCount(2, calls);
        CollectionAssert.AreEqual(new[] { "agent-stopped", Parent, "--agent", "background-agent", "--cwd", Cwd }, calls[0].Argv);
        CollectionAssert.AreEqual(new[] { "ready", Parent, "--cwd", Cwd }, calls[1].Argv);
    }

    [TestMethod]
    public void DuplicatePromptCandidates_AreAmbiguousAndNeverGuessed()
    {
        using var state = new TempDirectory();
        RunTranslator("preToolUse", ParentTask("first-agent", Prompt), state.Path);
        RunTranslator("preToolUse", ParentTask("second-agent", Prompt), state.Path);

        Assert.IsEmpty(RunTranslator("userPromptSubmitted", ChildPrompt(), state.Path));

        using JsonDocument doc = ReadState(state);
        Assert.HasCount(2, doc.RootElement.GetProperty("pending").EnumerateArray().ToArray());
        Assert.HasCount(0, doc.RootElement.GetProperty("active").EnumerateArray().ToArray());
        Assert.Contains("ambiguous", File.ReadAllText(Path.Combine(state.Path, "translator.log")));
    }

    [TestMethod]
    public async Task ConcurrentChildClaims_AllowExactlyOneWinner()
    {
        using var state = new TempDirectory();
        RunTranslator("preToolUse", ParentTask("readme-inspector", Prompt), state.Path);

        Task<List<StubInvocation>> first = Task.Run(() =>
            RunTranslator("userPromptSubmitted", ChildPrompt("call_first", Prompt), state.Path));
        Task<List<StubInvocation>> second = Task.Run(() =>
            RunTranslator("userPromptSubmitted", ChildPrompt("call_second", Prompt), state.Path));
        await Task.WhenAll(first, second);

        Assert.IsEmpty(first.Result);
        Assert.IsEmpty(second.Result);
        using JsonDocument doc = ReadState(state);
        Assert.HasCount(0, doc.RootElement.GetProperty("pending").EnumerateArray().ToArray());
        JsonElement[] active = [.. doc.RootElement.GetProperty("active").EnumerateArray()];
        Assert.HasCount(1, active);
        Assert.Contains(active[0].GetProperty("childSession").GetString(), new[] { "call_first", "call_second" });
    }

    [TestMethod]
    public void PermissionAndAskUserSignals_MapOnlyFromVerifiedEvents()
    {
        using var state = new TempDirectory();
        string permission = $$"""{"sessionId":"{{Parent}}","timestamp":7,"cwd":"{{Json(Cwd)}}","message":"Fetch URL: https://example.com","title":"Permission needed","hook_event_name":"Notification","notification_type":"permission_prompt"}""";
        var blocked = RunTranslator("notification", permission, state.Path);
        Assert.HasCount(1, blocked);
        Assert.AreEqual("blocked", blocked[0].Argv[0]);
        Assert.AreEqual("Fetch URL: https://example.com", blocked[0].Stdin?.TrimEnd());

        string ask = $$"""{"sessionId":"{{Parent}}","timestamp":8,"cwd":"{{Json(Cwd)}}","toolName":"ask_user","toolArgs":"{{Json("""{"question":"Proceed?","choices":["Yes","No"]}""")}}"}""";
        Assert.AreEqual("blocked", RunTranslator("preToolUse", ask, state.Path).Single().Argv[0]);
    }

    [TestMethod]
    public void MainAgentStopAndSessionEnd_MapConservatively()
    {
        using var state = new TempDirectory();
        string stop = $$"""{"sessionId":"{{Parent}}","timestamp":9,"cwd":"{{Json(Cwd)}}","transcriptPath":"C:\\trace.jsonl","stopReason":"end_turn"}""";
        CollectionAssert.AreEqual(new[] { "ready", Parent, "--cwd", Cwd }, RunTranslator("agentStop", stop, state.Path).Single().Argv);

        string complete = $$"""{"sessionId":"{{Parent}}","timestamp":10,"cwd":"{{Json(Cwd)}}","reason":"complete"}""";
        Assert.AreEqual("ready", RunTranslator("sessionEnd", complete, state.Path).Single().Argv[0]);

        string exit = $$"""{"sessionId":"{{Parent}}","timestamp":11,"cwd":"{{Json(Cwd)}}","reason":"user_exit"}""";
        CollectionAssert.AreEqual(new[] { "session-ended", Parent, "--reason", "finished" }, RunTranslator("sessionEnd", exit, state.Path).Single().Argv);

        string error = $$"""{"sessionId":"{{Parent}}","timestamp":12,"cwd":"{{Json(Cwd)}}","reason":"timeout"}""";
        CollectionAssert.AreEqual(new[] { "session-ended", Parent, "--reason", "error" }, RunTranslator("sessionEnd", error, state.Path).Single().Argv);
    }

    [TestMethod]
    public void CompactAndFatalError_MapToSemanticClaims()
    {
        using var state = new TempDirectory();
        string compact = $$"""{"sessionId":"{{Parent}}","timestamp":13,"cwd":"{{Json(Cwd)}}","trigger":"manual","customInstructions":""}""";
        CollectionAssert.AreEqual(new[] { "activity", Parent, "--kind", "compacting", "--cwd", Cwd }, RunTranslator("preCompact", compact, state.Path).Single().Argv);

        string error = $$"""{"sessionId":"{{Parent}}","timestamp":14,"cwd":"{{Json(Cwd)}}","error":{"message":"request timed out","name":"ModelError"},"errorContext":"model_call","recoverable":false}""";
        var call = RunTranslator("errorOccurred", error, state.Path).Single();
        CollectionAssert.AreEqual(new[] { "broken", Parent, "--reason", "timeout", "--detail", "-", "--cwd", Cwd }, call.Argv);
        Assert.AreEqual("ModelError: request timed out", call.Stdin?.TrimEnd());
    }

    [TestMethod]
    public void Utf8PromptCorrelation_RoundTripsWithoutPersistingPrompt()
    {
        using var state = new TempDirectory();
        const string prompt = "Inspect \"café\" 字\nsecond line \U0001F600";
        RunTranslator("preToolUse", ParentTask("utf8-agent", prompt), state.Path);
        RunTranslator("userPromptSubmitted", ChildPrompt("call_utf8", prompt), state.Path);

        string rawState = File.ReadAllText(Path.Combine(state.Path, "correlation-state.json"));
        Assert.DoesNotContain(prompt, rawState);
        using JsonDocument doc = JsonDocument.Parse(rawState);
        Assert.AreEqual("call_utf8", doc.RootElement.GetProperty("active")[0].GetProperty("childSession").GetString());
    }

    [TestMethod]
    public void MalformedOrUncorrelatedChildPayloads_AreSafeNoOps()
    {
        using var state = new TempDirectory();
        Assert.IsEmpty(RunTranslator("preToolUse", "{not-json", state.Path));
        Assert.IsEmpty(RunTranslator("preToolUse", ChildTool("view", """{"path":"README.md"}""", "call_unknown"), state.Path));
    }

    // ---- DIST-12 SS4: atv-command.txt override precedence ---------------------
    // The override file lives in the state root Get-StateRoot resolves (here,
    // the per-test TempDirectory ATV_COPILOT_STATE_DIR already points at) --
    // beside correlation-state.json/translator.log, no extra isolation needed.

    [TestMethod]
    public void AtvCommandTxt_FileUsed_InvokesConfiguredCommand()
    {
        using var state = new TempDirectory();
        Directory.CreateDirectory(state.Path);
        File.WriteAllText(Path.Combine(state.Path, "atv-command.txt"), EnsureStubBuilt());

        var environment = new Dictionary<string, string?> { ["ATV_TRANSLATOR_STUB_EXE"] = null };
        var calls = RunTranslator("userPromptSubmitted", ParentPrompt(), state.Path, environment);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "working", Parent, "--goal", "-", "--cwd", Cwd }, calls[0].Argv);
        Assert.AreEqual("Fix the login bug", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void AtvCommandTxt_StubVarBeatsFile_FileIgnored()
    {
        using var state = new TempDirectory();
        Directory.CreateDirectory(state.Path);
        File.WriteAllText(Path.Combine(state.Path, "atv-command.txt"), @"C:\does\not\exist\bogus-atv.exe");

        // Stub var left set (RunTranslator sets it by default) -- it must win
        // over the present-but-bogus file.
        var calls = RunTranslator("userPromptSubmitted", ParentPrompt(), state.Path);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "working", Parent, "--goal", "-", "--cwd", Cwd }, calls[0].Argv);
        Assert.AreEqual("Fix the login bug", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void AtvCommandTxt_Absent_MatchesExistingBehavior()
    {
        using var state = new TempDirectory();
        // No atv-command.txt dropped -- the new tier must be inert.

        var calls = RunTranslator("userPromptSubmitted", ParentPrompt(), state.Path);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "working", Parent, "--goal", "-", "--cwd", Cwd }, calls[0].Argv);
        Assert.AreEqual("Fix the login bug", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void AtvCommandTxt_BrokenTarget_NoOp_NeverFallsThroughToPathAtv()
    {
        using var state = new TempDirectory();
        Directory.CreateDirectory(state.Path);
        File.WriteAllText(Path.Combine(state.Path, "atv-command.txt"), @"C:\does\not\exist\bogus-atv.exe");

        // A decoy "atv.exe" is the ONLY atv resolvable from this run's PATH, so a
        // buggy fall-through to Get-Command atv -> & atv would be caught here --
        // and physically cannot reach any live install.
        using var decoyDir = new TempDirectory();
        IntegrationTranslatorProcess.CreateAtvDecoy(decoyDir.Path);
        string decoyOutput = Path.Combine(decoyDir.Path, "decoy-out.jsonl");

        var environment = new Dictionary<string, string?>
        {
            ["ATV_TRANSLATOR_STUB_EXE"] = null,
            ["PATH"] = IntegrationTranslatorProcess.PrependToPath(decoyDir.Path),
            ["ATV_STUB_OUTPUT"] = decoyOutput,
        };

        RunTranslator("userPromptSubmitted", ParentPrompt(), state.Path, environment);

        Assert.IsFalse(
            File.Exists(decoyOutput),
            "A broken atv-command.txt override must no-op, never fall through to Get-Command atv -> & atv -- the decoy on PATH must never be invoked.");
    }
}
