using Codevoid.AgentTaskVoid.Run;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Run;

/// <summary>
/// AC2's core: the debounced updater. All assertions go through
/// <see cref="RunTestHarness.Store"/>'s <c>UpdateCallCount</c> (a REAL
/// content-write counter, not a mock expectation) to structurally prove
/// coalescing -- a burst of many <see cref="StepPublisher.Ingest"/> calls
/// between two <see cref="StepPublisher.Tick"/>s must cost exactly one
/// content write, and an unchanged buffer must cost zero.
/// </summary>
[TestClass]
public sealed class StepPublisherTests
{
    private const string Handle = "run-test-handle";

    [TestMethod]
    public void Tick_NewLinesSinceLastPublish_WritesWholeBufferExactlyOnce()
    {
        using var h = new RunTestHarness();
        h.StartCard(Handle, RunTestHarness.Now);
        var publisher = new StepPublisher(h.Ops, Handle, TimeSpan.FromMinutes(5), RunTestHarness.Now);

        publisher.Ingest("line 1");
        publisher.Ingest("line 2");
        publisher.Ingest("line 3");
        publisher.Tick(RunTestHarness.Now.AddSeconds(1));

        Assert.AreEqual(1, h.Store.UpdateCallCount);
        AppTaskView view = FindByHandle(h, Handle);
        CollectionAssert.AreEqual(new[] { "line 1", "line 2" }, view.CompletedSteps.ToArray());
        Assert.AreEqual("line 3", view.ExecutingStep);
        Assert.AreEqual(AppTaskState.Running, view.State);
    }

    [TestMethod]
    public void Tick_BurstOfManyLinesBetweenTwoTicks_ProducesExactlyOneUpdate()
    {
        using var h = new RunTestHarness();
        h.StartCard(Handle, RunTestHarness.Now);
        var publisher = new StepPublisher(h.Ops, Handle, TimeSpan.FromMinutes(5), RunTestHarness.Now);
        int baselineUpdates = h.Store.UpdateCallCount;

        for (int i = 0; i < 100; i++)
            publisher.Ingest($"line {i}");

        publisher.Tick(RunTestHarness.Now.AddSeconds(1));

        Assert.AreEqual(baselineUpdates + 1, h.Store.UpdateCallCount);
    }

    [TestMethod]
    public void Ingest_CapsAtTenLines_OldestDropped()
    {
        using var h = new RunTestHarness();
        h.StartCard(Handle, RunTestHarness.Now);
        var publisher = new StepPublisher(h.Ops, Handle, TimeSpan.FromMinutes(5), RunTestHarness.Now);

        for (int i = 1; i <= 15; i++)
            publisher.Ingest($"line {i}");
        publisher.Tick(RunTestHarness.Now.AddSeconds(1));

        AppTaskView view = FindByHandle(h, Handle);
        // Last 10 lines total: "line 6".."line 15" -- 9 completed + 1 executing.
        CollectionAssert.AreEqual(
            new[] { "line 6", "line 7", "line 8", "line 9", "line 10", "line 11", "line 12", "line 13", "line 14" },
            view.CompletedSteps.ToArray());
        Assert.AreEqual("line 15", view.ExecutingStep);
    }

    [TestMethod]
    public void Tick_NoChangeSincePreviousTick_NoAdditionalUpdateCall()
    {
        using var h = new RunTestHarness();
        h.StartCard(Handle, RunTestHarness.Now);
        var publisher = new StepPublisher(h.Ops, Handle, TimeSpan.FromMinutes(5), RunTestHarness.Now);

        publisher.Ingest("only line");
        publisher.Tick(RunTestHarness.Now.AddSeconds(1));
        int afterFirstTick = h.Store.UpdateCallCount;

        // Second tick, nothing ingested since, and well within the keepalive window.
        publisher.Tick(RunTestHarness.Now.AddSeconds(2));

        Assert.AreEqual(afterFirstTick, h.Store.UpdateCallCount);
    }

