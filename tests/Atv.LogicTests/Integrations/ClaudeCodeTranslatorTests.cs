using static Atv.LogicTests.Integrations.ClaudeCodeTranslatorHarness;

namespace Atv.LogicTests.Integrations;

/// <summary>
/// Phase 18 AC2: drives translate.ps1 under Windows PowerShell 5.1 against a
/// stub atv, with payload shapes taken from the ACTUAL phase-14 captures
/// (<c>tools/host-event-recorder/captures/session-cc-*.jsonl</c>, cross-
/// referenced against <c>docs/host-events/claude-code.md</c>'s Findings
/// section) wherever a real capture exists. Payloads for events the phase-14
/// capture never exercised (StopFailure, SessionStart source=compact,
/// TodoWrite) are synthetic, built strictly from ERGO-31/the phase-18 file's
/// own documented field expectations -- called out per test.
///
/// A note on trailing newlines: PowerShell's own mechanism for delivering
/// text to an external process's stdin (both the real pipe-to-atv path and,
/// incidentally, a bare no-pipe native-command invocation) always appends a
/// trailing newline of its own. This is harmless in production -- atv's own
/// stdin contract explicitly trims trailing whitespace (docs/integration-api.md
/// SS7), and verbs that pass no "-" flag (agent-started, agent-stopped, bare
/// ready, session-ended) never read stdin at all, so the stray newline is
/// simply never consumed. Assertions below trim it to isolate the real claim
/// (every byte of the MEANINGFUL content survives) from this PowerShell-side
/// artifact.
/// </summary>
[TestClass]
public sealed class ClaudeCodeTranslatorTests
{
    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => EnsureStubBuilt();

    // ---- UserPromptSubmit -> working --goal - --------------------------------

