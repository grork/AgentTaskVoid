using Atv.Cli;
using Atv.Config;

namespace Atv.LogicTests.Cli;

/// <summary>
/// LIFE-17/INFRA-19's mode-gated liveness check: THIS phase leaves the real
/// spawn/inproc hosts stubbed (phase 09), but the gate + the OpenMutex
/// liveness probe are real and must never throw/block a verb.
/// </summary>
[TestClass]
public sealed class WatchdogGateTests
{
    [TestMethod]
    public void Ensure_ModeOff_DoesNothing_NoLog()
    {
        var logs = new List<string>();
        string name = $@"Local\atv-tests-{Guid.NewGuid():N}-watchdog";

        WatchdogGate.Ensure(WatchdogMode.Off, name, logs.Add);

        Assert.IsEmpty(logs);
    }

    [TestMethod]
    public void Ensure_ModeSpawn_NoExistingMutex_LogsStubMessage_DoesNotThrow()
    {
        var logs = new List<string>();
        string name = $@"Local\atv-tests-{Guid.NewGuid():N}-watchdog";

        WatchdogGate.Ensure(WatchdogMode.Spawn, name, logs.Add);

        Assert.HasCount(1, logs);
    }

    [TestMethod]
    public void Ensure_ModeInProc_NoExistingMutex_LogsStubMessage()
    {
        var logs = new List<string>();
        string name = $@"Local\atv-tests-{Guid.NewGuid():N}-watchdog";

        WatchdogGate.Ensure(WatchdogMode.InProc, name, logs.Add);

        Assert.HasCount(1, logs);
    }

    [TestMethod]
    public void Ensure_ExistingMutex_ShortCircuits_NoLog()
    {
        var logs = new List<string>();
        string name = $@"Local\atv-tests-{Guid.NewGuid():N}-watchdog";
        using var live = new Mutex(initiallyOwned: false, name);

        WatchdogGate.Ensure(WatchdogMode.Spawn, name, logs.Add);

        Assert.IsEmpty(logs, "a live watchdog mutex means nothing to do -- no spawn attempt, no log line.");
    }

    [TestMethod]
    public void Ensure_NullMutexNameOrLog_Throws()
    {
        Assert.Throws<ArgumentException>(() => WatchdogGate.Ensure(WatchdogMode.Spawn, "", _ => { }));
        Assert.Throws<ArgumentNullException>(() => WatchdogGate.Ensure(WatchdogMode.Spawn, "name", null!));
    }
}
