using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.LogicTests.Persistence;
using Codevoid.AgentTaskVoid.LogicTests.Store;
using Codevoid.AgentTaskVoid.Operations;
using Codevoid.AgentTaskVoid.Semantics;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Semantics;

/// <summary>
/// Phase 22 Part 1 (ERGO-34/ERGO-35's shared fix): AC1's icon-survives-plain-
/// updates repro (inverted) and AC2's deep-link-survives-plain-updates
/// structural proof. Both prove the same underlying rule -- an UNCLAIMED
/// (non-explicit) field is never re-written on an update -- from opposite
/// ends: AC1 via on-disk bytes/IconUri, AC2 via a counting-fake's
/// <see cref="IAppTaskStore.UpdateDeepLink"/> call count.
/// </summary>
[TestClass]
public sealed class SemanticEnginePreservationTests
{
    private static readonly Uri Link = SemanticEngineHarness.DeepLink;
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    // ==== AC1: icon file survives plain updates =================================

    [TestMethod]
    public void ExplicitIcon_SurvivesABurstOfNoIconUpdates_ByteIdentical_NoForcedRecreate()
    {
        using var h = new SemanticEngineHarness(withIcons: true);
        IconToken distinctive = IconToken.Segoe(IconTokens.CuratedSegoe["Heart"]);

        var created = h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, Link, "goal", Now, iconToken: distinctive, iconExplicit: true);
        Assert.IsTrue(created.Success);
        Uri createTimeIconUri = created.View!.IconUri;
        byte[] createTimeBytes = File.ReadAllBytes(createTimeIconUri.LocalPath);

        // A burst of plain (no-icon) updates -- exactly what a translator's
        // `activity` calls look like (no --icon flag ever sent).
        var outcome1 = h.Engine.Activity("h1", "T", "S", SemanticEngineHarness.IconUri, Link, ActivityKind.Read, "a.txt", null, null, Now.AddMinutes(1), iconToken: IconTokens.Default, iconExplicit: false);
        var outcome2 = h.Engine.Activity("h1", "T", "S", SemanticEngineHarness.IconUri, Link, ActivityKind.Edit, "b.txt", null, null, Now.AddMinutes(2), iconToken: IconTokens.Default, iconExplicit: false);
        var outcome3 = h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, Link, "goal2", Now.AddMinutes(3), iconToken: IconTokens.Default, iconExplicit: false);

        foreach (var outcome in new[] { outcome1, outcome2, outcome3 })
        {
            Assert.IsTrue(outcome.Success);
            Assert.IsFalse(outcome.IconChanged, "a plain update must never force a Remove+Create.");
            Assert.AreEqual(createTimeIconUri, outcome.View!.IconUri, "IconUri must be byte-for-byte the same URI across every plain update.");
        }

        byte[] afterBurstBytes = File.ReadAllBytes(createTimeIconUri.LocalPath);
        CollectionAssert.AreEqual(createTimeBytes, afterBurstBytes, "the on-disk PNG must be byte-identical to the create-time bytes -- a plain update must never rewrite it.");

        var finalView = h.Store.Find(created.View.Id)!;
        Assert.IsNotEmpty(finalView.CompletedSteps, "step history must be preserved across the burst (no forced recreate ever wiped it).");
    }

    [TestMethod]
    public void ExplicitIconOnUpdate_DoesRewriteTheFile()
    {
        using var h = new SemanticEngineHarness(withIcons: true);
        IconToken original = IconToken.Segoe(IconTokens.CuratedSegoe["Heart"]);
        IconToken updated = IconToken.Segoe(IconTokens.CuratedSegoe["Bug"]);

        h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, Link, "goal", Now, iconToken: original, iconExplicit: true);

        // An update that explicitly passes --icon (a NEW, real caller-supplied
        // token) via iconExplicit: true.
        var outcome = h.Engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, Link, "goal2", Now.AddMinutes(1), iconToken: updated, iconExplicit: true);

        Assert.IsTrue(outcome.Success);
        byte[] actual = File.ReadAllBytes(outcome.View!.IconUri.LocalPath);
        byte[] bugRef = File.ReadAllBytes(h.Icons!.Place("reference-bug", updated).LocalPath);
        CollectionAssert.AreEqual(bugRef, actual, "an explicit --icon on an update must actually rewrite the file to the new glyph.");
    }

    // ==== AC2: deep-link survives plain updates ==================================

    [TestMethod]
    public void PlainUpdate_NeverCallsUpdateDeepLink_LiveDeepLinkUnchanged()
    {
        var fake = new FakeAppTaskStore();
        var counting = new CountingAppTaskStore(fake);
        using var h = new SemanticEngineHarness();
        var engine = new SemanticEngine(counting, h.Sidecar, h.RecycleBin, h.Gate, SemanticEngineHarness.Ttl, new TaskOperations(counting, h.Sidecar, h.RecycleBin, h.Gate, SemanticEngineHarness.Ttl, h.Logs.Add), log: h.Logs.Add);

        Uri createDeepLink = new("https://example.invalid/create-time-link");
        var created = engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, createDeepLink, "goal", Now, deepLinkExplicit: true);
        Assert.IsTrue(created.Success);
        int callsAfterCreate = counting.UpdateDeepLinkCallCount;

        // A burst of plain (no --deep-link) updates -- the passed deepLink
        // argument is deliberately a DIFFERENT Uri than create-time, standing
        // in for whatever placeholder value a caller might pass when it isn't
        // claimed; it must never reach the store.
        Uri someOtherUri = new("https://example.invalid/some-other-uri");
        engine.Activity("h1", "T", "S", SemanticEngineHarness.IconUri, someOtherUri, ActivityKind.Read, "a.txt", null, null, Now.AddMinutes(1), deepLinkExplicit: false);
        engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, someOtherUri, "goal2", Now.AddMinutes(2), deepLinkExplicit: false);

        Assert.AreEqual(callsAfterCreate, counting.UpdateDeepLinkCallCount, "a plain (non-explicit) update must make ZERO additional UpdateDeepLink store calls.");
        Assert.AreEqual(createDeepLink, fake.Find(created.View!.Id)!.DeepLink, "the live card's deep-link must be unchanged by any plain update.");
    }

    [TestMethod]
    public void ExplicitDeepLinkOnUpdate_DoesWrite()
    {
        var fake = new FakeAppTaskStore();
        var counting = new CountingAppTaskStore(fake);
        using var h = new SemanticEngineHarness();
        var engine = new SemanticEngine(counting, h.Sidecar, h.RecycleBin, h.Gate, SemanticEngineHarness.Ttl, new TaskOperations(counting, h.Sidecar, h.RecycleBin, h.Gate, SemanticEngineHarness.Ttl, h.Logs.Add), log: h.Logs.Add);

        var created = engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, Link, "goal", Now, deepLinkExplicit: true);
        int callsAfterCreate = counting.UpdateDeepLinkCallCount;

        Uri newLink = new("https://example.invalid/new-explicit-link");
        var outcome = engine.Working("h1", "T", "S", SemanticEngineHarness.IconUri, newLink, "goal2", Now.AddMinutes(1), deepLinkExplicit: true);

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual(callsAfterCreate + 1, counting.UpdateDeepLinkCallCount, "an explicit --deep-link update must call UpdateDeepLink exactly once.");
        Assert.AreEqual(newLink, fake.Find(created.View!.Id)!.DeepLink);
    }
}
