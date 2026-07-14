using Atv.Operations;
using Atv.Persistence;
using Atv.Semantics;
using Atv.Store;

namespace Atv.AdapterTests;

/// <summary>
/// Phase-10 acceptance criterion 5: >=1 real end-to-end test for `list` and
/// `clear`, driving <see cref="TaskOperations.List"/>/
/// <see cref="TaskOperations.ClearAll"/> (what the phase-10 CLI verbs wrap --
/// see <c>src/Atv/Cli/Verbs/ListVerb.cs</c>/<c>ClearVerb.cs</c>) against the
/// REAL <see cref="AppTaskStore"/>. Same identity-gate/setup/serial pattern
/// as <see cref="SemanticVerbsEndToEndTests"/>. Setup now upserts real cards
/// via <see cref="SemanticEngine.Working"/> (phase 15's successor to the
/// retired <c>TaskOperations.Start</c>) -- <c>List</c>/<c>ClearAll</c>
/// themselves are unchanged v1-era <see cref="TaskOperations"/> members.
/// </summary>
[TestClass]
public sealed class UtilityVerbsEndToEndTests
{
    private static readonly Uri DeepLink = new("https://example.com/utility-e2e");
    private static readonly Uri IconUri = new("ms-appx:///Assets/Square44x44Logo.png");
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(1);

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

        _tempRoot = Path.Combine(Path.GetTempPath(), "atv-adapter-e2e-utility-tests", Guid.NewGuid().ToString("N"));
        _sidecar = new SidecarStore(Path.Combine(_tempRoot, "sidecar"));
        _recycleBin = new RecycleBin(Path.Combine(_tempRoot, "recycle-bin"));
        _mutex = new Mutex(initiallyOwned: false);
        var gate = new WriteGate(_mutex);
        _ops = new TaskOperations(_store, _sidecar, _recycleBin, gate, Ttl);
        _engine = new SemanticEngine(_store, _sidecar, _recycleBin, gate, Ttl, _ops);
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
    public void List_CorrelatesRealCardsWithSidecarHandles()
    {
        var now = DateTimeOffset.Now;
        _engine.Working("e2e-list-1", "Title One", "Sub", IconUri, DeepLink, "goal", now);
        _engine.Working("e2e-list-2", "Title Two", "Sub", IconUri, DeepLink, "goal", now);

        var entries = _ops.List();

        Assert.HasCount(2, entries);
        var byHandle = entries.ToDictionary(e => e.Handle!);
        Assert.AreEqual("Title One", byHandle["e2e-list-1"].Title);
        Assert.AreEqual("Title Two", byHandle["e2e-list-2"].Title);
        Assert.IsTrue(byHandle.Values.All(e => e.LastUpdate is not null));
    }

    [TestMethod]
    public void Clear_RemovesRealCards_SidecarEntries_AndIsACleanNoOpSecondTime()
    {
        var now = DateTimeOffset.Now;
        _engine.Working("e2e-clear-1", "T", "S", IconUri, DeepLink, "goal", now);
        _engine.Working("e2e-clear-2", "T", "S", IconUri, DeepLink, "goal", now);

        var summary = _ops.ClearAll(includeRecycleBin: false);

        Assert.IsTrue(summary.GateAcquired);
        Assert.AreEqual(2, summary.TasksRemoved);
        Assert.IsEmpty(_store.FindAll());
        Assert.IsEmpty(_ops.List());
        Assert.IsNull(_sidecar.Read("e2e-clear-1"));
        Assert.IsNull(_sidecar.Read("e2e-clear-2"));

        var second = _ops.ClearAll(includeRecycleBin: false);
        Assert.IsTrue(second.GateAcquired);
        Assert.AreEqual(0, second.TasksRemoved, "a second clear against an already-empty identity must be a clean no-op.");
    }

    private static void ClearAllTasks(IAppTaskStore store)
    {
        foreach (var task in store.FindAll())
            store.Remove(task.Id);
    }
}
