using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HostEventRecorder.Tests;

[TestClass]
public sealed class CopilotCliArtifactTests
{
    private static readonly string[] ExpectedEvents =
    [
        "sessionStart",
        "userPromptSubmitted",
        "userPromptTransformed",
        "preToolUse",
        "postToolUse",
        "postToolUseFailure",
        "permissionRequest",
        "notification",
        "agentStop",
        "subagentStart",
        "subagentStop",
        "errorOccurred",
        "preCompact",
        "sessionEnd",
    ];

    private static string RepoRoot([CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AppTaskInfoCli.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException($"Could not locate repo root above '{here}'.");
    }

    private static string HostRoot =>
        Path.Combine(RepoRoot(), "tools", "host-event-recorder", "hosts", "copilot-cli");

    [TestMethod]
    public void CopilotCaptureArtifacts_AllExist()
    {
        foreach (string relativePath in new[]
        {
            "plugin.template.json",
            "hooks.template.json",
            "stage.ps1",
            "driver-scripted.ps1",
            "cue-script.ps1",
        })
        {
            Assert.IsTrue(File.Exists(Path.Combine(HostRoot, relativePath)), $"Expected Copilot capture artifact '{relativePath}'.");
        }

        Assert.IsTrue(File.Exists(Path.Combine(RepoRoot(), "docs", "host-events", "copilot-cli.md")));
    }

    [TestMethod]
    public void PluginTemplate_PointsAtHooksFile()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(HostRoot, "plugin.template.json")));
        Assert.AreEqual("atv-copilot-hostrec", doc.RootElement.GetProperty("name").GetString());
        Assert.AreEqual("hooks/hooks.json", doc.RootElement.GetProperty("hooks").GetString());
    }

    [TestMethod]
    public void HooksTemplate_CampsExactlyTheDocumentedCopilotEvents()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(HostRoot, "hooks.template.json")));
        Assert.AreEqual(1, doc.RootElement.GetProperty("version").GetInt32());

        JsonElement hooks = doc.RootElement.GetProperty("hooks");
        string[] actualEvents = [.. hooks.EnumerateObject().Select(p => p.Name)];
        CollectionAssert.AreEquivalent(ExpectedEvents, actualEvents);
    }

    [TestMethod]
    public void HooksTemplate_UsesDirectExecAndEmitsNoDecisionConfiguration()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(HostRoot, "hooks.template.json")));
        JsonElement hooks = doc.RootElement.GetProperty("hooks");

        foreach (JsonProperty eventProperty in hooks.EnumerateObject())
        {
            JsonElement[] hookLines = [.. eventProperty.Value.EnumerateArray()];
            Assert.HasCount(1, hookLines, $"{eventProperty.Name}: expected one hook line.");
            JsonElement hookLine = hookLines[0];

            Assert.AreEqual("command", hookLine.GetProperty("type").GetString());
            Assert.AreEqual(
                $"& '__RECORDER_EXE_PATH__' --host copilot-cli --event {eventProperty.Name}",
                hookLine.GetProperty("powershell").GetString());
            Assert.AreEqual(10, hookLine.GetProperty("timeoutSec").GetInt32());
            Assert.IsFalse(hookLine.TryGetProperty("async", out _), $"{eventProperty.Name}: Copilot command hooks are synchronous; async must not be copied from Claude Code.");
            Assert.IsFalse(hookLine.TryGetProperty("bash", out _), $"{eventProperty.Name}: capture is a Windows-local direct executable.");
            Assert.IsFalse(hookLine.TryGetProperty("command", out _), $"{eventProperty.Name}: use Copilot's confirmed native flat PowerShell form, not the ignored nested command/args form.");
            Assert.IsFalse(hookLine.TryGetProperty("args", out _), $"{eventProperty.Name}: Copilot 1.0.71 ignored the Open-Plugin nested args array during the conduit probe.");
        }
    }

    [TestMethod]
    public void StagingAndDriver_AreIsolatedAndPromptModeAware()
    {
        string stage = File.ReadAllText(Path.Combine(HostRoot, "stage.ps1"));
        Assert.Contains("--plugin-dir", stage);
        Assert.Contains("never reads or changes", stage);
        Assert.DoesNotContain("Remove-Item", stage, "The stage step must not delete or replace the caller's scratch repository.");

        string driver = File.ReadAllText(Path.Combine(HostRoot, "driver-scripted.ps1"));
        Assert.Contains("GITHUB_COPILOT_PROMPT_MODE_EXTENSIONS", driver);
        Assert.Contains("--plugin-dir", driver);
        Assert.Contains("--allow-all", driver);
    }
}
