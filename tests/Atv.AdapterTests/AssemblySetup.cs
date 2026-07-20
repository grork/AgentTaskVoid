using System.Runtime.CompilerServices;
using Codevoid.AgentTaskVoid.Config;

namespace Codevoid.AgentTaskVoid.AdapterTests;

/// <summary>
/// INFRA-19 ("Inner-loop watchdog suppression"): "real-adapter test runs set
/// watchdog-mode=off so no supervisor perturbs assertions."
///
/// Phase-09 fix: the env var <see cref="SettingsLoader"/> actually resolves is
/// brand-derived (<see cref="SettingsLoader.CurrentEnvVarName"/> ->
/// <c>ATV_WATCHDOG_MODE</c>), not the bare <c>WATCHDOG_MODE</c> this file
/// originally set at phase 03 (when nothing read it yet, so the mismatch was
/// harmless -- see the same fix applied to
/// <c>src/Atv/Properties/launchSettings.json</c>). Now that phase 09 wires
/// real spawn/inproc hosts behind that setting, the correct name matters: this
/// suite must not have every real-adapter test run silently spawn a real
/// detached watchdog because of a stale env var key.
///
/// A module initializer, not a <c>[TestInitialize]</c>/<c>[AssemblyInitialize]</c>
/// hook, so it also covers the module-initializer worker paths
/// (<see cref="PeriodicWorkerEntryPoint"/>, <see cref="WatchdogWorkerEntryPoint"/>)
/// and runs unconditionally before Microsoft.Testing.Platform's own generated
/// entry point does anything.
/// </summary>
internal static class AssemblySetup
{
    [ModuleInitializer]
    internal static void SuppressWatchdogForTestRuns()
    {
        string envVarName = SettingsLoader.CurrentEnvVarName("watchdog-mode");

        // Never clobber an explicit override -- e.g. WatchdogProcessHostTests
        // deliberately sets ATV_WATCHDOG_MODE for its own spawned child.
        if (Environment.GetEnvironmentVariable(envVarName) is null)
            Environment.SetEnvironmentVariable(envVarName, "off");
    }
}
