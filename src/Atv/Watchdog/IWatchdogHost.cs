namespace Codevoid.AgentTaskVoid.Watchdog;

/// <summary>
/// INFRA-21's hosting seam: the ONLY thing that differs by
/// <see cref="Codevoid.AgentTaskVoid.Config.WatchdogMode"/> is the ACT of getting
/// <see cref="WatchdogLoop.Run"/> running somewhere -- never the logic
/// itself. <see cref="ProcessHost"/> spawns a detached windowless process
/// (LIFE-17, production default); <see cref="InProcThreadHost"/> runs the
/// same loop on a background thread bound to the invoking process's lifetime
/// (dev/debug only, NOT a production-supervision equivalent). Tests use a
/// fake (<c>tests/Atv.LogicTests/Watchdog/FakeWatchdogHost.cs</c>) that
/// records the call without doing anything real.
///
/// Fire-and-forget by contract: <see cref="Start"/> returns as soon as the
/// watchdog has been HANDED OFF to wherever it will run (process spawned /
/// thread started) -- it never blocks waiting for the watchdog itself to do
/// anything, and (FAIL-1) never lets an exception escape past
/// <see cref="EnsureWatchdog.Run"/>'s own try/catch around the call.
/// </summary>
public interface IWatchdogHost
{
    void Start();
}
