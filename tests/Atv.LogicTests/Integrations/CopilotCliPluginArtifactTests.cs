using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codevoid.AgentTaskVoid.LogicTests.Integrations;

[TestClass]
public sealed class CopilotCliPluginArtifactTests
{
    private static readonly string[] ExpectedHookEvents =
    [
        "userPromptSubmitted",
        "preToolUse",
        "postToolUse",
        "notification",
        "agentStop",
        "preCompact",
        "errorOccurred",
        "sessionEnd",
    ];

    private static readonly HashSet<string> SemanticVerbs = new(StringComparer.Ordinal)
    {
        "working", "activity", "blocked", "ready", "broken", "agent-started", "agent-stopped", "session-ended",
    };

    private static string RepoRoot([CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AppTaskInfoCli.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException($"Could not locate repo root above '{here}'.");
    }

    private static string IntegrationRoot => Path.Combine(RepoRoot(), "integrations", "copilot-cli");
    private static string PluginRoot => Path.Combine(IntegrationRoot, "plugins", "atv-integration");
    private static string PluginPath => Path.Combine(PluginRoot, "plugin.json");
    private static string HooksPath => Path.Combine(PluginRoot, "hooks", "hooks.json");
    private static string TranslatorPath => Path.Combine(PluginRoot, "translate.ps1");
    private static string MapPath => Path.Combine(PluginRoot, "map.json");
    private static string MarketplacePath => Path.Combine(IntegrationRoot, ".github", "plugin", "marketplace.json");

    [TestMethod]
    public void PluginArtifactFiles_AllExist()
    {
        foreach (string path in new[] { PluginPath, HooksPath, TranslatorPath, MapPath, MarketplacePath })
            Assert.IsTrue(File.Exists(path), $"Expected Copilot plugin artifact '{path}'.");
    }

    [TestMethod]
    public void PluginAndMarketplace_AreWellFormedAndConnected()
    {
        using var plugin = JsonDocument.Parse(File.ReadAllText(PluginPath));
        Assert.AreEqual("atv-integration", plugin.RootElement.GetProperty("name").GetString());
        Assert.AreEqual("hooks/hooks.json", plugin.RootElement.GetProperty("hooks").GetString());

        using var marketplace = JsonDocument.Parse(File.ReadAllText(MarketplacePath));
        JsonElement entry = marketplace.RootElement.GetProperty("plugins").EnumerateArray().Single();
        Assert.AreEqual("atv-integration", entry.GetProperty("name").GetString());
        Assert.AreEqual("./plugins/atv-integration", entry.GetProperty("source").GetString());
    }

    [TestMethod]
    public void Hooks_UseTheMinimalVerifiedEventSet()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(HooksPath));
        Assert.AreEqual(1, doc.RootElement.GetProperty("version").GetInt32());
        JsonElement hooks = doc.RootElement.GetProperty("hooks");

        string[] actual = [.. hooks.EnumerateObject().Select(p => p.Name)];
        CollectionAssert.AreEquivalent(ExpectedHookEvents, actual);
        Assert.IsFalse(hooks.TryGetProperty("permissionRequest", out _), "permissionRequest is pre-service and must not be mapped directly to Blocked.");
        Assert.IsFalse(hooks.TryGetProperty("subagentStart", out _), "Task tool events own uniquely-addressable lifecycle.");
        Assert.IsFalse(hooks.TryGetProperty("subagentStop", out _), "Raw subagentStop lacks the task instance id.");
        Assert.IsFalse(hooks.TryGetProperty("postToolUseFailure", out _), "No verified mapping requires the extra synchronous hook.");
    }

    [TestMethod]
    public void Hooks_InvokeOnlyTheBundledTranslator_AndNeverStrict()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(HooksPath));
        foreach (JsonProperty eventProperty in doc.RootElement.GetProperty("hooks").EnumerateObject())
        {
            JsonElement[] lines = [.. eventProperty.Value.EnumerateArray()];
            Assert.HasCount(1, lines, $"{eventProperty.Name}: expected one hook line.");
            JsonElement line = lines[0];
            Assert.AreEqual("command", line.GetProperty("type").GetString());
            Assert.AreEqual(10, line.GetProperty("timeoutSec").GetInt32());
            Assert.IsFalse(line.TryGetProperty("async", out _), "Copilot command hooks are synchronous.");

            string command = line.GetProperty("powershell").GetString()!;
            Assert.Contains("$env:COPILOT_PLUGIN_ROOT", command);
            Assert.Contains("translate.ps1", command);
            Assert.Contains($"-Event '{eventProperty.Name}'", command);
            Assert.DoesNotContain("--strict", command);
        }

        string raw = File.ReadAllText(HooksPath);
        Assert.DoesNotContain("--strict", raw);
    }

    [TestMethod]
    public void PostToolUse_IsRestrictedToEventsWithRequiredCompletionSemantics()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(HooksPath));
        JsonElement line = doc.RootElement.GetProperty("hooks").GetProperty("postToolUse").EnumerateArray().Single();
        Assert.AreEqual("ask_user|task", line.GetProperty("matcher").GetString());
    }

    [TestMethod]
    public void Translator_ParsesUnderWindowsPowerShell51()
    {
        string checkScript = Path.Combine(Path.GetTempPath(), "atv-copilot-parse-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(checkScript,
            "param([string]$Target)\n" +
            "$e = $null\n" +
            "[void][System.Management.Automation.Language.Parser]::ParseFile($Target, [ref]$null, [ref]$e)\n" +
            "if ($e.Count -gt 0) { $e | ForEach-Object { $_.Message }; exit 1 } else { exit 0 }\n");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(checkScript);
            psi.ArgumentList.Add("-Target");
            psi.ArgumentList.Add(TranslatorPath);

            using var process = System.Diagnostics.Process.Start(psi)!;
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            Assert.IsTrue(process.WaitForExit(30_000), "PowerShell parse check timed out.");
            Assert.AreEqual(0, process.ExitCode, $"{stdout} {stderr}");
        }
        finally
        {
            try { File.Delete(checkScript); } catch { }
        }
    }

    [TestMethod]
    public void Translator_OnlyInvokesV2Verbs_AndNeverReadsCopilotTranscripts()
    {
        string content = File.ReadAllText(TranslatorPath);
        var matches = Regex.Matches(content, @"@\(""([a-z-]+)"",\s*(?:\$sessionId|\$target\.handle|\$correlation\.parentSession|\$ParentSession)");
        Assert.IsNotEmpty(matches, "Expected semantic verb invocations; extraction regex may be stale.");

        string[] unknown = [.. matches.Select(m => m.Groups[1].Value).Distinct().Where(v => !SemanticVerbs.Contains(v))];
        Assert.IsEmpty(unknown, $"Translator invokes verbs outside the v2 surface: {string.Join(", ", unknown)}");

        Assert.DoesNotContain("\"--strict\"", content);
        Assert.DoesNotContain("session-state", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("events.jsonl", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".copilot", content, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void MapJson_IsWellFormed()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(MapPath));
        Assert.IsTrue(doc.RootElement.TryGetProperty("toolKind", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("toolLabelField", out _));
    }
}
