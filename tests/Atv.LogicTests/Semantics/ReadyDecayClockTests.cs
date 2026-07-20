using Codevoid.AgentTaskVoid.Persistence;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Semantics;

/// <summary>
/// AC5's engine-side coverage: LIFE-24 §6's rule that ONLY a genuine
/// transition INTO Ready starts the decay clock -- re-asserting an
/// already-held Ready never restarts it. What happens to an already-started
/// clock (accrual/demotion) is <c>Codevoid.AgentTaskVoid.Watchdog.ReadyDecayPassTests</c>'s job;
/// this file only covers the engine's own start/preserve/clear bookkeeping,
/// read back through the sidecar's <see cref="EngineMemory.ReadyDecay"/>.
/// </summary>
[TestClass]
public sealed class ReadyDecayClockTests
{
    private static readonly Uri Icon = SemanticEngineHarness.IconUri;
    private static readonly Uri Link = SemanticEngineHarness.DeepLink;
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    private static ReadyDecayState? ReadClock(SemanticEngineHarness h, string handle)
        => h.Sidecar.Read(handle)!.EngineMemory!.ReadyDecay;

    [TestMethod]
    public void Ready_FromWorking_StartsTheClockFresh_AtThisCallsTimestamp()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);

        h.Engine.Ready("h1", "T", "S", Icon, Link, null, Now.AddMinutes(1));

        var decay = ReadClock(h, "h1");
        Assert.IsNotNull(decay);
        Assert.AreEqual(Now.AddMinutes(1), decay!.LastSampledAt);
        Assert.AreEqual(TimeSpan.Zero, decay.AccruedPresentTime);
    }

    [TestMethod]
    public void Ready_OnABrandNewNeverSeenHandle_StillStartsTheClock()
    {
        // `ready` can be the very first semantic verb call for a handle (every
        // v2 verb upserts) -- the clock must still start in that case.
        using var h = new SemanticEngineHarness();

        h.Engine.Ready("h1", "T", "S", Icon, Link, null, Now);

        Assert.IsNotNull(ReadClock(h, "h1"));
    }

    [TestMethod]
    public void ReassertingReady_NeverRestartsTheClock_PreservesAccrual()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Ready("h1", "T", "S", Icon, Link, null, Now.AddMinutes(1));

        // Simulate the watchdog having accrued some decay time already (this is
        // what a real accrual write between the two `ready` calls looks like).
        var accrued = new ReadyDecayState(Now.AddMinutes(5), TimeSpan.FromMinutes(3));
        var entry = h.Sidecar.Read("h1")!;
        h.Sidecar.WriteWithMemory("h1", entry.Id, entry.LastUpdate, entry.EngineMemory! with { ReadyDecay = accrued });

        // A SECOND `ready` call, much later -- must NOT restart the clock.
        h.Engine.Ready("h1", "T", "S", Icon, Link, "All done.", Now.AddHours(2));

        var decay = ReadClock(h, "h1");
        Assert.AreEqual(accrued.LastSampledAt, decay!.LastSampledAt, "re-asserting Ready must not touch the clock at all.");
        Assert.AreEqual(accrued.AccruedPresentTime, decay.AccruedPresentTime);
    }

    [TestMethod]
    public void Working_LeavingReady_ClearsTheClock()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Ready("h1", "T", "S", Icon, Link, null, Now.AddMinutes(1));
        Assert.IsNotNull(ReadClock(h, "h1"));

        h.Engine.Working("h1", "T", "S", Icon, Link, "new goal", Now.AddMinutes(2));

        Assert.IsNull(ReadClock(h, "h1"));
    }

    [TestMethod]
    public void Blocked_LeavingReady_ClearsTheClock()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Ready("h1", "T", "S", Icon, Link, null, Now.AddMinutes(1));

        h.Engine.Blocked("h1", "T", "S", Icon, Link, "Continue?", agentId: null, Now.AddMinutes(2));

        Assert.IsNull(ReadClock(h, "h1"));
    }

    [TestMethod]
    public void Broken_LeavingReady_ClearsTheClock()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Ready("h1", "T", "S", Icon, Link, null, Now.AddMinutes(1));

        h.Engine.Broken("h1", "T", "S", Icon, Link, Codevoid.AgentTaskVoid.Semantics.BrokenReasonToken.Timeout, null, Now.AddMinutes(2));

        Assert.IsNull(ReadClock(h, "h1"));
    }

    [TestMethod]
    public void Activity_LeavingReady_ClearsTheClock()
    {
        // `activity` against a Ready card is not in ERGO-31's normal flow (Ready
        // is a turn-end state) but the engine must still project it correctly if
        // called -- lands back in Working, per the shared ProjectAfterLocusChange
        // path, and must clear the clock exactly like every other Ready-leaving claim.
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "T", "S", Icon, Link, "goal", Now);
        h.Engine.Ready("h1", "T", "S", Icon, Link, null, Now.AddMinutes(1));

        h.Engine.Activity("h1", "T", "S", Icon, Link, Codevoid.AgentTaskVoid.Semantics.ActivityKind.Read, "x", agentId: null, name: null, Now.AddMinutes(2));

        Assert.IsNull(ReadClock(h, "h1"));
        Assert.AreEqual(AppTaskState.Running, h.Store.FindAll().Single().State);
    }
}
