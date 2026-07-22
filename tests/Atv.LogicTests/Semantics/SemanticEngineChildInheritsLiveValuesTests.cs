using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.LogicTests.Persistence;
using Codevoid.AgentTaskVoid.Semantics;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Semantics;

/// <summary>
/// Phase 22 Part 1 item 5 / AC8: fan-out child mint and redirected activity
/// must source the PARENT'S OWN LIVE <c>IconUri</c>/<c>DeepLink</c>, not the
/// caller-passed-through arguments -- once the dispatcher stops re-resolving
/// an icon/deep-link on every call, the passed args are no longer guaranteed
/// to equal the parent's real current values. Complements
/// <see cref="SemanticEngineFanOutTests"/> (lifecycle) and
/// <see cref="SemanticEngineActivityRedirectTests"/>'s existing byte-for-byte
/// icon-reuse coverage with the DEEP-LINK half, exercised specifically
/// against a parent whose deep-link is the Part 2 anchor default (NOT the
/// app-data floor) so a regression that fell back to the floor would be
/// caught.
/// </summary>
[TestClass]
public sealed class SemanticEngineChildInheritsLiveValuesTests
{
    private static readonly Uri Icon = SemanticEngineHarness.IconUri;
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    private static RepoDiscoveryResult FakeDiscoveryAt(string anchorPath)
        => new(anchorPath, AnchorSource.CwdFlag, null, anchorPath, RepoConfigParseStatus.NotFound,
            new Dictionary<string, string>(), [], anchorPath, "repo", "main");

    [TestMethod]
    public void MintedChild_DeepLink_EqualsParentsLiveAnchorDeepLink_NotTheFloor()
    {
        using var anchor = new TempDirectory();
        Directory.CreateDirectory(anchor.Path);
        var discovery = FakeDiscoveryAt(anchor.Path);
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery, deepLinkFloor: SemanticEngineHarness.AppDataDeepLinkFloor);

        // Parent create: no explicit --deep-link -- resolves the ANCHOR
        // default (Part 2), which must NOT be the app-data floor here (the
        // anchor exists on disk).
        var parent = h.Engine.Working("session", "T", "S", Icon, SemanticEngineHarness.DeepLink, "goal", Now, iconExplicit: false, deepLinkExplicit: false);
        Uri parentDeepLink = parent.View!.DeepLink;
        Assert.AreNotEqual(SemanticEngineHarness.AppDataDeepLinkFloor, parentDeepLink, "sanity: the parent must have resolved the real anchor default, not the floor.");

        // Fan-out: 2nd concurrent agent-started mints BOTH children. Every
        // call below passes a DIFFERENT, deliberately-wrong deepLink argument
        // (the floor constant) to prove the child sources the PARENT's LIVE
        // value instead of whatever was passed through.
        h.Engine.AgentStarted("session", "T", "S", Icon, SemanticEngineHarness.AppDataDeepLinkFloor, agentId: "a1", name: null, Now.AddMinutes(1), deepLinkExplicit: false);
        h.Engine.AgentStarted("session", "T", "S", Icon, SemanticEngineHarness.AppDataDeepLinkFloor, agentId: "a2", name: null, Now.AddMinutes(2), deepLinkExplicit: false);

        var childA1Entry = h.Sidecar.Read("session#a1")!;
        var childA1 = h.Store.Find(childA1Entry.Id)!;
        Assert.AreEqual(parentDeepLink, childA1.DeepLink, "the minted child must inherit the PARENT's real live deep-link, not the floor constant passed through this call.");
    }

    [TestMethod]
    public void RedirectedActivity_ChildClaim_UsesParentsLiveDeepLink_EvenIfCallerPassesSomethingElse()
    {
        using var anchor = new TempDirectory();
        Directory.CreateDirectory(anchor.Path);
        var discovery = FakeDiscoveryAt(anchor.Path);
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery, deepLinkFloor: SemanticEngineHarness.AppDataDeepLinkFloor);

        var parent = h.Engine.Working("session", "T", "S", Icon, SemanticEngineHarness.DeepLink, "goal", Now, iconExplicit: false, deepLinkExplicit: false);
        Uri parentDeepLink = parent.View!.DeepLink;
        h.Engine.AgentStarted("session", "T", "S", Icon, SemanticEngineHarness.AppDataDeepLinkFloor, agentId: "a1", name: null, Now.AddMinutes(1), deepLinkExplicit: false);
        h.Engine.AgentStarted("session", "T", "S", Icon, SemanticEngineHarness.AppDataDeepLinkFloor, agentId: "a2", name: null, Now.AddMinutes(2), deepLinkExplicit: false);

        // A redirected activity call passing yet another wrong deep-link.
        var redirected = h.Engine.Activity("session", "T", "S", Icon, new Uri("https://example.invalid/wrong-value"), ActivityKind.Read, "foo.txt", agentId: "a1", name: null, Now.AddMinutes(3));

        Assert.AreEqual(parentDeepLink, redirected.View!.DeepLink, "a redirected activity claim's child outcome must report the PARENT's real live deep-link.");
    }
}
