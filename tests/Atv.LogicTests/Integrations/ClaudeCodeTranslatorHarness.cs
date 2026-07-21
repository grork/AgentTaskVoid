namespace Codevoid.AgentTaskVoid.LogicTests.Integrations;

/// <summary>
/// Claude-Code-specific wrapper over <see cref="IntegrationTranslatorProcess"/>.
/// Keeps the phase-18 test API stable while sharing the real PowerShell/stub-atv
/// process plumbing with later host integrations.
/// </summary>
internal static class ClaudeCodeTranslatorHarness
{
    internal sealed record StubInvocation(string[] Argv, string? Stdin);

    internal static string RepoRoot() => IntegrationTranslatorProcess.RepoRoot();

    internal static string TranslatePath =>
        Path.Combine(RepoRoot(), "integrations", "claude-code", "plugins", "atv-integration", "translate.ps1");

    internal static string PluginRoot =>
        Path.Combine(RepoRoot(), "integrations", "claude-code", "plugins", "atv-integration");

    internal static string EnsureStubBuilt() => IntegrationTranslatorProcess.EnsureStubBuilt();

    internal static List<StubInvocation> RunTranslator(
        string eventName,
        string payloadJson,
        string? projectDir = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var arguments = new List<string> { "-Event", eventName };
        if (projectDir is not null)
        {
            arguments.Add("-ProjectDir");
            arguments.Add(projectDir);
        }

        return
        [
            .. IntegrationTranslatorProcess.Run(TranslatePath, arguments, payloadJson, environment)
                .Select(call => new StubInvocation(call.Argv, call.Stdin)),
        ];
    }

    /// <summary>
    /// Populates <paramref name="destinationDir"/> with a copy of translate.ps1 +
    /// map.json (the script loads map.json from $PSScriptRoot, so the two must
    /// travel together). Phase 20's atv-command.txt precedence tests run from a
    /// per-test temp copy rather than the shared working-tree plugin dir, so
    /// dropping an override file never pollutes it or races parallel tests.
    /// </summary>
    internal static void PopulatePluginCopy(string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        File.Copy(TranslatePath, Path.Combine(destinationDir, "translate.ps1"), overwrite: true);
        File.Copy(Path.Combine(PluginRoot, "map.json"), Path.Combine(destinationDir, "map.json"), overwrite: true);
    }

    internal static List<StubInvocation> RunTranslatorAt(
        string pluginDir,
        string eventName,
        string payloadJson,
        string? projectDir = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var arguments = new List<string> { "-Event", eventName };
        if (projectDir is not null)
        {
            arguments.Add("-ProjectDir");
            arguments.Add(projectDir);
        }

        string scriptPath = Path.Combine(pluginDir, "translate.ps1");
        return
        [
            .. IntegrationTranslatorProcess.Run(scriptPath, arguments, payloadJson, environment)
                .Select(call => new StubInvocation(call.Argv, call.Stdin)),
        ];
    }
}
