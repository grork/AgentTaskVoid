using System.Runtime.CompilerServices;
using Atv.Config;
using Atv.Persistence;
using Atv.Store;
using Atv.Watchdog;

namespace Atv.AdapterTests;

/// <summary>
/// INFRA-21's REQUIRED integration test: "a real detached process spawns,
/// acquires the single-instance mutex, survives parent exit, and self-exits
/// when the supervised set empties" -- the one thing unit tests (FakeHost +
/// fake clock) structurally cannot exercise, since <see cref="ProcessHost"/>
/// itself is the thin, deliberately-unabstracted seam onto real
/// <see cref="System.Diagnostics.Process"/> mechanics. Without this test the
/// process mechanics rot silently, since the dev inner loop runs
/// <c>ATV_WATCHDOG_MODE=off</c> and never spawns anything for real.
///
/// Spawns a REAL COPY of THIS test exe (<see cref="Environment.ProcessPath"/>,
/// exactly as production <see cref="Atv.Cli.CompositionRoot"/> does for the
/// real <c>atv.exe</c>) -- which, per <c>build/Atv.TestIdentity.targets</c>,
/// carries the IDENTICAL per-worktree test package identity as this process,
/// so the spawned child observes the exact same <c>tasks.json</c> through the
/// real <see cref="AppTaskStore"/>. <see cref="WatchdogWorkerEntryPoint"/>'s
/// module initializer (same established pattern as
/// <see cref="PeriodicWorkerEntryPoint"/>, phase 03) intercepts before
/// Microsoft.Testing.Platform's own generated entry point ever runs, so the
/// spawned child runs the REAL <see cref="WatchdogLoop.Run"/> instead of the
/// test runner.
/// </summary>
[TestClass]
public sealed class WatchdogProcessHostTests
{
    private const string ChildRootEnvVar = "ATV_TEST_WATCHDOG_CHILD";
    private const string ChildIdleSecondsEnvVar = "ATV_TEST_WATCHDOG_IDLE_SECONDS";
    private const string ChildPollMillisecondsEnvVar = "ATV_TEST_WATCHDOG_POLL_MS";

    private IAppTaskStore _store = null!;
    private string _tempRoot = null!;

    [TestInitialize]
    public void BeforeEachTest()
    {
        IdentityGate.AssertIdentityOrSkip();
        _store = new AppTaskStore();
        IdentityGate.AssertApiSupportedOrSkip(_store);
        ClearAllTasks(_store);

        _tempRoot = Path.Combine(Path.GetTempPath(), "atv-watchdog-processhost-tests", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void AfterEachTest()
    {
        if (_store is not null)
            ClearAllTasks(_store);

        if (_tempRoot is not null && Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [TestMethod]
    public void ProcessHost_SpawnsRealDetachedWatchdog_AcquiresMutex_SurvivesParent_ExpiresIdleTask_ThenSelfExits()
    {
        // Arrange: a real card whose sidecar lastUpdate is already stale
        // relative to a config-pinned 1-second idle threshold, so the
        // spawned watchdog's very first tick expires it.
        var created = _store.Create(
            "watchdog-e2e", "sub",
            new Uri("https://example.com/watchdog-e2e"),
            new Uri("ms-appx:///Assets/Square44x44Logo.png"),
            new AppTaskContentDto.SequenceOfSteps([], "step"));

        Directory.CreateDirectory(_tempRoot);
        var sidecar = new SidecarStore(Path.Combine(_tempRoot, "sidecar"));
        var recycleBin = new RecycleBin(Path.Combine(_tempRoot, "recycle-bin"));
        DateTimeOffset staleTime = DateTimeOffset.Now.AddSeconds(-5);
        sidecar.Write("watchdog-e2e", created.Id, staleTime);

        string mutexName = AppPaths.BuildWatchdogMutexName(Windows.ApplicationModel.Package.Current.Id.FamilyName);
        Assert.IsFalse(MutexExists(mutexName), "sanity: no watchdog should be running before the spawn.");

        // Act: a REAL detached ProcessHost spawn -- exactly LIFE-17's production
        // mechanics (windowless, CreateNoWindow, no tracked/awaited handle).
        var extraEnv = new Dictionary<string, string>
        {
            [ChildRootEnvVar] = _tempRoot,
            [ChildIdleSecondsEnvVar] = "1",
            [ChildPollMillisecondsEnvVar] = "200",
        };
        var logs = new List<string>();
        var host = new ProcessHost(Environment.ProcessPath!, ["watchdog"], logs.Add, extraEnv);

        host.Start();

        // Assert: LIFE-18 single-instance mutex goes live shortly after spawn.
        Assert.IsTrue(
            SpinWaitUntil(() => MutexExists(mutexName), TimeSpan.FromSeconds(10)),
            "the spawned watchdog must acquire its single-instance mutex.");

        // "Survives parent exit": this test method never tracks/waits on the
        // child's process handle (ProcessHost is fire-and-forget by design) --
        // everything below happens purely because the DETACHED child kept
        // running and did real work entirely on its own.

        // Assert: the real watchdog tick expires the idle task.
        Assert.IsTrue(
            SpinWaitUntil(() => _store.Find(created.Id) is null, TimeSpan.FromSeconds(10)),
            "the real detached watchdog must expire the idle task on its own.");

        var record = recycleBin.TryResurrect("watchdog-e2e", DateTimeOffset.Now, TimeSpan.FromDays(1));
        Assert.IsNotNull(record, "expiry must have written a recycle-bin tombstone.");
        Assert.AreEqual("watchdog-e2e", record!.Handle);

        // Assert: self-exit on an empty supervised set (LIFE-19) -- the
        // single-instance mutex is eventually released.
        Assert.IsTrue(
            SpinWaitUntil(() => !MutexExists(mutexName), TimeSpan.FromSeconds(15)),
            "the watchdog must self-exit (releasing its mutex) once its supervised set is empty.");
    }

    private static bool MutexExists(string name)
    {
        try { using var m = Mutex.OpenExisting(name); return true; }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }

    private static bool SpinWaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > timeout) return false;
            Thread.Sleep(100);
        }
        return true;
    }

    private static void ClearAllTasks(IAppTaskStore store)
    {
        foreach (var task in store.FindAll())
            store.Remove(task.Id);
    }
}

/// <summary>
/// Lets <c>Atv.AdapterTests.exe</c> double as a real, standalone
/// <see cref="WatchdogLoop.Run"/> worker process for
/// <see cref="WatchdogProcessHostTests"/>, without fighting
/// Microsoft.Testing.Platform's own generated entry point -- same established
/// pattern as <see cref="PeriodicWorkerEntryPoint"/> (phase 03). Checked once
/// per process start via env vars <see cref="ProcessHost"/> threads through
/// (rather than CLI args, which the real <c>atv watchdog</c> verb uses --
/// this test exe's <c>Main</c> belongs to MTP, not <c>Atv.Cli</c>, so argv
/// isn't a usable channel here).
/// </summary>
internal static class WatchdogWorkerEntryPoint
{
    private const string ChildRootEnvVar = "ATV_TEST_WATCHDOG_CHILD";
    private const string ChildIdleSecondsEnvVar = "ATV_TEST_WATCHDOG_IDLE_SECONDS";
    private const string ChildPollMillisecondsEnvVar = "ATV_TEST_WATCHDOG_POLL_MS";

