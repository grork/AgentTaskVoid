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

    internal static List<StubInvocation> RunTranslator(string eventName, string payloadJson, string? projectDir = null)
    {
        var arguments = new List<string> { "-Event", eventName };
        if (projectDir is not null)
        {
            arguments.Add("-ProjectDir");
            arguments.Add(projectDir);
        }

        return
        [
            .. IntegrationTranslatorProcess.Run(TranslatePath, arguments, payloadJson)
                .Select(call => new StubInvocation(call.Argv, call.Stdin)),
        ];
    }
}
