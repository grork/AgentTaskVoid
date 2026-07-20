using Codevoid.AgentTaskVoid.Watchdog;

namespace Codevoid.AgentTaskVoid.LogicTests.Watchdog;

/// <summary>
/// INFRA-21's test host: records that <see cref="Start"/> was called without
/// doing anything real (no process spawn, no thread) -- lets
/// <c>EnsureWatchdogTests</c>/<c>DispatcherHarness</c> assert WHICH host a
/// given mode selected, and simulate a spawn failure via
/// <see cref="OnStart"/> throwing.
/// </summary>
internal sealed class FakeWatchdogHost : IWatchdogHost
{
    public int StartCallCount { get; private set; }
    public Action? OnStart { get; set; }

    public void Start()
    {
        StartCallCount++;
        OnStart?.Invoke();
    }
}
