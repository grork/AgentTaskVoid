using Atv.Config;
using Atv.Watchdog;

namespace Atv.LogicTests.Watchdog;

/// <summary>AC2: <see cref="EnsureWatchdog.Run"/>'s decide-to-spawn matrix -- mode x mutex-liveness -> spawn / inproc / nothing; spawn failure logs and continues.</summary>
[TestClass]
public sealed class EnsureWatchdogTests
{
    private static string FreshMutexName() => $@"Local\atv-tests-{Guid.NewGuid():N}-watchdog";

    [TestMethod]
    public void Mode_Off_NeverStartsEitherHost_NoLog()
    {
        var logs = new List<string>();
        var processHost = new FakeWatchdogHost();
        var inProcHost = new FakeWatchdogHost();

        EnsureWatchdog.Run(WatchdogMode.Off, FreshMutexName(), processHost, inProcHost, logs.Add);

        Assert.AreEqual(0, processHost.StartCallCount);
        Assert.AreEqual(0, inProcHost.StartCallCount);
        Assert.IsEmpty(logs);
    }

    [TestMethod]
    public void Mode_Spawn_MutexNotLive_StartsProcessHostOnly()
    {
        var logs = new List<string>();
        var processHost = new FakeWatchdogHost();
        var inProcHost = new FakeWatchdogHost();

        EnsureWatchdog.Run(WatchdogMode.Spawn, FreshMutexName(), processHost, inProcHost, logs.Add);

        Assert.AreEqual(1, processHost.StartCallCount);
        Assert.AreEqual(0, inProcHost.StartCallCount);
    }

    [TestMethod]
    public void Mode_InProc_MutexNotLive_StartsInProcHostOnly()
    {
        var logs = new List<string>();
        var processHost = new FakeWatchdogHost();
        var inProcHost = new FakeWatchdogHost();

        EnsureWatchdog.Run(WatchdogMode.InProc, FreshMutexName(), processHost, inProcHost, logs.Add);

        Assert.AreEqual(0, processHost.StartCallCount);
        Assert.AreEqual(1, inProcHost.StartCallCount);
    }

    [TestMethod]
    public void Mode_Spawn_MutexAlreadyLive_StartsNeitherHost_NoLog()
    {
        var logs = new List<string>();
        var processHost = new FakeWatchdogHost();
        var inProcHost = new FakeWatchdogHost();
        string name = FreshMutexName();
        using var live = new Mutex(initiallyOwned: false, name);

        EnsureWatchdog.Run(WatchdogMode.Spawn, name, processHost, inProcHost, logs.Add);

        Assert.AreEqual(0, processHost.StartCallCount);
        Assert.AreEqual(0, inProcHost.StartCallCount);
        Assert.IsEmpty(logs, "a live watchdog mutex means nothing to do -- no spawn attempt, no log line.");
    }

    [TestMethod]
    public void Mode_InProc_MutexAlreadyLive_StartsNeitherHost()
    {
        var logs = new List<string>();
        var processHost = new FakeWatchdogHost();
        var inProcHost = new FakeWatchdogHost();
        string name = FreshMutexName();
        using var live = new Mutex(initiallyOwned: false, name);

        EnsureWatchdog.Run(WatchdogMode.InProc, name, processHost, inProcHost, logs.Add);

        Assert.AreEqual(0, processHost.StartCallCount);
        Assert.AreEqual(0, inProcHost.StartCallCount);
    }

    [TestMethod]
    public void SpawnFailure_IsLogged_AndDoesNotThrow()
    {
        var logs = new List<string>();
        var processHost = new FakeWatchdogHost { OnStart = () => throw new InvalidOperationException("boom") };
        var inProcHost = new FakeWatchdogHost();

        EnsureWatchdog.Run(WatchdogMode.Spawn, FreshMutexName(), processHost, inProcHost, logs.Add);

        Assert.AreEqual(1, processHost.StartCallCount, "Start was attempted despite the eventual throw.");
        Assert.IsTrue(logs.Any(l => l.Contains("failed to start", StringComparison.Ordinal)));
    }

}
