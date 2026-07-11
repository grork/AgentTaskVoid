using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Atv.LogicTests.Integrations;

/// <summary>
/// Phase 13 (optional, per the phase file's "§D") automated teeth for AC1/AC4
/// on the shipped Claude Code hook artifact
/// (<c>integrations/claude-code/settings.hooks.json</c>): parses the file as
/// JSON (well-formed), then greps every embedded PowerShell hook `command`
/// string for `atv &lt;verb&gt;` invocations and asserts (a) every invoked
/// verb is in the real host-agnostic surface
/// (<c>src/Atv/Cli/Dispatcher.cs</c>'s routed verbs) and (b) no hook line ever
/// passes `--strict` (plan/README.md standing invariant #4's non-disruptive
/// posture is load-bearing for a host that fails closed on a nonzero hook
/// exit -- Copilot CLI's preToolUse, not currently shipped, but the same rule
/// applies to every artifact). Guards against the artifact silently drifting
/// from the verb surface as the CLI evolves.
/// </summary>
[TestClass]
public sealed class ClaudeCodeArtifactTests
{
    /// <summary>
    /// The real verb set <c>Atv.Cli.Dispatcher.Run</c> routes to (lifecycle +
    /// utility verbs), plus <c>run</c> (routed via <c>RunVerb</c>). Hidden
    /// <c>watchdog</c> is deliberately excluded -- no host-integration artifact
    /// should ever invoke it directly.
    /// </summary>
    private static readonly HashSet<string> RealVerbs = new(StringComparer.Ordinal)
    {
        "start", "step", "state", "attention", "done", "fail", "remove",
        "list", "clear", "doctor", "run",
    };

    /// <summary>Matches an actual invocation (<c>&amp; atv &lt;verb&gt;</c>) -- deliberately requires the call-operator prefix so a bare mention like <c>Get-Command atv</c> (the artifact's own install guard) is never mistaken for a verb call.</summary>
    private static readonly Regex AtvInvocation = new(@"&\s+atv\s+(\S+)", RegexOptions.Compiled);

    [TestMethod]
    public void ClaudeCodeArtifact_IsWellFormedJson_WithExpectedHooksShape()
    {
        string path = ArtifactPath();
        Assert.IsTrue(File.Exists(path), $"Expected the Claude Code hook artifact at {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.AreEqual(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.IsTrue(doc.RootElement.TryGetProperty("hooks", out var hooks), "Expected a top-level 'hooks' object.");
        Assert.AreEqual(JsonValueKind.Object, hooks.ValueKind);

        // The five Claude Code events this artifact wires (LIFE-10 mapping):
        // SessionStart -> start, PostToolUse -> state+step, Notification ->
        // attention, Stop -> done, SessionEnd -> remove.
        string[] expectedEvents = ["SessionStart", "PostToolUse", "Notification", "Stop", "SessionEnd"];
        foreach (string evt in expectedEvents)
            Assert.IsTrue(hooks.TryGetProperty(evt, out _), $"Expected a '{evt}' hook entry in the artifact.");
    }

    [TestMethod]
    public void ClaudeCodeArtifact_EveryInvokedVerb_IsInTheRealVerbSet()
    {
        string content = File.ReadAllText(ArtifactPath());
        var invokedVerbs = AtvInvocation.Matches(content).Select(m => m.Groups[1].Value).ToArray();

        Assert.IsNotEmpty(invokedVerbs, "Expected at least one '& atv <verb>' invocation in the artifact -- the extraction regex may be stale.");

        var unknown = invokedVerbs.Where(v => !RealVerbs.Contains(v)).ToArray();
        Assert.IsEmpty(unknown, $"Artifact invokes verb(s) not in the real Dispatcher-routed surface: {string.Join(", ", unknown)}");
    }

    [TestMethod]
    public void ClaudeCodeArtifact_InvokesTheExpectedVerbSet()
    {
        // Pins the LIFE-10 mapping this artifact implements: start (SessionStart),
        // state + step (PostToolUse, chained -- the phase-05 state-reset-after-
        // attention caveat), attention (Notification), done (Stop), remove
        // (SessionEnd). A change here should be a deliberate mapping edit, not
        // an accidental drop.
        string content = File.ReadAllText(ArtifactPath());
        var invokedVerbs = AtvInvocation.Matches(content).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(
            new[] { "start", "state", "step", "attention", "done", "remove" },
            invokedVerbs.ToArray());
    }

    [TestMethod]
    public void ClaudeCodeArtifact_NeverPassesStrict()
    {
        // Non-disruptive posture (plan/README.md standing invariant #4) is
        // load-bearing for any host hook: --strict would turn a routine
        // refusal/miss into a nonzero exit, which a fail-closed hook host
        // could treat as "block". This artifact must never opt into it.
        string content = File.ReadAllText(ArtifactPath());
        Assert.IsFalse(content.Contains("--strict", StringComparison.Ordinal), "Artifact must never pass --strict in a hook line.");
    }

    private static string ArtifactPath([CallerFilePath] string here = "")
        => Path.Combine(RepoRoot(here), "integrations", "claude-code", "settings.hooks.json");

    private static string RepoRoot(string here)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AppTaskInfoCli.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException($"Could not locate the repo root (AppTaskInfoCli.slnx) above {here}");

        return dir.FullName;
    }
}
