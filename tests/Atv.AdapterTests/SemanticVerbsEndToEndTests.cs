using Atv.Operations;
using Atv.Persistence;
using Atv.Semantics;
using Atv.Store;

namespace Atv.AdapterTests;

/// <summary>
/// Phase-15 AC9's real-API e2e requirement: at least one real-API test PER
/// SEMANTIC VERB, driving <see cref="SemanticEngine"/> (what the CLI's
/// Dispatcher itself wraps -- see <c>src/Atv/Cli/Dispatcher.cs</c>) against
/// the REAL <see cref="AppTaskStore"/>. Supersedes the retired
/// <c>LifecycleVerbsEndToEndTests</c> (v1 verbs) with the ERGO-31 v2
/// surface. 15A scope: the non-fan-out, non-clock slice -- <c>agent-started</c>/
/// <c>agent-stopped</c> get a real-API bookkeeping smoke test (no child-card
/// minting yet, 15B's job); Ready→Idle decay is not exercised (no clock
/// built yet, also 15B).
///
/// Same identity-gate/before-and-after-clear pattern as
/// <see cref="AdapterFidelityTests"/>: sidecar/recycle-bin live under a fresh
/// per-test temp directory, unnamed <see cref="Mutex"/>, SERIAL by
/// construction (no <c>[assembly: Parallelize]</c> in this project).
/// </summary>
[TestClass]
public sealed class SemanticVerbsEndToEndTests
{
    private static readonly Uri DeepLink = new("https://example.com/semantic-e2e");
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(1);

    // A REAL file:// icon (exactly what production always supplies via
    // IconService.Place -- see src/Atv/Icons/IconService.cs) rather than the
    // symbolic "ms-appx:///Assets/..." placeholder other adapter suites use
    // for a single one-shot Create(). Empirically discovered this phase: an
    // "ms-appx" icon Uri does NOT round-trip byte-identical through the real
    // platform's IconUri readback, so a SECOND verb call on the same handle
    // (this file's whole point -- upsert semantics mean every test chains
    // multiple verb calls per handle) would spuriously trip the icon-
    // immutability-forces-recreate branch on every single call. A real file
    // Uri round-trips stable, matching how the product is actually driven.
    private Uri IconUri = null!;

    private IAppTaskStore _store = null!;
    private SidecarStore _sidecar = null!;
    private RecycleBin _recycleBin = null!;
    private Mutex _mutex = null!;
    private TaskOperations _ops = null!;
    private SemanticEngine _engine = null!;
    private string _tempRoot = null!;

    [TestInitialize]
    public void BeforeEachTest()
    {
        IdentityGate.AssertIdentityOrSkip();
        _store = new AppTaskStore();
        IdentityGate.AssertApiSupportedOrSkip(_store);
        ClearAllTasks(_store);

        _tempRoot = Path.Combine(Path.GetTempPath(), "atv-adapter-semantic-e2e-tests", Guid.NewGuid().ToString("N"));
        _sidecar = new SidecarStore(Path.Combine(_tempRoot, "sidecar"));
        _recycleBin = new RecycleBin(Path.Combine(_tempRoot, "recycle-bin"));
        _mutex = new Mutex(initiallyOwned: false);
        var gate = new WriteGate(_mutex);
        _ops = new TaskOperations(_store, _sidecar, _recycleBin, gate, Ttl);
        _engine = new SemanticEngine(_store, _sidecar, _recycleBin, gate, Ttl, _ops);

        Directory.CreateDirectory(_tempRoot);
        string iconPath = Path.Combine(_tempRoot, "icon.png");
        File.WriteAllBytes(iconPath, [0x89, 0x50, 0x4E, 0x47]); // minimal PNG-signature stand-in -- content bytes are never inspected by the real API.
        IconUri = new Uri(iconPath);
    }

