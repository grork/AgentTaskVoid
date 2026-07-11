using Atv.Watchdog;

namespace Atv.Cli.Verbs;

/// <summary>
/// The hidden <c>atv watchdog</c> verb (LIFE-17: "the same atv exe in a
/// hidden watchdog mode") -- <see cref="ProcessHost"/>'s real spawn target,
/// and INFRA-21's direct F5 target ("watchdog (foreground)" launch profile).
/// Deliberately NOT listed in <c>Program.cs</c>'s usage text and NOT routed
/// through <see cref="Dispatcher"/>/<see cref="Diagnostics.Posture"/> -- this
/// is a long-running blocking loop, not a single request/outcome verb.
/// </summary>
public static class WatchdogVerb
{
    public static int Run(GlobalOptions global)
    {
        if (global.WaitForDebugger)
            SpinUntilDebuggerAttached();

        RunContext ctx = CompositionRoot.BuildWatchdogRunContext(global);
        WatchdogLoop.Run(ctx);
        return 0;
    }

    private static void SpinUntilDebuggerAttached()
    {
        while (!System.Diagnostics.Debugger.IsAttached)
            Thread.Sleep(200);
    }
}
