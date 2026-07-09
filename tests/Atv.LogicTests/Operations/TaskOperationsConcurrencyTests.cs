using Atv.Operations;

namespace Atv.LogicTests.Operations;

/// <summary>
/// Covers phase-05 acceptance criterion 7: every <see cref="TaskOperations"/>
/// verb acquires <see cref="Atv.Persistence.WriteGate"/> exactly once around
/// its full read-modify-write. Proven the same way phase 04's
/// <c>WriteGateTests</c> proves it for the raw gate: the fake's
/// <see cref="Atv.LogicTests.Store.FakeAppTaskStore.InterleaveHook"/> fires
/// INSIDE writer A's critical section and spawns writer B on another thread
/// through a SEPARATE <see cref="TaskOperations"/> instance sharing the same
/// underlying <see cref="Mutex"/> -- B can only proceed once A's
/// <c>TaskOperations</c> call (not just the raw store write) has released the
/// gate, so both writes must survive.
/// </summary>
[TestClass]
public sealed class TaskOperationsConcurrencyTests
{
    [TestMethod]
    public void TwoConcurrentStarts_ThroughTaskOperations_BothSurvive_NoLostWrite()
    {
        using var h = new OperationsHarness();
        var opsB = h.NewOpsOnSameMutex();

        OperationOutcome? outcomeB = null;
        Thread? threadB = null;

        h.Store.InterleaveHook = () =>
        {
            h.Store.InterleaveHook = null; // don't recurse -- B's own create must commit atomically
            threadB = new Thread(() =>
                outcomeB = opsB.Start("h2", "B", "", OperationsHarness.IconUri, OperationsHarness.DeepLink, OperationsHarness.Now));
            threadB.Start();
            Thread.Sleep(150); // give B a real chance to reach WaitOne and block on A's still-held mutex
        };

        var outcomeA = h.Ops.Start("h1", "A", "", OperationsHarness.IconUri, OperationsHarness.DeepLink, OperationsHarness.Now);

        Assert.IsTrue(threadB!.Join(TimeSpan.FromSeconds(5)), "B's TaskOperations call should complete once A's gate releases");

        Assert.AreEqual(OutcomeKind.Accepted, outcomeA.Kind);
        Assert.IsNotNull(outcomeB);
        Assert.AreEqual(OutcomeKind.Accepted, outcomeB!.Kind);

        Assert.HasCount(2, h.Store.FindAll(), "both A's and B's creates must survive -- TaskOperations serializes its whole RMW behind one WriteGate acquisition, never two separate ones");
        Assert.IsNotNull(h.Sidecar.Read("h1"));
        Assert.IsNotNull(h.Sidecar.Read("h2"));
    }

    [TestMethod]
    public void TwoConcurrentSteps_OnDifferentHandles_ThroughTaskOperations_BothSurvive()
    {
        using var h = new OperationsHarness();
        h.StartNew("h1");
        h.StartNew("h2");
        var opsB = h.NewOpsOnSameMutex();

        OperationOutcome? outcomeB = null;
        Thread? threadB = null;

        h.Store.InterleaveHook = () =>
        {
            h.Store.InterleaveHook = null;
            threadB = new Thread(() => outcomeB = opsB.Step("h2", "b-step", OperationsHarness.Now));
            threadB.Start();
            Thread.Sleep(150);
        };

        var outcomeA = h.Ops.Step("h1", "a-step", OperationsHarness.Now);

        Assert.IsTrue(threadB!.Join(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(OutcomeKind.Accepted, outcomeA.Kind);
        Assert.AreEqual(OutcomeKind.Accepted, outcomeB!.Kind);

        var all = h.Store.FindAll();
        Assert.IsTrue(all.Any(t => t.ExecutingStep == "a-step"), "A's step must not have been clobbered by B's interleaved write");
        Assert.IsTrue(all.Any(t => t.ExecutingStep == "b-step"), "B's step must not have been clobbered by A's write");
    }
}