    [TestCleanup]
    public void AfterEachTest()
    {
        if (_store is not null)
            ClearAllTasks(_store);

        _mutex?.Dispose();
        if (_tempRoot is not null && Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [TestMethod]
    public void Activity_OmittedTitleOnFollowUpCall_NeverCrashes_PreservesTheRealTitle()
    {
        // The real-adapter discovery this phase made: AppTaskInfo.UpdateTitles
        // throws on an empty title for an ALREADY-LIVE task (Create tolerates
        // empty fine) -- a translator that (very plausibly) sends --title only
        // on the FIRST call of a session must never crash on every call after.
        var now = DateTimeOffset.Now;
        _engine.Working("e2e-omitted-title", "Real Title", "Real Sub", IconUri, DeepLink, "goal", now);

        var outcome = _engine.Activity("e2e-omitted-title", title: null, subtitle: null, IconUri, DeepLink,
            ActivityKind.Shell, "npm test", agentId: null, name: null, now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual("Real Title", outcome.View!.Title, "the real title must survive an omitted --title on a follow-up call.");
        var found = _store.Find(outcome.View.Id);
        Assert.AreEqual("Real Title", found!.Title);
    }

    [TestMethod]
    public void Working_CreatesARealTaskbarCard_WithGoalAsExecutingStep()
    {
        var outcome = _engine.Working("e2e-working", "Title", "Sub", IconUri, DeepLink, "Fix the bug", DateTimeOffset.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        var found = _store.Find(outcome.View!.Id);
        Assert.IsNotNull(found);
        Assert.AreEqual("Title", found!.Title);
        Assert.AreEqual(AppTaskState.Running, found.State);
        Assert.AreEqual("Fix the bug", found.ExecutingStep);
    }

    [TestMethod]
    public void Activity_AdvancesTheRealCardsExecutingStep_RenderedFromKindAndLabel()
    {
        var now = DateTimeOffset.Now;
        _engine.Working("e2e-activity", "T", "S", IconUri, DeepLink, "goal", now);

        var outcome = _engine.Activity("e2e-activity", "T", "S", IconUri, DeepLink, ActivityKind.Read, "auth.ts", agentId: null, name: null, now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual("Reading auth.ts", outcome.View!.ExecutingStep);
        var found = _store.Find(outcome.View.Id);
        Assert.AreEqual("Reading auth.ts", found!.ExecutingStep);
    }

    [TestMethod]
    public void Blocked_SetsQuestionOnTheRealCard_ReachesNeedsAttention()
    {
        var now = DateTimeOffset.Now;
        _engine.Working("e2e-blocked", "T", "S", IconUri, DeepLink, "goal", now);

        var outcome = _engine.Blocked("e2e-blocked", "T", "S", IconUri, DeepLink, "Continue?", agentId: null, now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State);
    }

    [TestMethod]
    public void Activity_AgainstARealBlockedCard_DropsTheQuestion_ReEntersRunning()
    {
        var now = DateTimeOffset.Now;
        _engine.Working("e2e-blocked-clear", "T", "S", IconUri, DeepLink, "goal", now);
        _engine.Blocked("e2e-blocked-clear", "T", "S", IconUri, DeepLink, "Continue?", agentId: null, now);

        var outcome = _engine.Activity("e2e-blocked-clear", "T", "S", IconUri, DeepLink, ActivityKind.Write, "result.txt", agentId: null, name: null, now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind, "AC3: no v1 'state running' chain needed first -- the real platform must accept this directly.");
        Assert.AreEqual(AppTaskState.Running, outcome.View!.State);
    }

    [TestMethod]
    public void Ready_CompletesTheRealCard_WithSummary()
    {
        var now = DateTimeOffset.Now;
        _engine.Working("e2e-ready", "T", "S", IconUri, DeepLink, "goal", now);

        var outcome = _engine.Ready("e2e-ready", "T", "S", IconUri, DeepLink, "All finished.", now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
        Assert.IsNotNull(outcome.View.EndTime);
    }

    [TestMethod]
    public void Broken_FailsTheRealCard_TextSummaryReason()
    {
        var now = DateTimeOffset.Now;
        _engine.Working("e2e-broken", "T", "S", IconUri, DeepLink, "goal", now);

        var outcome = _engine.Broken("e2e-broken", "T", "S", IconUri, DeepLink, BrokenReasonToken.ApiError, "connection reset", now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Error, outcome.View!.State);
    }

    [TestMethod]
    public void AgentStartedAndStopped_RealCard_SoloLocus_BookkeepingOnly_NoStateChange_NoCard()
    {
        var now = DateTimeOffset.Now;
        _engine.Working("e2e-agent", "T", "S", IconUri, DeepLink, "goal", now);

        var started = _engine.AgentStarted("e2e-agent", "T", "S", IconUri, DeepLink, agentId: "worker-1", name: "worker", now);
        var stopped = _engine.AgentStopped("e2e-agent", "T", "S", IconUri, DeepLink, agentId: "worker-1", now);

        Assert.AreEqual(OutcomeKind.Accepted, started.Kind);
        Assert.AreEqual(OutcomeKind.Accepted, stopped.Kind);
        Assert.AreEqual(AppTaskState.Running, _store.Find(started.View!.Id)!.State, "a solo (never-concurrent) locus is pure bookkeeping -- no child card, no state transition.");
    }

    [TestMethod]
    public void AgentStarted_SecondConcurrent_MintsRealChildCards_ByteIdenticalIcon_ThenCascadesOnRemove()
    {
        // AC6/AC9's real-API fan-out slice: an actual 2nd-concurrent agent-started
        // mints TWO real WinRT cards (retroactive 1st + the 2nd), reusing the
        // parent's own IconUri byte-for-byte, and `remove` on the parent actually
        // makes both real child cards disappear from the live platform.
        var now = DateTimeOffset.Now;
        var parent = _engine.Working("e2e-fanout", "Session", "S", IconUri, DeepLink, "goal", now);
        int countBeforeFanOut = _store.FindAll().Count;

        var first = _engine.AgentStarted("e2e-fanout", "Session", "S", IconUri, DeepLink, agentId: "worker-a", name: "Worker A", now);
        Assert.HasCount(countBeforeFanOut, _store.FindAll(), "the 1st worker alone must not yet mint a real card.");

        var second = _engine.AgentStarted("e2e-fanout", "Session", "S", IconUri, DeepLink, agentId: "worker-b", name: "Worker B", now);

        Assert.AreEqual(OutcomeKind.Accepted, first.Kind);
        Assert.AreEqual(OutcomeKind.Accepted, second.Kind);

        var childEntryA = _sidecar.Read("e2e-fanout#worker-a");
        var childEntryB = _sidecar.Read("e2e-fanout#worker-b");
        Assert.IsNotNull(childEntryA, "the retroactively-carded 1st worker must have a real sidecar entry.");
        Assert.IsNotNull(childEntryB, "the 2nd worker must have a real sidecar entry.");

        var childViewA = _store.Find(childEntryA!.Id);
        var childViewB = _store.Find(childEntryB!.Id);
        Assert.IsNotNull(childViewA, "worker-a's card must be a REAL, live WinRT task.");
        Assert.IsNotNull(childViewB, "worker-b's card must be a REAL, live WinRT task.");
        Assert.AreEqual(IconUri, childViewA!.IconUri, "the child must reuse the parent's exact IconUri byte-for-byte -- never IconService.Place.");
        Assert.AreEqual(IconUri, childViewB!.IconUri);
        Assert.AreEqual(AppTaskState.Running, childViewA.State);
        Assert.AreEqual(AppTaskState.Running, childViewB.State);
        Assert.AreEqual("Worker A", childViewA.Title);
        Assert.AreEqual("Worker B", childViewB.Title);

        // Cascade: removing the parent must ALSO remove both real children.
        var removeOutcome = _ops.Remove("e2e-fanout", now);

        Assert.AreEqual(OutcomeKind.Removed, removeOutcome.Kind);
        Assert.IsNull(_store.Find(parent.View!.Id), "the parent card must be gone.");
        Assert.IsNull(_store.Find(childEntryA.Id), "the cascade must ALSO remove worker-a's real card.");
        Assert.IsNull(_store.Find(childEntryB.Id), "the cascade must ALSO remove worker-b's real card.");
        Assert.IsNull(_sidecar.Read("e2e-fanout#worker-a"));
        Assert.IsNull(_sidecar.Read("e2e-fanout#worker-b"));
    }

    [TestMethod]
    public void SessionEnded_Finished_RemovesTheRealCard()
    {
        var now = DateTimeOffset.Now;
        var started = _engine.Working("e2e-session-finished", "T", "S", IconUri, DeepLink, "goal", now);

        var outcome = _engine.SessionEnded("e2e-session-finished", SessionEndedReasonToken.Finished, now);

        Assert.AreEqual(OutcomeKind.Removed, outcome.Kind);
        Assert.IsNull(_store.Find(started.View!.Id));
    }

    [TestMethod]
    public void SessionEnded_Error_FailsTheRealCard()
    {
        var now = DateTimeOffset.Now;
        _engine.Working("e2e-session-error", "T", "S", IconUri, DeepLink, "goal", now);

        var outcome = _engine.SessionEnded("e2e-session-error", SessionEndedReasonToken.Error, now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Error, outcome.View!.State);
    }

    [TestMethod]
    public void Remove_DeletesTheRealCard()
    {
        var now = DateTimeOffset.Now;
        var started = _engine.Working("e2e-remove", "T", "S", IconUri, DeepLink, "goal", now);

        var outcome = _ops.Remove("e2e-remove", now);

        Assert.AreEqual(OutcomeKind.Removed, outcome.Kind);
        Assert.IsNull(_store.Find(started.View!.Id));
    }

    private static void ClearAllTasks(IAppTaskStore store)
    {
        foreach (var task in store.FindAll())
            store.Remove(task.Id);
    }
}
