using Codevoid.AgentTaskVoid.Operations;

namespace Codevoid.AgentTaskVoid.LogicTests.Semantics;

/// <summary>
/// Phase-15 counterpart of phase-05's <c>TaskOperationsConcurrencyTests</c>
/// (AC7 in that phase's numbering; carried here as the same structural
/// guarantee for the v2 engine): every <see cref="Codevoid.AgentTaskVoid.Semantics.SemanticEngine"/>
/// verb acquires the <see cref="Codevoid.AgentTaskVoid.Persistence.WriteGate"/> exactly once
/// around its full read-modify-write, proven by having the fake's
/// <c>InterleaveHook</c> fire INSIDE writer A's critical section and spawn
/// writer B on another thread through a SEPARATE engine instance sharing the
/// same underlying <see cref="Mutex"/> -- B can only proceed once A's whole
/// engine call (not just the raw store write) has released the gate.
/// </summary>
[TestClass]
public sealed class SemanticEngineConcurrencyTests
{
    [TestMethod]
    public void TwoConcurrentWorkingCreates_ThroughSemanticEngine_BothSurvive_NoLostWrite()
    {
        using var h = new SemanticEngineHarness();
        var engineB = h.NewEngineOnSameMutex();

        OperationOutcome? outcomeB = null;
        Thread? threadB = null;

        h.Store.InterleaveHook = () =>
        {
            h.Store.InterleaveHook = null; // don't recurse -- B's own create must commit atomically
            threadB = new Thread(() =>
                outcomeB = engineB.Working("h2", "B", "", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "goal-b", SemanticEngineHarness.Now));
            threadB.Start();
            Thread.Sleep(150); // give B a real chance to reach WaitOne and block on A's still-held mutex
        };

        var outcomeA = h.Engine.Working("h1", "A", "", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink, "goal-a", SemanticEngineHarness.Now);

        Assert.IsTrue(threadB!.Join(TimeSpan.FromSeconds(5)), "B's SemanticEngine call should complete once A's gate releases");

        Assert.AreEqual(OutcomeKind.Accepted, outcomeA.Kind);
        Assert.IsNotNull(outcomeB);
        Assert.AreEqual(OutcomeKind.Accepted, outcomeB!.Kind);

        Assert.HasCount(2, h.Store.FindAll(), "both A's and B's creates must survive -- SemanticEngine serializes its whole RMW behind one WriteGate acquisition, never two separate ones");
        Assert.IsNotNull(h.Sidecar.Read("h1"));
        Assert.IsNotNull(h.Sidecar.Read("h2"));
    }

    [TestMethod]
    public void TwoConcurrentActivities_OnDifferentHandles_ThroughSemanticEngine_BothSurvive()
    {
        using var h = new SemanticEngineHarness();
        h.WorkingNew("h1");
        h.WorkingNew("h2");
        var engineB = h.NewEngineOnSameMutex();

        OperationOutcome? outcomeB = null;
        Thread? threadB = null;

        h.Store.InterleaveHook = () =>
        {
            h.Store.InterleaveHook = null;
            threadB = new Thread(() => outcomeB = engineB.Activity("h2", "B", "", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink,
                Codevoid.AgentTaskVoid.Semantics.ActivityKind.Shell, "b-step", null, null, SemanticEngineHarness.Now));
            threadB.Start();
            Thread.Sleep(150);
        };

        var outcomeA = h.Engine.Activity("h1", "A", "", SemanticEngineHarness.IconUri, SemanticEngineHarness.DeepLink,
            Codevoid.AgentTaskVoid.Semantics.ActivityKind.Shell, "a-step", null, null, SemanticEngineHarness.Now);

        Assert.IsTrue(threadB!.Join(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(OutcomeKind.Accepted, outcomeA.Kind);
        Assert.AreEqual(OutcomeKind.Accepted, outcomeB!.Kind);

        var all = h.Store.FindAll();
        Assert.IsTrue(all.Any(t => t.ExecutingStep == "Running a-step"), "A's step must not have been clobbered by B's interleaved write");
        Assert.IsTrue(all.Any(t => t.ExecutingStep == "Running b-step"), "B's step must not have been clobbered by A's write");
    }
}