    [ModuleInitializer]
    internal static void RunAsWatchdogWorkerIfRequested()
    {
        string? root = Environment.GetEnvironmentVariable(ChildRootEnvVar);
        if (root is null)
            return;

        int idleSeconds = int.TryParse(Environment.GetEnvironmentVariable(ChildIdleSecondsEnvVar), out int i) ? i : 1;
        int pollMs = int.TryParse(Environment.GetEnvironmentVariable(ChildPollMillisecondsEnvVar), out int p) ? p : 200;

        var store = new AppTaskStore();
        var sidecar = new SidecarStore(Path.Combine(root, "sidecar"));
        var recycleBin = new RecycleBin(Path.Combine(root, "recycle-bin"));

        // Unnamed -- this worker is the only writer in this test scenario (the
        // parent test method only reads via Find/FindAll), so there is no
        // cross-process write serialization to prove here (that's INFRA-6's
        // own, separately-covered concern).
        var gate = new WriteGate(new Mutex(initiallyOwned: false));

        Settings settings = Settings.Default with
        {
            IdleRunning = TimeSpan.FromSeconds(idleSeconds),
            IdlePaused = TimeSpan.FromSeconds(idleSeconds),
            IdleNeedsAttention = TimeSpan.FromSeconds(idleSeconds),
            IdleCompleted = TimeSpan.FromSeconds(idleSeconds),
            WatchdogPollInterval = TimeSpan.FromMilliseconds(pollMs),
        };

        var deps = new WatchdogDeps(
            store, sidecar, recycleBin, gate, Icons: null,
            Log: msg => Console.Error.WriteLine($"[watchdog-worker] {msg}"),
            Clock: () => DateTimeOffset.Now,
            settings);

        string mutexName = AppPaths.BuildWatchdogMutexName(Windows.ApplicationModel.Package.Current.Id.FamilyName);
        Mutex instanceMutex;
        try { instanceMutex = new Mutex(initiallyOwned: false, mutexName); }
        catch { instanceMutex = new Mutex(initiallyOwned: false); }

        var ctx = new RunContext(deps, instanceMutex, Thread.Sleep, EnableStartupTask: () => { }, DisableStartupTask: () => { });

        WatchdogLoop.Run(ctx);
        Environment.Exit(0);
    }
}
