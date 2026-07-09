using System.Runtime.CompilerServices;

namespace Atv.AdapterTests;

/// <summary>
/// INFRA-19 ("Inner-loop watchdog suppression"): "real-adapter test runs set
/// watchdog-mode=off so no supervisor perturbs assertions." Nothing reads
/// <c>WATCHDOG_MODE</c> yet (the watchdog itself lands in phase 09) -- this mirrors
/// the same forward-compatible, currently-inert setup phase 01 already did for the
/// dev-loop launch profile (src/Atv/Properties/launchSettings.json).
///
/// A module initializer, not a <c>[TestInitialize]</c>/<c>[AssemblyInitialize]</c>
/// hook, so it also covers the module-initializer worker path
/// (<see cref="PeriodicWorkerEntryPoint"/>) and runs unconditionally before
/// Microsoft.Testing.Platform's own generated entry point does anything.
/// </summary>
internal static class AssemblySetup
{
    [ModuleInitializer]
    internal static void SuppressWatchdogForTestRuns()
    {
        // Never clobber an explicit override -- e.g. a future phase-09 ProcessHost
        // integration test in this same suite that deliberately wants spawn/inproc.
        if (Environment.GetEnvironmentVariable("WATCHDOG_MODE") is null)
            Environment.SetEnvironmentVariable("WATCHDOG_MODE", "off");
    }
}