    [TestMethod]
    public void Tick_SilentStretchPastKeepAliveInterval_TouchesLastUpdateWithNoContentWrite()
    {
        using var h = new RunTestHarness();
        DateTimeOffset start = RunTestHarness.Now;
        h.StartCard(Handle, start);
        var keepAlive = TimeSpan.FromSeconds(30);
        var publisher = new StepPublisher(h.Ops, Handle, keepAlive, start);

        publisher.Ingest("only line");
        publisher.Tick(start.AddSeconds(1));
        int updatesAfterContentTick = h.Store.UpdateCallCount;
        DateTimeOffset lastUpdateAfterContentTick = h.Sidecar.Read(Handle)!.LastUpdate;

        // No new Ingest -- a silent stretch past the keepalive interval.
        DateTimeOffset keepAliveTickTime = start.AddSeconds(1) + keepAlive + TimeSpan.FromSeconds(1);
        publisher.Tick(keepAliveTickTime);

        Assert.AreEqual(updatesAfterContentTick, h.Store.UpdateCallCount, "keepalive must NOT touch store content.");
        DateTimeOffset lastUpdateAfterKeepAlive = h.Sidecar.Read(Handle)!.LastUpdate;
        Assert.IsTrue(lastUpdateAfterKeepAlive > lastUpdateAfterContentTick, "sidecar lastUpdate must be refreshed by the keepalive tick.");
        Assert.AreEqual(keepAliveTickTime, lastUpdateAfterKeepAlive);
    }

    [TestMethod]
    public void Tick_SilentStretchUnderKeepAliveInterval_NoTouchAtAll()
    {
        using var h = new RunTestHarness();
        DateTimeOffset start = RunTestHarness.Now;
        h.StartCard(Handle, start);
        var keepAlive = TimeSpan.FromMinutes(5);
        var publisher = new StepPublisher(h.Ops, Handle, keepAlive, start);

        publisher.Ingest("only line");
        publisher.Tick(start.AddSeconds(1));
        DateTimeOffset lastUpdateAfterContentTick = h.Sidecar.Read(Handle)!.LastUpdate;

        publisher.Tick(start.AddSeconds(2)); // well under the 5-minute keepalive interval

        Assert.AreEqual(lastUpdateAfterContentTick, h.Sidecar.Read(Handle)!.LastUpdate);
    }

    [TestMethod]
    public void FlushFinal_WritesEvenWithoutATickHavingRun()
    {
        using var h = new RunTestHarness();
        h.StartCard(Handle, RunTestHarness.Now);
        var publisher = new StepPublisher(h.Ops, Handle, TimeSpan.FromMinutes(5), RunTestHarness.Now);

        publisher.Ingest("last line before exit");
        publisher.FlushFinal(RunTestHarness.Now.AddSeconds(1));

        Assert.AreEqual(1, h.Store.UpdateCallCount);
        Assert.AreEqual("last line before exit", FindByHandle(h, Handle).ExecutingStep);
    }

    [TestMethod]
    public void FlushFinal_EmptyBuffer_NoWrite()
    {
        using var h = new RunTestHarness();
        h.StartCard(Handle, RunTestHarness.Now);
        var publisher = new StepPublisher(h.Ops, Handle, TimeSpan.FromMinutes(5), RunTestHarness.Now);

        publisher.FlushFinal(RunTestHarness.Now.AddSeconds(1));

        Assert.AreEqual(0, h.Store.UpdateCallCount);
    }

    [TestMethod]
    public void RunLoop_TicksUntilStopRequested_ThenStops()
    {
        using var h = new RunTestHarness();
        h.StartCard(Handle, RunTestHarness.Now);
        var publisher = new StepPublisher(h.Ops, Handle, TimeSpan.FromMinutes(5), RunTestHarness.Now);
        publisher.Ingest("one line");

        int sleepCalls = 0;
        DateTimeOffset clock = RunTestHarness.Now;
        bool stop = false;

        publisher.RunLoop(
            clock: () => clock,
            sleep: _ => { sleepCalls++; if (sleepCalls >= 3) stop = true; },
            interval: TimeSpan.FromMilliseconds(1),
            shouldStop: () => stop);

        Assert.AreEqual(3, sleepCalls);
        Assert.AreEqual(1, h.Store.UpdateCallCount); // the one real content change, published on the first tick.
    }

    private static AppTaskView FindByHandle(RunTestHarness h, string handle)
    {
        string id = h.Sidecar.Read(handle)!.Id;
        return h.Fake.Find(id)!;
    }
}
