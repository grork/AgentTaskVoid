using Atv.Semantics;

namespace Atv.LogicTests.Semantics;

/// <summary>AC7's closed-vocabulary parsing (ERGO-31 §2): every documented token parses to its kind, and the vocabulary is genuinely CLOSED (an arbitrary/unmapped token is rejected, never silently accepted).</summary>
[TestClass]
public sealed class ActivityKindTests
{
    [TestMethod]
    [DataRow("read", ActivityKind.Read)]
    [DataRow("edit", ActivityKind.Edit)]
    [DataRow("write", ActivityKind.Write)]
    [DataRow("search", ActivityKind.Search)]
    [DataRow("shell", ActivityKind.Shell)]
    [DataRow("fetch", ActivityKind.Fetch)]
    [DataRow("web-search", ActivityKind.WebSearch)]
    [DataRow("plan", ActivityKind.Plan)]
    [DataRow("compacting", ActivityKind.Compacting)]
    [DataRow("tool", ActivityKind.Tool)]
    public void TryParse_EachDocumentedToken_ParsesToItsKind(string token, ActivityKind expected)
    {
        Assert.IsTrue(ActivityKinds.TryParse(token, out ActivityKind kind));
        Assert.AreEqual(expected, kind);
    }

    [TestMethod]
    [DataRow("delegate")]
    [DataRow("progress")]
    [DataRow("find")]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("Read")] // case-sensitive -- host-translator-constant strings, not user input.
    public void TryParse_UnmappedOrWrongCaseToken_Rejected(string? token)
    {
        Assert.IsFalse(ActivityKinds.TryParse(token, out _));
    }

    [TestMethod]
    public void VerbWord_CoversEveryVerbWordKind()
    {
        Assert.AreEqual("Reading", ActivityKinds.VerbWord(ActivityKind.Read));
        Assert.AreEqual("Editing", ActivityKinds.VerbWord(ActivityKind.Edit));
        Assert.AreEqual("Writing", ActivityKinds.VerbWord(ActivityKind.Write));
        Assert.AreEqual("Searching", ActivityKinds.VerbWord(ActivityKind.Search));
        Assert.AreEqual("Running", ActivityKinds.VerbWord(ActivityKind.Shell));
        Assert.AreEqual("Fetching", ActivityKinds.VerbWord(ActivityKind.Fetch));
        Assert.AreEqual("Searching the web", ActivityKinds.VerbWord(ActivityKind.WebSearch));
    }
}

/// <summary>AC7's closed-vocabulary parsing (ERGO-31 §3): <c>broken --reason</c>.</summary>
[TestClass]
public sealed class BrokenReasonTests
{
    [TestMethod]
    [DataRow("rate-limit", BrokenReasonToken.RateLimit, "Rate limited")]
    [DataRow("overloaded", BrokenReasonToken.Overloaded, "Overloaded")]
    [DataRow("api-error", BrokenReasonToken.ApiError, "API error")]
    [DataRow("timeout", BrokenReasonToken.Timeout, "Timed out")]
    [DataRow("fatal", BrokenReasonToken.Fatal, "Failed")]
    public void TryParse_AndRender_EachDocumentedToken(string token, BrokenReasonToken expectedToken, string expectedRendered)
    {
        Assert.IsTrue(BrokenReasons.TryParse(token, out BrokenReasonToken reason));
        Assert.AreEqual(expectedToken, reason);
        Assert.AreEqual(expectedRendered, BrokenReasons.Render(reason));
    }

    [TestMethod]
    [DataRow("network-error")]
    [DataRow("")]
    [DataRow(null)]
    public void TryParse_UnmappedToken_Rejected(string? token)
    {
        Assert.IsFalse(BrokenReasons.TryParse(token, out _));
    }
}

