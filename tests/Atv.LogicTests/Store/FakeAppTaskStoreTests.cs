using Atv.Store;

namespace Atv.LogicTests.Store;

/// <summary>
/// Covers phase-02 acceptance criterion 2: CRUD round-trip through the fake;
/// opaque-Id minting; unknown-Id ops return not-found (no throw); the
/// interleave hook produces deterministic last-writer-wins loss;
/// <c>HiddenByUser</c> surfaces through enumerate; drift hooks (vanish /
/// seed-unknown) behave as specified. Also doubles as the confirming check
/// referenced by docs/testing/fake-fidelity-promises.md for each promise's
/// "fake mechanism" (as opposed to the real-platform confirming checks, which
/// live in phase 03's adapter suite).
/// </summary>
[TestClass]
public sealed class FakeAppTaskStoreTests
{
    private static readonly Uri DeepLink = new("https://example.invalid/deep-link");
    private static readonly Uri IconUri = new("ms-appx:///Assets/Square44x44Logo.png");

    // ---- CRUD round-trip ----------------------------------------------

    [TestMethod]
    public void Create_Then_FindAndFindAll_RoundTripsAllFields()
    {
        var store = new FakeAppTaskStore();
        var content = new AppTaskContentDto.SequenceOfSteps(["step one"], "step two");

        var created = store.Create("Title", "Subtitle", DeepLink, IconUri, content);

        Assert.AreEqual("Title", created.Title);
        Assert.AreEqual("Subtitle", created.Subtitle);
        Assert.AreEqual(AppTaskState.Running, created.State, "Create has no state parameter -- a fresh task always starts Running, matching AppTaskInfo.Create's real signature.");
        Assert.AreEqual(DeepLink, created.DeepLink);
        Assert.AreEqual(IconUri, created.IconUri);
        Assert.IsFalse(created.HiddenByUser);
        Assert.IsNull(created.EndTime);
        CollectionAssert.AreEqual(new[] { "step one" }, created.CompletedSteps.ToArray());
        Assert.AreEqual("step two", created.ExecutingStep);

        AssertSameTask(created, store.Find(created.Id));

        var all = store.FindAll();
        Assert.HasCount(1, all);
        AssertSameTask(created, all[0]);
    }

    [TestMethod]
    public void Update_ReplacesContentWholesale_NoMerge()
    {
        var store = new FakeAppTaskStore();
        var created = store.Create("T", "S", DeepLink, IconUri,
            new AppTaskContentDto.SequenceOfSteps(["a", "b"], "c"));

        bool ok = store.Update(created.Id, AppTaskState.Paused,
            new AppTaskContentDto.SequenceOfSteps(["z"], "y"));

        Assert.IsTrue(ok);
        var view = store.Find(created.Id)!;
        Assert.AreEqual(AppTaskState.Paused, view.State);
        // Whole replacement, not merge -- "a"/"b"/"c" from the first write
        // must be gone, not appended to (ERGO-8; INFRA-15's negative
        // fidelity obligation).
        CollectionAssert.AreEqual(new[] { "z" }, view.CompletedSteps.ToArray());
        Assert.AreEqual("y", view.ExecutingStep);
    }

    [TestMethod]
    public void Update_ToEndingState_SetsEndTime()
    {
        var store = new FakeAppTaskStore();
        var created = store.Create("T", "S", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("hi"));
        Assert.IsNull(created.EndTime);

        store.Update(created.Id, AppTaskState.Completed, new AppTaskContentDto.TextSummaryResult("done"));

        Assert.IsNotNull(store.Find(created.Id)!.EndTime);
    }

