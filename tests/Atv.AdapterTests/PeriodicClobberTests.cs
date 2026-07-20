using System.Diagnostics;
using System.Runtime.CompilerServices;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.AdapterTests;

/// <summary>
/// The demoted periodic reality-check (INFRA-9, INFRA-15): confirms the real platform
/// still clobbers concurrent unlocked writes as docs/windows-ui-shell-tasks/README.md's
/// "Concurrency: writes are not serialized across processes" and
/// docs/testing/fake-fidelity-promises.md's promise 1 describe -- the one thing that
/// proves the phase-04 <c>WriteGate</c> mutex mitigation is protecting against a REAL
/// platform behavior, not a fake-only artifact.
///
/// EXCLUDED FROM DEFAULT RUNS by design, not by test-runner filter syntax: MSTest has
/// no direct equivalent to NUnit's <c>[Explicit]</c>, so the gate is a plain
/// environment-variable check in the test body (see <see cref="RunPeriodicEnvVar"/>) --
/// deterministic regardless of which MTP/`dotnet test` invocation style is used to run
/// it. <c>[TestCategory("Periodic")]</c> is also present for discoverability/filtering
/// convenience, but the actual exclusion mechanism is the env-var-gated
/// <c>Assert.Inconclusive</c> below. A plain `dotnet test` therefore reports this test
/// Inconclusive (not Failed, not silently absent) unless a human deliberately opts in.
///
/// Heavy and slow relative to the rest of this suite (400 real platform writes across
/// 4 real processes) -- exactly why it is periodic, not a gate (AC7).
/// </summary>
[TestClass]
public sealed class PeriodicClobberTests
{
    private const string RunPeriodicEnvVar = "ATV_RUN_PERIODIC";
    private const string WorkerCreateCountEnvVar = "ATV_PERIODIC_WORKER_CREATE_COUNT";
    private const int ProcessCount = 4;
    private const int CreatesPerProcess = 100;

    private IAppTaskStore Store { get; set; } = null!;

    [TestInitialize]
    public void BeforeEachTest()
    {
        IdentityGate.AssertIdentityOrSkip();
        Store = new AppTaskStore();
        IdentityGate.AssertApiSupportedOrSkip(Store);
        ClearAllTasks(Store);
    }

    [TestCleanup]
    public void AfterEachTest()
    {
        if (Store is not null)
            ClearAllTasks(Store);
    }

    [TestMethod]
    [TestCategory("Periodic")]
    public void FourProcesses_100CreatesEach_Unlocked_ReproducesLastWriterWinsLoss()
    {
        if (Environment.GetEnvironmentVariable(RunPeriodicEnvVar) != "1")
        {
            Assert.Inconclusive(
                $"Periodic reality-check (INFRA-9/INFRA-15), excluded from default runs by " +
                $"design -- set {RunPeriodicEnvVar}=1 and re-run to exercise it manually. " +
                "See docs/testing/fake-fidelity-promises.md promise 1.");
        }

        // Environment.ProcessPath is THIS process's own launch path -- by the time this
        // line runs, IdentityGate has already confirmed this process carries identity,
        // which (per build/Atv.TestIdentity.targets' AtvEnsureTestIdentity) means
        // Environment.ProcessPath necessarily IS the hard link to the registered
        // AppExecutionAlias stub. Reusing that exact path to launch worker children
        // gives them the identical identity, hence the identical tasks.json.
        string? launcherPath = Environment.ProcessPath;
        Assert.IsFalse(string.IsNullOrEmpty(launcherPath), "Environment.ProcessPath was unexpectedly empty.");
        Assert.IsTrue(File.Exists(launcherPath),
            $"Expected this process's own launch path ('{launcherPath}') to exist on disk.");

        var workers = new Process[ProcessCount];
        for (int i = 0; i < ProcessCount; i++)
        {
            var psi = new ProcessStartInfo
            {
                FileName = launcherPath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.EnvironmentVariables[WorkerCreateCountEnvVar] = CreatesPerProcess.ToString();
            workers[i] = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start periodic worker #{i}.");
        }

        foreach (var worker in workers)
        {
            worker.WaitForExit();
            Assert.AreEqual(0, worker.ExitCode, $"Periodic worker process {worker.Id} did not exit cleanly.");
        }

        int survivingCount = Store.FindAll().Count;
        int expectedTotal = ProcessCount * CreatesPerProcess;

        Console.WriteLine($"Unlocked concurrent creates: kept {survivingCount}/{expectedTotal}.");
        Assert.IsLessThan(expectedTotal, survivingCount,
            $"Without a cross-process write mutex, the real platform's whole-store clobber " +
            $"(docs/windows-ui-shell-tasks/README.md, 'Concurrency') should lose some writes " +
            $"-- got {survivingCount}/{expectedTotal}. If this now holds all {expectedTotal}, " +
            "either the platform started serializing writes itself, or something upstream is " +
            "unexpectedly protecting this path.");
    }

    private static void ClearAllTasks(IAppTaskStore store)
    {
        foreach (var task in store.FindAll())
            store.Remove(task.Id);
    }
}

/// <summary>
/// Lets tests/Atv.AdapterTests.exe double as a disposable "create N tasks, then exit"
/// worker process for <see cref="PeriodicClobberTests"/>, without fighting
/// Microsoft.Testing.Platform's own generated entry point (which owns <c>Main</c> and
/// its argv). A module initializer runs before any of that -- checked once per
/// process start, effectively free when the trigger env var isn't set, which is every
/// other test run in this suite.
/// </summary>
internal static class PeriodicWorkerEntryPoint
{
    [ModuleInitializer]
    internal static void RunAsPeriodicWorkerIfRequested()
    {
        string? raw = Environment.GetEnvironmentVariable("ATV_PERIODIC_WORKER_CREATE_COUNT");
        if (raw is null || !int.TryParse(raw, out int count))
            return;

        var store = new AppTaskStore();
        var deepLink = new Uri("https://example.com/periodic-worker");
        var iconUri = new Uri("ms-appx:///Assets/Square44x44Logo.png");
        int pid = Environment.ProcessId;

        for (int i = 0; i < count; i++)
        {
            store.Create($"periodic-{pid}-{i}", "", deepLink, iconUri, new AppTaskContentDto.SequenceOfSteps([], "x"));
        }

        Environment.Exit(0);
    }
}