/// <summary>AC7's closed-vocabulary parsing (ERGO-31 §3): <c>session-ended --reason</c> -- token-only, no free-text.</summary>
[TestClass]
public sealed class SessionEndedReasonTests
{
    [TestMethod]
    [DataRow("finished", SessionEndedReasonToken.Finished)]
    [DataRow("error", SessionEndedReasonToken.Error)]
    public void TryParse_EachDocumentedToken_ParsesToItsReason(string token, SessionEndedReasonToken expected)
    {
        Assert.IsTrue(SessionEndedReasons.TryParse(token, out SessionEndedReasonToken reason));
        Assert.AreEqual(expected, reason);
    }

    [TestMethod]
    [DataRow("aborted")]
    [DataRow("")]
    [DataRow(null)]
    public void TryParse_UnmappedToken_Rejected(string? token)
    {
        Assert.IsFalse(SessionEndedReasons.TryParse(token, out _));
    }
}

/// <summary>ERGO-31 §2's rendering: verb-word-prefixed activity lines, the three special-shaped kinds (plan/compacting/tool), and the MCP tool-name prettifier.</summary>
[TestClass]
public sealed class RenderingTests
{
    [TestMethod]
    public void BuildActivityLine_ReadKind_PrefixesVerbWordOntoLabel()
    {
        Assert.AreEqual("Reading auth.ts", Rendering.BuildActivityLine(ActivityKind.Read, "auth.ts", name: null));
    }

    [TestMethod]
    public void BuildActivityLine_ShellKind_PrefixesRunningOntoLabel()
    {
        Assert.AreEqual("Running npm test", Rendering.BuildActivityLine(ActivityKind.Shell, "npm test", name: null));
    }

    [TestMethod]
    public void BuildActivityLine_PlanKind_UsesLabelAsIs_NoVerbWordPrefix()
    {
        Assert.AreEqual("(3/7) Write tests", Rendering.BuildActivityLine(ActivityKind.Plan, "(3/7) Write tests", name: null));
    }

    [TestMethod]
    public void BuildActivityLine_CompactingKind_AlwaysFixedPhrase_IgnoresLabel()
    {
        Assert.AreEqual(Rendering.CompactingLine, Rendering.BuildActivityLine(ActivityKind.Compacting, "irrelevant label", name: null));
        Assert.AreEqual(Rendering.CompactingLine, Rendering.BuildActivityLine(ActivityKind.Compacting, null, name: null));
    }

    [TestMethod]
    public void BuildActivityLine_ToolKind_McpName_PrettifiedServerAndTool_PlusLabel()
    {
        string line = Rendering.BuildActivityLine(ActivityKind.Tool, "Create authentication bug ticket", name: "mcp__jira__create_ticket");
        Assert.AreEqual("Jira: Create Ticket: Create authentication bug ticket", line);
    }

    [TestMethod]
    public void BuildActivityLine_ToolKind_NonMcpName_PrettifiedAsSingleToken()
    {
        string line = Rendering.BuildActivityLine(ActivityKind.Tool, "", name: "my_custom_tool");
        Assert.AreEqual("My Custom Tool", line);
    }

    [TestMethod]
    public void BuildActivityLine_ToolKind_NoNameNoLabel_DegradesGracefully()
    {
        Assert.AreEqual("Running a tool", Rendering.BuildActivityLine(ActivityKind.Tool, null, null));
    }

    [TestMethod]
    public void BuildActivityLine_ToolKind_NoName_UsesLabelAlone()
    {
        Assert.AreEqual("Custom tool call", Rendering.BuildActivityLine(ActivityKind.Tool, "Custom tool call", null));
    }

    [TestMethod]
    public void PrettifyToolName_McpPattern_SplitsServerAndTool()
    {
        Assert.AreEqual("Jira: Create Ticket", Rendering.PrettifyToolName("mcp__jira__create_ticket"));
    }

    [TestMethod]
    public void PrettifyToolName_NonMcpName_SingleTokenPrettified()
    {
        Assert.AreEqual("Some Tool Name", Rendering.PrettifyToolName("some-tool-name"));
    }
}