    [TestMethod]
    public void UpdateTitles_And_UpdateDeepLink_TouchOnlyTheirOwnFields()
    {
        var store = new FakeAppTaskStore();
        var created = store.Create("T", "S", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("hi"));

        Assert.IsTrue(store.UpdateTitles(created.Id, "T2", "S2"));
        var afterTitles = store.Find(created.Id)!;
        Assert.AreEqual("T2", afterTitles.Title);
        Assert.AreEqual("S2", afterTitles.Subtitle);
        Assert.AreEqual(DeepLink, afterTitles.DeepLink);

        var newDeepLink = new Uri("https://example.invalid/new");
        Assert.IsTrue(store.UpdateDeepLink(created.Id, newDeepLink));
        var afterDeepLink = store.Find(created.Id)!;
        Assert.AreEqual(newDeepLink, afterDeepLink.DeepLink);
        Assert.AreEqual("T2", afterDeepLink.Title);
    }

    [TestMethod]
    public void Remove_DeletesTask_AndIsFalseOnSecondCall()
    {
        var store = new FakeAppTaskStore();
        var created = store.Create("T", "S", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("hi"));

        Assert.IsTrue(store.Remove(created.Id));
        Assert.IsNull(store.Find(created.Id));
        Assert.IsFalse(store.Remove(created.Id));
    }

    // ---- Fidelity promise 4: opaque Id minting -------------------------

    [TestMethod]
    public void Create_MintsNonEmptyDistinctIds()
    {
        var store = new FakeAppTaskStore();
        var a = store.Create("A", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        var b = store.Create("B", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));

        Assert.IsFalse(string.IsNullOrEmpty(a.Id));
        Assert.IsFalse(string.IsNullOrEmpty(b.Id));
        Assert.AreNotEqual(a.Id, b.Id);
    }

    // ---- Fidelity promise 3: unknown/vanished-Id ops are clean not-found ----

    [TestMethod]
    public void UnknownId_Find_ReturnsNull_NoThrow()
    {
        var store = new FakeAppTaskStore();
        Assert.IsNull(store.Find("does-not-exist"));
    }

    [TestMethod]
    public void UnknownId_MutatingOps_ReturnFalse_NoThrow()
    {
        var store = new FakeAppTaskStore();
        const string bogus = "does-not-exist";

        Assert.IsFalse(store.Update(bogus, AppTaskState.Running, new AppTaskContentDto.TextSummaryResult("x")));
        Assert.IsFalse(store.UpdateState(bogus, AppTaskState.Paused));
        Assert.IsFalse(store.UpdateTitles(bogus, "T", "S"));
        Assert.IsFalse(store.UpdateDeepLink(bogus, DeepLink));
        Assert.IsFalse(store.Remove(bogus));
    }

    // ---- Fidelity promise 2: HiddenByUser surfacing --------------------

    [TestMethod]
    public void HiddenByUser_DefaultsFalse_AndSurfacesThroughFindAndFindAll()
    {
        var store = new FakeAppTaskStore();
        var created = store.Create("T", "S", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));
        Assert.IsFalse(created.HiddenByUser);

        store.SetHiddenByUser(created.Id, true);