    [TestMethod]
    public void UserPromptSubmit_MapsTo_Working_WithGoalOnStdin()
    {
        // Real capture: session-cc-interactive-3.jsonl line 2.
        string payload = """{"session_id":"86297cc0-a3e5-43ec-9bf2-9a322a6aedd8","cwd":"C:\\scratch","prompt_id":"p1","permission_mode":"auto","hook_event_name":"UserPromptSubmit","prompt":"tell me a joke about cats"}""";

        var calls = RunTranslator("UserPromptSubmit", payload, projectDir: @"C:\proj");

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "working", "86297cc0-a3e5-43ec-9bf2-9a322a6aedd8", "--goal", "-", "--cwd", @"C:\proj" }, calls[0].Argv);
        Assert.AreEqual("tell me a joke about cats", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void UserPromptSubmit_WithoutProjectDir_OmitsCwdEntirely()
    {
        string payload = """{"session_id":"sess-1","hook_event_name":"UserPromptSubmit","prompt":"hi"}""";

        var calls = RunTranslator("UserPromptSubmit", payload, projectDir: null);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "working", "sess-1", "--goal", "-" }, calls[0].Argv);
    }

    // ---- PreToolUse/PostToolUse -> activity --kind <map> --label - ----------

    [TestMethod]
    public void PreToolUse_Bash_MapsTo_Activity_ShellKind_CommandLabel()
    {
        // Real capture: session-cc-20260712-212159.jsonl line 4.
        string payload = """{"session_id":"1d17eebc-3060-4494-8238-a22f4ac7bacb","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"ls -la","description":"List files in current directory"},"tool_use_id":"toolu_01"}""";

        var calls = RunTranslator("PreToolUse", payload, projectDir: @"C:\proj");

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "activity", "1d17eebc-3060-4494-8238-a22f4ac7bacb", "--kind", "shell", "--label", "-", "--cwd", @"C:\proj" }, calls[0].Argv);
        Assert.AreEqual("ls -la", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void PostToolUse_Read_MapsTo_Activity_ReadKind_FilePathLabel()
    {
        // Real capture: session-cc-20260712-212159.jsonl line 9 (PostToolUse Read).
        string payload = """{"session_id":"1d17eebc-3060-4494-8238-a22f4ac7bacb","hook_event_name":"PostToolUse","tool_name":"Read","tool_input":{"file_path":"C:\\temp\\activate.ps1"},"tool_response":{"type":"text"},"tool_use_id":"toolu_02"}""";

        var calls = RunTranslator("PostToolUse", payload, projectDir: @"C:\proj");

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "activity", "1d17eebc-3060-4494-8238-a22f4ac7bacb", "--kind", "read", "--label", "-", "--cwd", @"C:\proj" }, calls[0].Argv);
        Assert.AreEqual(@"C:\temp\activate.ps1", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void PreToolUse_SubagentScoped_CarriesAgentAndNameFlags()
    {
        // Real capture: session-cc-interactive-1.jsonl line 21 (subagent Bash).
        string payload = """{"session_id":"4b8f7eec-9c33-4929-bdd3-1df2b2ec17d0","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"command":"git log --oneline -5"},"agent_id":"a72aee33467652aa4","agent_type":"general-purpose","tool_use_id":"toolu_03"}""";

        var calls = RunTranslator("PreToolUse", payload, projectDir: @"C:\proj");

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(
            new[] { "activity", "4b8f7eec-9c33-4929-bdd3-1df2b2ec17d0", "--kind", "shell", "--label", "-", "--agent", "a72aee33467652aa4", "--name", "general-purpose", "--cwd", @"C:\proj" },
            calls[0].Argv);
        Assert.AreEqual("git log --oneline -5", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void PreToolUse_AgentTool_IsSuppressed_NoActivityCall()
    {
        // Real capture: session-cc-20260712-212159.jsonl line 12 (Agent tool spawn --
        // ERGO-31: "There is no delegate kind... never rendered as an activity line").
        string payload = """{"session_id":"1d17eebc-3060-4494-8238-a22f4ac7bacb","hook_event_name":"PreToolUse","tool_name":"Agent","tool_input":{"description":"Reverse string alpha","subagent_type":"general-purpose"},"tool_use_id":"toolu_04"}""";

        var calls = RunTranslator("PreToolUse", payload, projectDir: @"C:\proj");

        Assert.IsEmpty(calls);
    }

    [TestMethod]
    public void PreToolUse_UnmappedTool_FallsBackToToolKind_WithNameAndLabel()
    {
        string payload = """{"session_id":"sess-1","hook_event_name":"PreToolUse","tool_name":"mcp__jira__create_ticket","tool_input":{"summary":"Fix the login bug"},"tool_use_id":"toolu_05"}""";

        var calls = RunTranslator("PreToolUse", payload, projectDir: null);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "activity", "sess-1", "--kind", "tool", "--name", "mcp__jira__create_ticket", "--label", "-" }, calls[0].Argv);
        Assert.AreEqual("Fix the login bug", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void PreToolUse_UnmappedTool_WithNoStringFields_LabelFallsBackToToolName()
    {
        // AC2's literal "an unmapped tool falls to --kind tool --label <tool_name>"
        // degenerate case: no field on tool_input can supply a subject at all.
        string payload = """{"session_id":"sess-1","hook_event_name":"PreToolUse","tool_name":"SomeFutureTool","tool_input":{"count":3,"enabled":true},"tool_use_id":"toolu_06"}""";

        var calls = RunTranslator("PreToolUse", payload, projectDir: null);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "activity", "sess-1", "--kind", "tool", "--name", "SomeFutureTool", "--label", "-" }, calls[0].Argv);
        Assert.AreEqual("SomeFutureTool", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void PreToolUse_TodoWrite_MapsTo_Activity_PlanKind_ComposedLabel()
    {
        // Synthetic: TodoWrite was never captured live in phase 14 (not in the
        // beat corpus). Shape follows ERGO-31 SS3's own documented "(n/m) <item>"
        // composition guidance -- flagged as an assumption in the executor report.
        string payload = """{"session_id":"sess-1","hook_event_name":"PreToolUse","tool_name":"TodoWrite","tool_input":{"todos":[{"content":"Write tests","status":"completed","activeForm":"Writing tests"},{"content":"Implement feature","status":"in_progress","activeForm":"Implementing feature"},{"content":"Update docs","status":"pending","activeForm":"Updating docs"}]},"tool_use_id":"toolu_07"}""";

        var calls = RunTranslator("PreToolUse", payload, projectDir: null);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "activity", "sess-1", "--kind", "plan", "--label", "-" }, calls[0].Argv);
        Assert.AreEqual("(2/3) Implement feature", calls[0].Stdin?.TrimEnd());
    }

    // ---- PermissionRequest -> blocked --question - ---------------------------

    [TestMethod]
    public void PermissionRequest_MainThread_MapsTo_Blocked_NoAgentFlag()
    {
        // Real capture: session-cc-interactive-1.jsonl line 8.
        string payload = """{"session_id":"4b8f7eec-9c33-4929-bdd3-1df2b2ec17d0","hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"git init","description":"Initialize a new git repository"},"permission_suggestions":[]}""";

        var calls = RunTranslator("PermissionRequest", payload, projectDir: @"C:\proj");

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "blocked", "4b8f7eec-9c33-4929-bdd3-1df2b2ec17d0", "--question", "-", "--cwd", @"C:\proj" }, calls[0].Argv);
        Assert.AreEqual("Bash: git init", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void PermissionRequest_SubagentOriginated_CarriesAgentFlag()
    {
        // Real capture: session-cc-interactive-1.jsonl line 37 (subagent Write prompt --
        // the phase-14 capture finding 5 that PermissionRequest, not Notification,
        // carries agent_id).
        string payload = """{"session_id":"4b8f7eec-9c33-4929-bdd3-1df2b2ec17d0","hook_event_name":"PermissionRequest","tool_name":"Write","tool_input":{"file_path":"C:\\temp\\cat.txt","content":"cat"},"agent_id":"a7f043e61ee0d6fa0","agent_type":"general-purpose"}""";

        var calls = RunTranslator("PermissionRequest", payload, projectDir: null);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "blocked", "4b8f7eec-9c33-4929-bdd3-1df2b2ec17d0", "--question", "-", "--agent", "a7f043e61ee0d6fa0" }, calls[0].Argv);
        Assert.AreEqual(@"Write: C:\temp\cat.txt", calls[0].Stdin?.TrimEnd());
    }

    // ---- Notification -> ready (idle_prompt only) -----------------------------

    [TestMethod]
    public void Notification_IdlePrompt_MapsTo_BareReady()
    {
        // Real capture: session-cc-interactive-3.jsonl line 6.
        string payload = """{"session_id":"86297cc0-a3e5-43ec-9bf2-9a322a6aedd8","hook_event_name":"Notification","message":"Claude is waiting for your input","notification_type":"idle_prompt"}""";

        var calls = RunTranslator("Notification", payload, projectDir: @"C:\proj");

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "ready", "86297cc0-a3e5-43ec-9bf2-9a322a6aedd8", "--cwd", @"C:\proj" }, calls[0].Argv);
        Assert.IsTrue(string.IsNullOrWhiteSpace(calls[0].Stdin), $"Expected no meaningful stdin; PowerShell may still deliver a harmless trailing newline atv never reads (no flag requests it). Got: {calls[0].Stdin}");
    }

    [TestMethod]
    public void Notification_PermissionPrompt_IsNoOp()
    {
        // Real capture: session-cc-interactive-1.jsonl line 9. PermissionRequest
        // already owns Blocked (phase-14 finding 5) -- this must be a pure no-op.
        string payload = """{"session_id":"4b8f7eec-9c33-4929-bdd3-1df2b2ec17d0","hook_event_name":"Notification","message":"Claude needs your permission","notification_type":"permission_prompt"}""";

        var calls = RunTranslator("Notification", payload, projectDir: @"C:\proj");

        Assert.IsEmpty(calls);
    }

    // ---- Stop -> ready --summary - --------------------------------------------

    [TestMethod]
    public void Stop_MapsTo_Ready_WithSummaryOnStdin()
    {
        // Real capture: session-cc-interactive-3.jsonl line 5.
        string payload = """{"session_id":"86297cc0-a3e5-43ec-9bf2-9a322a6aedd8","hook_event_name":"Stop","stop_hook_active":false,"last_assistant_message":"Why was the cat sitting on the computer?\n\nTo keep an eye on the mouse."}""";

        var calls = RunTranslator("Stop", payload, projectDir: @"C:\proj");

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "ready", "86297cc0-a3e5-43ec-9bf2-9a322a6aedd8", "--summary", "-", "--cwd", @"C:\proj" }, calls[0].Argv);
        Assert.AreEqual("Why was the cat sitting on the computer?\n\nTo keep an eye on the mouse.", calls[0].Stdin?.TrimEnd());
    }

    // ---- StopFailure -> broken --reason <map> --detail - ----------------------

    [TestMethod]
    public void StopFailure_RateLimit_MapsToMappedReasonToken()
    {
        // Synthetic: StopFailure was never captured live (phase 14: "Not exercised --
        // requires an API error, not induced"). Best-effort field reading --
        // flagged as an assumption in the executor report.
        string payload = """{"session_id":"sess-1","hook_event_name":"StopFailure","reason":"rate_limit","error":"429 Too Many Requests"}""";

        var calls = RunTranslator("StopFailure", payload, projectDir: null);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "broken", "sess-1", "--reason", "rate-limit", "--detail", "-" }, calls[0].Argv);
        Assert.AreEqual("429 Too Many Requests", calls[0].Stdin?.TrimEnd());
    }

    [TestMethod]
    public void StopFailure_UnknownReason_FallsBackToFatal()
    {
        string payload = """{"session_id":"sess-1","hook_event_name":"StopFailure","reason":"something_never_seen_before"}""";

        var calls = RunTranslator("StopFailure", payload, projectDir: null);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "broken", "sess-1", "--reason", "fatal" }, calls[0].Argv);
    }

    // ---- SubagentStart / SubagentStop -> agent-started / agent-stopped -------

    [TestMethod]
    public void SubagentStart_MapsTo_AgentStarted()
    {
        // Real capture: session-cc-20260712-212159.jsonl line 13.
        string payload = """{"session_id":"1d17eebc-3060-4494-8238-a22f4ac7bacb","hook_event_name":"SubagentStart","agent_id":"a98cf7c791c5ca991","agent_type":"general-purpose"}""";

        var calls = RunTranslator("SubagentStart", payload, projectDir: @"C:\proj");

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "agent-started", "1d17eebc-3060-4494-8238-a22f4ac7bacb", "--agent", "a98cf7c791c5ca991", "--name", "general-purpose", "--cwd", @"C:\proj" }, calls[0].Argv);
        Assert.IsTrue(string.IsNullOrWhiteSpace(calls[0].Stdin), $"Expected no meaningful stdin; PowerShell may still deliver a harmless trailing newline atv never reads (no flag requests it). Got: {calls[0].Stdin}");
    }

    [TestMethod]
    public void SubagentStop_MapsTo_AgentStopped()
    {
        // Real capture: session-cc-20260712-212159.jsonl line 16.
        string payload = """{"session_id":"1d17eebc-3060-4494-8238-a22f4ac7bacb","hook_event_name":"SubagentStop","agent_id":"a98cf7c791c5ca991","agent_type":"general-purpose","last_assistant_message":"aplha"}""";

        var calls = RunTranslator("SubagentStop", payload, projectDir: null);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "agent-stopped", "1d17eebc-3060-4494-8238-a22f4ac7bacb", "--agent", "a98cf7c791c5ca991" }, calls[0].Argv);
    }

    // ---- SessionEnd -> session-ended --reason finished -------------------------

    [TestMethod]
    public void SessionEnd_ReasonOther_MapsTo_SessionEndedFinished_NoCwd()
    {
        // Real capture: session-cc-20260712-212159.jsonl line 23 (-p one-shot exit).
        string payload = """{"session_id":"1d17eebc-3060-4494-8238-a22f4ac7bacb","hook_event_name":"SessionEnd","reason":"other"}""";

        var calls = RunTranslator("SessionEnd", payload, projectDir: @"C:\proj");

        Assert.HasCount(1, calls);
        // No --cwd: session-ended is the one verb with no identity flags and no upsert (SS2).
        CollectionAssert.AreEqual(new[] { "session-ended", "1d17eebc-3060-4494-8238-a22f4ac7bacb", "--reason", "finished" }, calls[0].Argv);
    }

    [TestMethod]
    public void SessionEnd_ReasonPromptInputExit_AlsoMapsTo_Finished()
    {
        // Real capture: session-cc-interactive-3.jsonl line 7 (/exit).
        string payload = """{"session_id":"86297cc0-a3e5-43ec-9bf2-9a322a6aedd8","hook_event_name":"SessionEnd","reason":"prompt_input_exit"}""";

        var calls = RunTranslator("SessionEnd", payload, projectDir: null);

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "session-ended", "86297cc0-a3e5-43ec-9bf2-9a322a6aedd8", "--reason", "finished" }, calls[0].Argv);
    }

    // ---- SessionStart -> nothing (except source=compact) -----------------------

    [TestMethod]
    public void SessionStart_SourceStartup_IsNoOp()
    {
        // Real capture: session-cc-20260712-212159.jsonl line 1.
        string payload = """{"session_id":"1d17eebc-3060-4494-8238-a22f4ac7bacb","hook_event_name":"SessionStart","source":"startup"}""";

        var calls = RunTranslator("SessionStart", payload, projectDir: @"C:\proj");

        Assert.IsEmpty(calls);
    }

    [TestMethod]
    public void SessionStart_SourceCompact_MapsTo_CompactingActivity()
    {
        // Synthetic: source=compact was never captured live (no /compact beat in
        // the phase-14 corpus). Per docs/integration-api.md's own optional row.
        string payload = """{"session_id":"sess-1","hook_event_name":"SessionStart","source":"compact"}""";

        var calls = RunTranslator("SessionStart", payload, projectDir: @"C:\proj");

        Assert.HasCount(1, calls);
        CollectionAssert.AreEqual(new[] { "activity", "sess-1", "--kind", "compacting", "--cwd", @"C:\proj" }, calls[0].Argv);
    }

    // ---- discipline 3: UTF-8 torture payload reaches the stub byte-intact -----

    [TestMethod]
    public void TortureUtf8Payload_ReachesStub_Intact()
    {
        // Non-ASCII (accented Latin, CJK, emoji surrogate pair), an embedded
        // double quote, and an embedded newline -- all inside one free-text field.
        const string original = "Fix the \"login\" bug -- café 字 test\nsecond line\nthird \U0001F600";
        string jsonEscaped = System.Text.Json.JsonSerializer.Serialize(original);
        string payload = $$"""{"session_id":"sess-1","hook_event_name":"UserPromptSubmit","prompt":{{jsonEscaped}}}""";

        var calls = RunTranslator("UserPromptSubmit", payload, projectDir: null);

        Assert.HasCount(1, calls);
        Assert.IsNotNull(calls[0].Stdin);
        // PowerShell's pipe-to-native-process mechanism appends a trailing
        // newline of its own (a documented, atv-tolerated artifact -- atv's
        // own stdin contract trims trailing whitespace, docs/integration-api.md
        // SS7); trim it here so the assertion isolates discipline 3/4's real
        // claim: every byte of the ORIGINAL text -- quotes, embedded newlines,
        // non-ASCII -- survives untouched.
        Assert.AreEqual(original, calls[0].Stdin!.TrimEnd('\r', '\n'));
    }

    // ---- discipline 4: payload fragments are never re-serialized --------------

    [TestMethod]
    public void ToolInputLabel_IsThePluckedStringVerbatim_NotAReserializedFragment()
    {
        // If translate.ps1 ever re-serialized tool_input (e.g. via ConvertTo-Json)
        // instead of plucking the raw decoded string, the label would come out
        // wrapped in JSON quotes / containing escape sequences. It must not.
        string payload = """{"session_id":"sess-1","hook_event_name":"PreToolUse","tool_name":"Read","tool_input":{"file_path":"C:\\a\\path with spaces\\and \"quotes\".txt"},"tool_use_id":"t1"}""";

        var calls = RunTranslator("PreToolUse", payload, projectDir: null);

        Assert.HasCount(1, calls);
        string? label = calls[0].Stdin?.TrimEnd();
        Assert.AreEqual("C:\\a\\path with spaces\\and \"quotes\".txt", label);
        Assert.DoesNotContain("\\\"", label!, "label must not carry JSON escape sequences from a re-serialized fragment");
    }

    // ---- FAIL-1: translate.ps1 always exits 0, even against a garbage payload -

    [TestMethod]
    public void MalformedPayload_NeverThrows_TranslatorStillExitsZero()
    {
        // RunTranslator() itself asserts exit code 0 (throws otherwise) -- reaching this
        // point at all is the assertion.
        var calls = RunTranslator("PreToolUse", "{ this is not valid json", projectDir: null);
        Assert.IsEmpty(calls);
    }

    [TestMethod]
    public void EmptyPayload_NeverThrows_TranslatorStillExitsZero()
    {
        var calls = RunTranslator("Stop", "", projectDir: null);
        Assert.IsEmpty(calls);
    }
}
