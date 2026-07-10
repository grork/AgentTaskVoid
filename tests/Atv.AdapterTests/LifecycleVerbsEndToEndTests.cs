using Atv.Operations;
using Atv.Persistence;
using Atv.Store;

namespace Atv.AdapterTests;

/// <summary>
/// Phase-08 acceptance criterion 5: extends the phase-03 real-adapter suite
/// with >=1 END-TO-END test PER LIFECYCLE VERB, driving the phase-05
/// <see cref="TaskOperations"/> core (what the phase-08 CLI itself wraps --
/// see <c>src/Atv/Cli/Dispatcher.cs</c>) against the REAL
/// <see cref="AppTaskStore"/>. <see cref="AdapterFidelityTests"/> already
/// proves the raw <see cref="IAppTaskStore"/> primitives round-trip; this
/// file proves the OPERATIONS layer -- sidecar + write-gate + validator +
/// the real platform together -- drives the taskbar card correctly for each
/// of the seven verbs (INFRA-9's "≥1 per verb" becomes meaningful at the
/// verb level, per plan/phase-08-cli-lifecycle-verbs.md).
///
/// Sidecar/recycle-bin live under a fresh per-test temp directory (our own
/// bookkeeping, independent of the real platform's tasks.json) with an
/// unnamed <see cref="Mutex"/> -- this suite isn't testing cross-process
/// write serialization, just real-API correctness through the operations
/// core. SERIAL by construction (no <c>[assembly: Parallelize]</c> in this
/// project), same identity-gate/before-and-after-clear pattern as
/// <see cref="AdapterFidelityTests"/>.
/// </summary>
[TestClass]
public sealed class LifecycleVerbsEndToEndTests
{
    private static readonly Uri DeepLink = new("https://example.com/lifecycle-e2e");
    private static readonly Uri IconUri = new("ms-appx:///Assets/Square44x44Logo.png");
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(1);

    private IAppTaskStore _store = null!;
    private SidecarStore _sidecar = null!;
    private RecycleBin _recycleBin = null!;
    private Mutex _mutex = null!;
    private TaskOperations _ops = null!;
    private string _tempRoot = null!;

    [TestInitialize]
    public void BeforeEachTest()
    {
        IdentityGate.AssertIdentityOrSkip();
        _store = new AppTaskStore();
        IdentityGate.AssertApiSupportedOrSkip(_store);
        ClearAllTasks(_store);

        _tempRoot = Path.Combine(Path.GetTempPath(), "atv-adapter-e2e-tests", Guid.NewGuid().ToString("N"));
        _sidecar = new SidecarStore(Path.Combine(_tempRoot, "sidecar"));
        _recycleBin = new RecycleBin(Path.Combine(_tempRoot, "recycle-bin"));
        _mutex = new Mutex(initiallyOwned: false);
        var gate = new WriteGate(_mutex);
        _ops = new TaskOperations(_store, _sidecar, _recycleBin, gate, Ttl);
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
    public void Start_CreatesARealTaskbarCard()
    {
        var outcome = _ops.Start("e2e-start", "Title", "Sub", IconUri, DeepLink, DateTimeOffset.Now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        var found = _store.Find(outcome.View!.Id);
        Assert.IsNotNull(found);
        Assert.AreEqual("Title", found!.Title);
        Assert.AreEqual(AppTaskState.Running, found.State);
    }

    [TestMethod]
    public void Step_AdvancesTheRealCardsExecutingStep()
    {
        var now = DateTimeOffset.Now;
        _ops.Start("e2e-step", "T", "S", IconUri, DeepLink, now);

        var outcome = _ops.Step("e2e-step", "first step", now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual("first step", outcome.View!.ExecutingStep);
        var found = _store.Find(outcome.View.Id);
        Assert.AreEqual("first step", found!.ExecutingStep);
    }

    [TestMethod]
    public void SetState_PausesTheRealCard()
    {
        var now = DateTimeOffset.Now;
        _ops.Start("e2e-state", "T", "S", IconUri, DeepLink, now);

        var outcome = _ops.SetState("e2e-state", AppTaskState.Paused, now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Paused, outcome.View!.State);
    }

    [TestMethod]
    public void Attention_SetsQuestionOnTheRealCard_ReachesNeedsAttention()
    {
        var now = DateTimeOffset.Now;
        _ops.Start("e2e-attention", "T", "S", IconUri, DeepLink, now);

        var outcome = _ops.Attention("e2e-attention", "Continue?", now);

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.NeedsAttention, outcome.View!.State);
    }

    [TestMethod]
    public void Done_CompletesTheRealCard_WithSummary()
    {
        var now = DateTimeOffset.Now;
        _ops.Start("e2e-done", "T", "S", IconUri, DeepLink, now);

        var outcome = _ops.Done("e2e-done", now, summary: "All finished.");

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Completed, outcome.View!.State);
        Assert.IsNotNull(outcome.View.EndTime);
    }

    [TestMethod]
    public void Fail_FailsTheRealCard()
    {
        var now = DateTimeOffset.Now;
        _ops.Start("e2e-fail", "T", "S", IconUri, DeepLink, now);

        var outcome = _ops.Fail("e2e-fail", now, summary: "boom");

        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
        Assert.AreEqual(AppTaskState.Error, outcome.View!.State);
    }

    [TestMethod]
    public void Remove_DeletesTheRealCard()
    {
        var now = DateTimeOffset.Now;
        var started = _ops.Start("e2e-remove", "T", "S", IconUri, DeepLink, now);

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
