using Atv.LogicTests.Store;
using Atv.Persistence;
using Atv.Store;

namespace Atv.LogicTests.Persistence;

/// <summary>
/// Covers phase-04 acceptance criterion 1: with the fake's interleave hook,
/// an unprotected concurrent read-modify-write shows deterministic loss; the
/// same sequence through <see cref="WriteGate"/> shows none. Abandoned-mutex
/// path proceeds and logs. Timeout path skips non-disruptively (no exception
/// escapes) in non-strict mode, and surfaces in strict mode.
/// </summary>
[TestClass]
public sealed class WriteGateTests
{
    private static readonly Uri DeepLink = new("https://example.invalid/deep-link");
    private static readonly Uri IconUri = new("ms-appx:///Assets/Square44x44Logo.png");

    // ---- Unprotected loss vs WriteGate-protected no-loss -----------------

    [TestMethod]
    public void Unprotected_InterleaveHook_LosesTheSecondWrite()
    {
        var store = new FakeAppTaskStore();
        AppTaskView? lost = null;
        store.InterleaveHook = () =>
        {
            store.InterleaveHook = null;
            lost = store.Create("B", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("b"));
        };

        var survivor = store.Create("A", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("a"));

        Assert.IsNotNull(lost);
        var all = store.FindAll();
        Assert.HasCount(1, all, "unprotected: B's create should be silently clobbered by A's stale whole-store write");
        Assert.AreEqual(survivor.Id, all[0].Id);
    }

    [TestMethod]
    public void WriteGate_Protected_BothWritersSurvive_NoLoss()
    {
        var store = new FakeAppTaskStore();
        using var mutex = new Mutex(initiallyOwned: false);
        var gateA = new WriteGate(mutex);
        var gateB = new WriteGate(mutex);

        AppTaskView? aResult = null;
        AppTaskView? bResult = null;
        Thread? bThread = null;

        // Fires INSIDE the fake's whole-store-commit chokepoint while gateA
        // still holds the real mutex. B's attempt to acquire the SAME mutex
        // on a separate thread must therefore BLOCK until A's
        // WriteGate.TryRun releases it -- so B's write can never land inside
        // A's stale snapshot window.
        store.InterleaveHook = () =>
        {
            store.InterleaveHook = null;
            bThread = new Thread(() =>
                gateB.TryRun(() =>
                    bResult = store.Create("B", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("b"))));
            bThread.Start();
            Thread.Sleep(100); // give B a real chance to reach WaitOne and block
        };

        bool ok = gateA.TryRun(() =>
            aResult = store.Create("A", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("a")));

        Assert.IsTrue(bThread!.Join(TimeSpan.FromSeconds(5)), "B's writer thread should complete once A releases the mutex");

        Assert.IsTrue(ok);
        Assert.IsNotNull(aResult);
        Assert.IsNotNull(bResult);
        Assert.HasCount(2, store.FindAll(), "WriteGate-protected: both A and B survive -- serialized, never interleaved");
    }

    // ---- Abandoned mutex ---------------------------------------------------

    [TestMethod]
    public void AbandonedMutex_ProceedsAndLogs()
    {
        using var mutex = new Mutex(initiallyOwned: false);
        var abandoner = new Thread(() => mutex.WaitOne()); // exits WITHOUT releasing
        abandoner.Start();
        abandoner.Join();

        var logs = new List<string>();
        var gate = new WriteGate(mutex, log: logs.Add);

        bool ran = false;
        bool ok = gate.TryRun(() => ran = true);

        Assert.IsTrue(ok, "an abandoned mutex must still be treated as acquired");
        Assert.IsTrue(ran);
        Assert.IsTrue(logs.Any(l => l.Contains("abandoned", StringComparison.OrdinalIgnoreCase)));
    }

    // ---- Timeout ------------------------------------------------------------

    [TestMethod]
    public void Timeout_NonStrict_SkipsNonDisruptively_NoExceptionEscapes_AndLogs()
    {
        using var mutex = new Mutex(initiallyOwned: false);
        using var holderReady = new ManualResetEventSlim(false);
        using var releaseHolder = new ManualResetEventSlim(false);
        var holder = StartHolderThread(mutex, holderReady, releaseHolder);

        try
        {
            var logs = new List<string>();
            var gate = new WriteGate(mutex, timeout: TimeSpan.FromMilliseconds(100), log: logs.Add);

            bool ran = false;
            bool ok = gate.TryRun(() => ran = true); // must not throw

            Assert.IsFalse(ok);
            Assert.IsFalse(ran, "the critical section must never run if the mutex wasn't acquired");
            Assert.IsTrue(logs.Any(l => l.Contains("timed out", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            releaseHolder.Set();
            holder.Join();
        }
    }

    [TestMethod]
    public void Timeout_Strict_Throws()
    {
        using var mutex = new Mutex(initiallyOwned: false);
        using var holderReady = new ManualResetEventSlim(false);
        using var releaseHolder = new ManualResetEventSlim(false);
        var holder = StartHolderThread(mutex, holderReady, releaseHolder);

        try
        {
            var gate = new WriteGate(mutex, timeout: TimeSpan.FromMilliseconds(100), strict: true);
            Assert.Throws<TimeoutException>(() => gate.TryRun(() => { }));
        }
        finally
        {
            releaseHolder.Set();
            holder.Join();
        }
    }

    [TestMethod]
    public void TryRunGeneric_ReturnsCriticalSectionResult()
    {
        using var mutex = new Mutex(initiallyOwned: false);
        var gate = new WriteGate(mutex);

        bool ok = gate.TryRun(() => 42, out int result);

        Assert.IsTrue(ok);
        Assert.AreEqual(42, result);
    }

    private static Thread StartHolderThread(Mutex mutex, ManualResetEventSlim ready, ManualResetEventSlim release)
    {
        var holder = new Thread(() =>
        {
            mutex.WaitOne();
            ready.Set();
            release.Wait();
            mutex.ReleaseMutex();
        })
        { IsBackground = true };
        holder.Start();
        ready.Wait();
        return holder;
    }
}
