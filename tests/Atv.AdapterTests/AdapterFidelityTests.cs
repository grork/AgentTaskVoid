using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.AdapterTests;

/// <summary>
/// INFRA-9's "small, data-driven, serial real-API adapter suite": proves the thin
/// <see cref="AppTaskStore"/> faithfully drives the real platform. Per-primitive
/// round-trips (>=1 end-to-end per <see cref="IAppTaskStore"/> member), adapter
/// translation + <c>tasks.json</c> fidelity (via <see cref="TasksJsonReader"/>), and
/// the two INFRA-15 "automated confirming checks" fake-fidelity-promises.md points at:
/// unknown-Id / removed-Id behavior (promise 3), and the negative whole-content
/// -replacement check (promise 1's negative twin -- <c>Update</c> replaces content
/// wholesale, nothing merges; ERGO-8/INFRA-15's "no convenience merge/append").
///
/// SERIAL by design (no <c>[assembly: Parallelize]</c> anywhere in this project,
/// unlike tests/Atv.LogicTests) -- every test in this suite shares the ONE real
/// tasks.json for this identity (INFRA-9), so parallel test methods would clobber each
/// other exactly like PeriodicClobberTests demonstrates deliberately.
///
/// AC4: every test clears this identity's tasks BEFORE and AFTER itself, so a crashed
/// run leaves at most stale tasks the next run's BEFORE-clear removes, and a normal run
/// leaves tasks.json clean afterward regardless of pass/fail (TestCleanup always runs).
/// </summary>
[TestClass]
public sealed class AdapterFidelityTests
{
    private static readonly Uri DeepLink = new("https://example.com/adapter-tests");
    private static readonly Uri IconUri = new("ms-appx:///Assets/Square44x44Logo.png");

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
        // Store is only unset if BeforeEachTest bailed out via Assert.Inconclusive
        // before reaching the assignment -- nothing to clear in that case.
        if (Store is not null)
            ClearAllTasks(Store);
    }

    // ---- IsSupported --------------------------------------------------------------

    [TestMethod]
    public void IsSupported_ReturnsTrue_HavingPassedTheGate()
    {
        // The TestInitialize gate already called this once (IdentityGate); a second,
        // explicit call here is the dedicated coverage point for this primitive.
        Assert.IsTrue(Store.IsSupported());
    }

    // ---- Create / FindAll / Find ---------------------------------------------------

    [TestMethod]
    public void Create_ThenFindAll_RoundTripsEveryField()
    {
        var content = new AppTaskContentDto.SequenceOfSteps(["step one"], "step two");

        var created = Store.Create("Fidelity title", "Fidelity subtitle", DeepLink, IconUri, content);

        Assert.IsFalse(string.IsNullOrEmpty(created.Id), "Platform must mint a non-empty Id (INFRA-15 promise 4).");
        Assert.AreEqual(AppTaskState.Running, created.State, "A freshly created task always starts Running.");

        var all = Store.FindAll();
        var found = all.SingleOrDefault(t => t.Id == created.Id);
        Assert.IsNotNull(found, "FindAll() must include the just-created task.");
        Assert.AreEqual("Fidelity title", found!.Title);
        Assert.AreEqual("Fidelity subtitle", found.Subtitle);
        Assert.AreEqual(DeepLink, found.DeepLink);
        Assert.AreEqual(AppTaskState.Running, found.State);
        Assert.IsFalse(found.HiddenByUser);
        CollectionAssert.AreEqual(new[] { "step one" }, found.CompletedSteps.ToArray());
        Assert.AreEqual("step two", found.ExecutingStep);
    }

    [TestMethod]
    public void Create_TasksJsonReflectsTheCreatedTask()
    {
        var content = new AppTaskContentDto.SequenceOfSteps([], "in progress");
        var created = Store.Create("Json fidelity", "sub", DeepLink, IconUri, content);

        var raw = TasksJsonReader.Read();
        var rawTask = raw.Tasks.SingleOrDefault(t => t.Id == created.Id);

        Assert.IsNotNull(rawTask, $"tasks.json (at {TasksJsonReader.GetPathForCurrentPackage()}) must contain the created task.");
        Assert.AreEqual("Json fidelity", rawTask!.Title);
        Assert.AreEqual("sub", rawTask.Subtitle);
        Assert.AreEqual(0, rawTask.TaskState, "taskState 0 == AppTaskState.Running on disk.");
        Assert.IsFalse(rawTask.HiddenByUser);
        Assert.AreEqual("SequenceOfSteps", rawTask.DataJson.GetProperty("template").GetString());
    }

    [TestMethod]
    public void Find_ById_ReturnsTheTask()
    {
        var created = Store.Create("Find me", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("n/a"));

        var found = Store.Find(created.Id);

        Assert.IsNotNull(found);
        Assert.AreEqual(created.Id, found!.Id);
        Assert.AreEqual("Find me", found.Title);
    }

    // ---- Update: whole-content replacement (INFRA-15 promise 3's negative twin; ERGO-8) -------

    [TestMethod]
    public void Update_ReplacesContentWholesale_OldStepsDoNotLinger()
    {
        var created = Store.Create(
            "Advance model",
            "",
            DeepLink,
            IconUri,
            new AppTaskContentDto.SequenceOfSteps(["old-a", "old-b"], "old-executing"));

        bool updated = Store.Update(
            created.Id,
            AppTaskState.Running,
            new AppTaskContentDto.SequenceOfSteps(["new-x"], "new-executing"));

        Assert.IsTrue(updated);
        var found = Store.Find(created.Id);
        Assert.IsNotNull(found);

        // The negative check: nothing from the OLD content survives -- no merge/append
        // (a fake bug this exact check would catch: accidentally concatenating
        // completedSteps across Update calls instead of replacing them).
        CollectionAssert.AreEqual(new[] { "new-x" }, found!.CompletedSteps.ToArray());
        Assert.AreEqual("new-executing", found.ExecutingStep);
        CollectionAssert.DoesNotContain(found.CompletedSteps.ToArray(), "old-a");
        CollectionAssert.DoesNotContain(found.CompletedSteps.ToArray(), "old-b");

        // Cross-check directly against tasks.json, independent of the adapter's own read path.
        var raw = TasksJsonReader.Read().Tasks.Single(t => t.Id == created.Id);
        var completedOnDisk = raw.DataJson.GetProperty("data").GetProperty("completedSteps")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        CollectionAssert.AreEqual(new[] { "new-x" }, completedOnDisk);
    }

    [TestMethod]
    public void Update_ToCompletedWithTextSummary_RoundTrips()
    {
        var created = Store.Create("Summary flow", "", DeepLink, IconUri, new AppTaskContentDto.SequenceOfSteps([], "working"));

        bool updated = Store.Update(created.Id, AppTaskState.Completed, new AppTaskContentDto.TextSummaryResult("Done: 3 files changed."));

        Assert.IsTrue(updated);
        var found = Store.Find(created.Id);
        Assert.IsNotNull(found);
        Assert.AreEqual(AppTaskState.Completed, found!.State);
        Assert.IsNotNull(found.EndTime, "EndTime populates once a task reaches an ending state (Completed/Error).");
    }

    // ---- UpdateState / UpdateTitles / UpdateDeepLink --------------------------------

    [TestMethod]
    public void UpdateState_ChangesStateOnly_ContentUnchanged()
    {
        var created = Store.Create("State only", "", DeepLink, IconUri, new AppTaskContentDto.SequenceOfSteps(["kept"], "still here"));

        bool updated = Store.UpdateState(created.Id, AppTaskState.Paused);

        Assert.IsTrue(updated);
        var found = Store.Find(created.Id);
        Assert.IsNotNull(found);
        Assert.AreEqual(AppTaskState.Paused, found!.State);
        CollectionAssert.AreEqual(new[] { "kept" }, found.CompletedSteps.ToArray());
        Assert.AreEqual("still here", found.ExecutingStep);
    }

    [TestMethod]
    public void UpdateTitles_ChangesTitleAndSubtitle()
    {
        var created = Store.Create("Old title", "Old subtitle", DeepLink, IconUri, new AppTaskContentDto.SequenceOfSteps([], "x"));

        bool updated = Store.UpdateTitles(created.Id, "New title", "New subtitle");

        Assert.IsTrue(updated);
        var found = Store.Find(created.Id);
        Assert.IsNotNull(found);
        Assert.AreEqual("New title", found!.Title);
        Assert.AreEqual("New subtitle", found.Subtitle);
    }

    [TestMethod]
    public void UpdateDeepLink_ChangesDeepLink()
    {
        var created = Store.Create("Link test", "", DeepLink, IconUri, new AppTaskContentDto.SequenceOfSteps([], "x"));
        var newLink = new Uri("https://example.com/adapter-tests/updated");

        bool updated = Store.UpdateDeepLink(created.Id, newLink);

        Assert.IsTrue(updated);
        var found = Store.Find(created.Id);
        Assert.IsNotNull(found);
        Assert.AreEqual(newLink, found!.DeepLink);
    }

    // ---- Remove ----------------------------------------------------------------------

    [TestMethod]
    public void Remove_DeletesTheTask_SubsequentFindReturnsNull()
    {
        var created = Store.Create("To remove", "", DeepLink, IconUri, new AppTaskContentDto.SequenceOfSteps([], "x"));

        bool removed = Store.Remove(created.Id);

        Assert.IsTrue(removed);
        Assert.IsNull(Store.Find(created.Id));
        Assert.IsFalse(Store.FindAll().Any(t => t.Id == created.Id));
    }

    // ---- Unknown-Id / removed-Id behavior (INFRA-15 promise 3, real platform) --------
    // Real-platform confirmation of what FakeAppTaskStoreTests already proves against
    // the fake (phase 02): every Id-addressed member returns a clean not-found for an
    // unknown/vanished Id -- never a throw.

    [TestMethod]
    public void Find_UnknownId_ReturnsNull_NoThrow()
        => Assert.IsNull(Store.Find("this-id-was-never-minted"));

    [TestMethod]
    public void Update_UnknownId_ReturnsFalse_NoThrow()
        => Assert.IsFalse(Store.Update("this-id-was-never-minted", AppTaskState.Running, new AppTaskContentDto.SequenceOfSteps([], "x")));

    [TestMethod]
    public void UpdateState_UnknownId_ReturnsFalse_NoThrow()
        => Assert.IsFalse(Store.UpdateState("this-id-was-never-minted", AppTaskState.Running));

    [TestMethod]
    public void UpdateTitles_UnknownId_ReturnsFalse_NoThrow()
        => Assert.IsFalse(Store.UpdateTitles("this-id-was-never-minted", "t", "s"));

    [TestMethod]
    public void UpdateDeepLink_UnknownId_ReturnsFalse_NoThrow()
        => Assert.IsFalse(Store.UpdateDeepLink("this-id-was-never-minted", DeepLink));

    [TestMethod]
    public void Remove_UnknownId_ReturnsFalse_NoThrow()
        => Assert.IsFalse(Store.Remove("this-id-was-never-minted"));

    [TestMethod]
    public void Remove_ThenOperateOnRemovedId_AllReturnCleanNotFound_NoThrow()
    {
        var created = Store.Create("Will vanish", "", DeepLink, IconUri, new AppTaskContentDto.SequenceOfSteps([], "x"));
        Assert.IsTrue(Store.Remove(created.Id));

        // The Id is now genuinely "removed-Id", not merely "never minted" -- a
        // slightly different real-platform code path than the never-minted cases
        // above, worth its own coverage.
        Assert.IsNull(Store.Find(created.Id));
        Assert.IsFalse(Store.Update(created.Id, AppTaskState.Running, new AppTaskContentDto.SequenceOfSteps([], "x")));
        Assert.IsFalse(Store.UpdateState(created.Id, AppTaskState.Running));
        Assert.IsFalse(Store.UpdateTitles(created.Id, "t", "s"));
        Assert.IsFalse(Store.UpdateDeepLink(created.Id, DeepLink));
        Assert.IsFalse(Store.Remove(created.Id), "Removing an already-removed Id is a clean not-found, not idempotent-true.");
    }

    private static void ClearAllTasks(IAppTaskStore store)
    {
        foreach (var task in store.FindAll())
            store.Remove(task.Id);
    }
}