        Assert.IsTrue(store.Find(created.Id)!.HiddenByUser);
        Assert.IsTrue(store.FindAll().Single().HiddenByUser);
    }

    // ---- Fidelity promise 3: out-of-band drift hooks -------------------

    [TestMethod]
    public void SimulateVanish_RemovesTask_BehindLogicsBack()
    {
        var store = new FakeAppTaskStore();
        var created = store.Create("T", "S", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("x"));

        store.SimulateVanish(created.Id);

        Assert.IsNull(store.Find(created.Id));
        Assert.IsEmpty(store.FindAll());
        // Vanished-Id ops still return a clean not-found, never throw.
        Assert.IsFalse(store.UpdateState(created.Id, AppTaskState.Paused));
    }

    [TestMethod]
    public void SeedEntrylessTask_AppearsInFindAll_WithOpaqueMintedId()
    {
        var store = new FakeAppTaskStore();
        var seeded = store.SeedEntrylessTask("Entryless", "no sidecar entry knows this Id");

        Assert.IsFalse(string.IsNullOrEmpty(seeded.Id));
        AssertSameTask(seeded, store.Find(seeded.Id));
        Assert.HasCount(1, store.FindAll());
    }

    // ---- Fidelity promise 1: non-atomic whole-store clobber ------------

    [TestMethod]
    public void InterleaveHook_ProducesDeterministicLastWriterWinsLoss()
    {
        // Models INFRA-5's empirical clobber: writer A reads the whole store,
        // then (before A writes back) writer B fully creates a task and
        // commits it -- then A's write blindly overwrites with A's own
        // stale-snapshot-derived state, silently losing B's task. This is the
        // UNPROTECTED path; INFRA-6's mutex (phase 04's WriteGate, which wraps
        // a whole calling-code read-modify-write in a real Mutex so no second
        // writer's calls can land in between) is what prevents it in
        // production -- proving that regression is WriteGate's test, not this
        // one; this test only proves the fake can demonstrate the loss at all.
        var store = new FakeAppTaskStore();
        AppTaskView? lostTask = null;

        store.InterleaveHook = () =>
        {
            store.InterleaveHook = null; // don't recurse -- B's own create must commit atomically
            lostTask = store.Create("B (interleaved)", "", DeepLink, IconUri,
                new AppTaskContentDto.TextSummaryResult("b"));
        };

        var survivor = store.Create("A", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("a"));

        Assert.IsNotNull(lostTask);
        var all = store.FindAll();
        Assert.HasCount(1, all, "B's task should have been silently clobbered by A's stale write.");
        Assert.AreEqual(survivor.Id, all[0].Id);
        Assert.IsNull(store.Find(lostTask!.Id), "B's task must not be reachable after the clobber.");
    }

    [TestMethod]
    public void NoInterleave_TwoSequentialCreates_BothSurvive()
    {
        // Control case: without setting InterleaveHook, calls behave
        // atomically -- the hook is what introduces loss, not the
        // whole-store-replace mechanism by itself.
        var store = new FakeAppTaskStore();
        var a = store.Create("A", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("a"));
        var b = store.Create("B", "", DeepLink, IconUri, new AppTaskContentDto.TextSummaryResult("b"));

        Assert.HasCount(2, store.FindAll());
        Assert.IsNotNull(store.Find(a.Id));
        Assert.IsNotNull(store.Find(b.Id));
    }

    // ---- IsSupported ----------------------------------------------------

    [TestMethod]
    public void IsSupported_DefaultsTrue_AndIsSettable()
    {
        var store = new FakeAppTaskStore();
        Assert.IsTrue(store.IsSupported());
        store.Supported = false;
        Assert.IsFalse(store.IsSupported());
    }

    /// <summary>
    /// Field-by-field comparison instead of record <c>Equals</c>: several
    /// <see cref="AppTaskView"/> fields (e.g. <c>CompletedSteps</c>) are
    /// reference-typed collections without structural equality overrides, so
    /// leaning on the generated record <c>Equals</c> would only coincidentally
    /// pass when the two arguments happen to share list references.
    /// </summary>
    private static void AssertSameTask(AppTaskView expected, AppTaskView? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.Id, actual!.Id);
        Assert.AreEqual(expected.Title, actual.Title);
        Assert.AreEqual(expected.Subtitle, actual.Subtitle);
        Assert.AreEqual(expected.State, actual.State);
        Assert.AreEqual(expected.StartTime, actual.StartTime);
        Assert.AreEqual(expected.EndTime, actual.EndTime);
        Assert.AreEqual(expected.DeepLink, actual.DeepLink);
        Assert.AreEqual(expected.IconUri, actual.IconUri);
        Assert.AreEqual(expected.HiddenByUser, actual.HiddenByUser);
        CollectionAssert.AreEqual(expected.CompletedSteps.ToArray(), actual.CompletedSteps.ToArray());
        Assert.AreEqual(expected.ExecutingStep, actual.ExecutingStep);
    }
}
