namespace Codevoid.AgentTaskVoid.LogicTests.Integrations;

internal static class CopilotCliTranslatorHarness
{
    internal sealed record StubInvocation(string[] Argv, string? Stdin);

    internal static string RepoRoot() => IntegrationTranslatorProcess.RepoRoot();

    internal static string PluginRoot =>
        Path.Combine(RepoRoot(), "integrations", "copilot-cli", "plugins", "atv-integration");

    internal static string TranslatePath => Path.Combine(PluginRoot, "translate.ps1");

    internal static string EnsureStubBuilt() => IntegrationTranslatorProcess.EnsureStubBuilt();

    internal static List<StubInvocation> RunTranslator(
        string eventName,
        string payloadJson,
        string stateDirectory,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var mergedEnvironment = new Dictionary<string, string?>
        {
            ["ATV_COPILOT_STATE_DIR"] = stateDirectory,
            ["COPILOT_PLUGIN_DATA"] = null,
            ["CLAUDE_PLUGIN_DATA"] = null,
        };
        if (environment is not null)
        {
            foreach ((string key, string? value) in environment)
                mergedEnvironment[key] = value;
        }

        return
        [
            .. IntegrationTranslatorProcess.Run(
                    TranslatePath,
                    ["-Event", eventName],
                    payloadJson,
                    mergedEnvironment)
                .Select(call => new StubInvocation(call.Argv, call.Stdin)),
        ];
    }
}
