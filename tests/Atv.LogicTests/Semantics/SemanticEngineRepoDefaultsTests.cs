using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.IconRendering;
using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.Semantics;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Semantics;

/// <summary>
/// ERGO-30 (phase 17) AC3/AC4/AC5/AC6 at the <see cref="Codevoid.AgentTaskVoid.Semantics.SemanticEngine"/>
/// level: repo-scoped presentation defaults apply ONLY on the upsert CREATE
/// branch (proven via a counting spy, not just "it looked right"), the
/// five-key allowlist actually applies (title-template, subtitle, icon,
/// icon-file, grouping intent) while disallowed keys are ignored AND logged,
/// grouping produces real byte-identical <see cref="Uri"/> sharing within a
/// repo while staying separate across repos, and the full flag/env/repo/
/// user-file precedence (including the two easy-to-invert orderings).
/// </summary>
[TestClass]
public sealed class SemanticEngineRepoDefaultsTests
{
    private static readonly Uri DefaultIconUri = SemanticEngineHarness.IconUri;
    private static readonly Uri Link = SemanticEngineHarness.DeepLink;
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    private static RepoDiscoveryResult FakeDiscovery(
        IReadOnlyDictionary<string, string>? allowed = null,
        string? repoRootDir = @"C:\repo",
        string? repoName = "repo",
        string? branch = "main",
        RepoConfigParseStatus status = RepoConfigParseStatus.Ok,
        IReadOnlyList<string>? disallowed = null,
        string? configPath = @"C:\repo\.atv.json")
        => new(@"C:\repo", AnchorSource.CwdFlag, configPath, @"C:\repo", status,
            allowed ?? new Dictionary<string, string>(), disallowed ?? [], repoRootDir, repoName, branch);

    // ==== AC3: create-only gating, proven via a counting spy ===================

    [TestMethod]
    public void CreatingANewCard_InvokesDiscoverRepoExactlyOnce()
    {
        int calls = 0;
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => { calls++; return FakeDiscovery(); });

        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        Assert.AreEqual(1, calls, "a genuine creation must trigger discovery exactly once.");
    }

    [TestMethod]
    public void UpdatingALiveCard_NeverInvokesDiscoverRepo_ZeroAdditionalCalls()
    {
        int calls = 0;
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => { calls++; return FakeDiscovery(); });

        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
        Assert.AreEqual(1, calls, "sanity: creation triggered discovery once.");
        // ERGO-33: with no --title anywhere in the chain, the create branch
        // must have resolved the built-in default (FakeDiscovery's anchor and
        // repo root both name "repo" -> the bare anchor name, no parenthetical).
        Assert.AreEqual("repo", h.Store.FindAll().Single().Title, "sanity: the built-in default resolved on create.");

        // Several different verbs, all against the SAME already-live handle --
        // none of them may probe discovery again.
        h.Engine.Activity("session", null, null, DefaultIconUri, Link, ActivityKind.Read, "reading", null, null, Now.AddMinutes(1), iconToken: IconTokens.Default, iconExplicit: false);
        h.Engine.Ready("session", null, null, DefaultIconUri, Link, null, Now.AddMinutes(2), iconToken: IconTokens.Default, iconExplicit: false);
        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal2", Now.AddMinutes(3), iconToken: IconTokens.Default, iconExplicit: false);

        Assert.AreEqual(1, calls, "the update path must perform literally NO discovery probing.");
        Assert.AreEqual("repo", h.Store.FindAll().Single().Title, "the built-in default must stay pinned across updates -- never re-derived.");
    }

    [TestMethod]
    public void EditingRepoFile_MidSession_NeverAffectsAnExistingCard_OnlyTheNextNewOne()
    {
        var discoveryVersion = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyTitleTemplate] = "Version A" });
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discoveryVersion);

        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
        var afterCreate = h.Store.FindAll().Single();
        Assert.AreEqual("Version A", afterCreate.Title);

        // "Edit .atv.json" -- the discovery closure now reports a different value.
        discoveryVersion = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyTitleTemplate] = "Version B" });

        // An UPDATE against the SAME existing handle: must be completely unaffected.
        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal2", Now.AddMinutes(1), iconToken: IconTokens.Default, iconExplicit: false);
        var afterUpdate = h.Store.FindAll().Single();
        Assert.AreEqual("Version A", afterUpdate.Title, "editing .atv.json mid-session must change nothing about a live card.");

        // The NEXT NEW card picks up the edit.
        h.Engine.Working("session-2", null, null, DefaultIconUri, Link, "goal", Now.AddMinutes(2), iconToken: IconTokens.Default, iconExplicit: false);
        var secondCard = h.Store.FindAll().Single(t => t.Title == "Version B");
        Assert.IsNotNull(secondCard);
    }

    // ==== AC4: allowlist enforcement ============================================

    [TestMethod]
    public void TitleTemplate_AppliesFromRepoFile_OnCreate_NoExplicitTitle()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyTitleTemplate] = "{repo}: {branch}" }, repoName: "myrepo", branch: "feature-x");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session", null, "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        var view = h.Store.FindAll().Single();
        Assert.AreEqual("myrepo: feature-x", view.Title);
    }

    [TestMethod]
    public void TitleTemplate_NeverAppliesWhenCallerSuppliedAnExplicitTitle_VerbatimNoExpansion()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyTitleTemplate] = "{repo}: {branch}" });
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session", "Literal {repo} Title", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        var view = h.Store.FindAll().Single();
        Assert.AreEqual("Literal {repo} Title", view.Title, "an explicit --title always wins verbatim, never templated.");
    }

    [TestMethod]
    public void Subtitle_AppliesFromRepoFile_OnCreate()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeySubtitle] = "Repo Subtitle" });
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session", "T", null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        Assert.AreEqual("Repo Subtitle", h.Store.FindAll().Single().Subtitle);
    }

    [TestMethod]
    public void IconToken_AppliesFromRepoFile_OnCreate_RealPixelRender()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyIcon] = "Bug" });
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        // iconExplicit: false -- mirrors "the caller passed no --icon", the
        // ONLY condition under which a repo icon override is even consulted.
        h.Engine.Working("session", "T", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        var view = h.Store.FindAll().Single();
        byte[] actual = File.ReadAllBytes(view.IconUri.LocalPath);
        byte[] bugRefBytes = File.ReadAllBytes(h.Icons!.Place("reference-bug", IconToken.Segoe(IconTokens.CuratedSegoe["Bug"])).LocalPath);
        byte[] defaultRefBytes = File.ReadAllBytes(h.Icons.Place("reference-default", IconTokens.Default).LocalPath);

        CollectionAssert.AreEqual(bugRefBytes, actual, "the repo's icon override must actually render -- byte-identical to a direct Bug-glyph render.");
        CollectionAssert.AreNotEqual(defaultRefBytes, actual, "must NOT be the untouched default glyph.");
    }

    [TestMethod]
    public void IconToken_NeverAppliesWhenCallerExplicitlyRequestedAnIcon()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyIcon] = "Bug" });
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        IconToken explicitToken = IconToken.Segoe(IconTokens.CuratedSegoe["Heart"]);
        Uri placed = h.Icons!.Place("session", explicitToken);
        h.Engine.Working("session", "T", "S", placed, Link, "goal", Now, iconToken: explicitToken, iconExplicit: true);

        var view = h.Store.FindAll().Single();
        byte[] actual = File.ReadAllBytes(view.IconUri.LocalPath);
        byte[] heartRefBytes = File.ReadAllBytes(h.Icons.Place("reference-heart", explicitToken).LocalPath);
        CollectionAssert.AreEqual(heartRefBytes, actual, "the caller's own explicit --icon must never be overridden by repo config.");
    }

    [TestMethod]
    public void IconFile_AppliesFromRepoFile_OnCreate_NormalizedRealRender()
    {
        byte[] source = ShapeRenderer.RenderDefaultShape(128).PngBytes!;
        string sourcePath = Path.Combine(Path.GetTempPath(), $"atv-repo-icon-file-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(sourcePath, source);
        try
        {
            var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyIconFile] = sourcePath });
            using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

            h.Engine.Working("session", "T", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

            var view = h.Store.FindAll().Single();
            byte[] actual = File.ReadAllBytes(view.IconUri.LocalPath);
            byte[] expected = RasterNormalizer.Normalize(source, IconService.DefaultSizePx).PngBytes!;
            CollectionAssert.AreEqual(expected, actual);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public void DisallowedKeys_DeepLinkAndOperationalKnobs_AreIgnored_AndLogged()
    {
        var discovery = FakeDiscovery(
            allowed: new Dictionary<string, string> { [RepoSettings.KeySubtitle] = "S" },
            disallowed: ["deep-link", "idle-running", "watchdog-poll-interval"]);
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        Uri callerDeepLink = new("https://example.invalid/caller-supplied-deep-link");
        h.Engine.Working("session", "T", null, DefaultIconUri, callerDeepLink, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        var view = h.Store.FindAll().Single();
        Assert.AreEqual("S", view.Subtitle, "the allowlisted key must still apply.");
        Assert.AreEqual(callerDeepLink, view.DeepLink, "deep-link must be COMPLETELY unaffected by anything in the repo file -- it isn't even allowlisted.");
        Assert.IsTrue(h.Logs.Any(l => l.Contains("deep-link", StringComparison.Ordinal) && l.Contains("idle-running", StringComparison.Ordinal)),
            "disallowed keys must be ignored AND durably logged, never silently dropped.");
    }

    [TestMethod]
    public void MalformedRepoFile_CreateStillSucceeds_UsesDefaults_LogsTheIssue()
    {
        var discovery = FakeDiscovery(status: RepoConfigParseStatus.Malformed, allowed: new Dictionary<string, string>());
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        var outcome = h.Engine.Working("session", "Explicit Title", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        Assert.IsTrue(outcome.Success, "a malformed repo file must never block a create -- non-disruptive posture.");
        Assert.AreEqual("Explicit Title", h.Store.FindAll().Single().Title);
        Assert.IsTrue(h.Logs.Any(l => l.Contains("Malformed", StringComparison.Ordinal)), "a malformed repo file must produce a durable log entry.");
    }

    // ==== AC4: grouping intent -- real IconUri glomming =========================

    [TestMethod]
    public void Grouping_TwoSessionsSameRepo_ShareOneExactIconUri()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyGroup] = "true" }, repoRootDir: @"C:\repoA", repoName: "repoA");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session-1", "T1", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
        h.Engine.Working("session-2", "T2", "S", DefaultIconUri, Link, "goal", Now.AddMinutes(1), iconToken: IconTokens.Default, iconExplicit: false);

        var uri1 = h.Store.FindAll().Single(t => t.Title == "T1").IconUri;
        var uri2 = h.Store.FindAll().Single(t => t.Title == "T2").IconUri;
        Assert.AreEqual(uri1, uri2, "two sessions in the same repo must share ONE exact icon URI.");
    }

    [TestMethod]
    public void Grouping_DifferentRepos_StayVisuallySeparate_DifferentIconUri()
    {
        var discoveryA = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyGroup] = "true" }, repoRootDir: @"C:\repoA", repoName: "repoA");
        var discoveryB = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyGroup] = "true" }, repoRootDir: @"C:\repoB", repoName: "repoB");

        using var iconsDir = new Persistence.TempDirectory();
        using var recycleDir = new Persistence.TempDirectory();
        using var groupsDir = new Persistence.TempDirectory();
        var icons = new IconService(iconsDir.Path, recycleDir.Path);
        var groupRegistry = new IconGroupRegistry(groupsDir.Path);

        // Two engines sharing the SAME icon/group infrastructure (as production
        // does, one process per repo) but different discovery results -- proves
        // cross-repo separation independent of any single harness's plumbing.
        using var hA = new SemanticEngineHarness(withIcons: false, discoverRepo: () => discoveryA);
        using var hB = new SemanticEngineHarness(withIcons: false, discoverRepo: () => discoveryB);
        var engineA = new Codevoid.AgentTaskVoid.Semantics.SemanticEngine(hA.Store, hA.Sidecar, hA.RecycleBin, hA.Gate, SemanticEngineHarness.Ttl, hA.Ops, icons, hA.Logs.Add, discoverRepo: () => discoveryA, groupRegistry: groupRegistry);
        var engineB = new Codevoid.AgentTaskVoid.Semantics.SemanticEngine(hB.Store, hB.Sidecar, hB.RecycleBin, hB.Gate, SemanticEngineHarness.Ttl, hB.Ops, icons, hB.Logs.Add, discoverRepo: () => discoveryB, groupRegistry: groupRegistry);

        engineA.Working("session-a", "TA", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
        engineB.Working("session-b", "TB", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        var uriA = hA.Store.FindAll().Single().IconUri;
        var uriB = hB.Store.FindAll().Single().IconUri;
        Assert.AreNotEqual(uriA, uriB, "different repos' cards must stay visually separate -- different icon URIs.");
    }

    [TestMethod]
    public void Grouping_WithoutGroupKey_EachSessionGetsItsOwnIconUri_NoAccidentalGlom()
    {
        // Group NOT set -- ordinary per-handle placement, sanity control. Mirrors
        // what Dispatcher actually does: place the fallback icon PER HANDLE
        // before calling the engine (a shared fallback Uri would be a test bug,
        // not a product one -- the product never passes the same fallback Uri
        // for two different handles).
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string>());
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        Uri fallback1 = h.Icons!.Place("session-1", IconTokens.Default);
        Uri fallback2 = h.Icons!.Place("session-2", IconTokens.Default);
        h.Engine.Working("session-1", "T1", "S", fallback1, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
        h.Engine.Working("session-2", "T2", "S", fallback2, Link, "goal", Now.AddMinutes(1), iconToken: IconTokens.Default, iconExplicit: false);

        var uri1 = h.Store.FindAll().Single(t => t.Title == "T1").IconUri;
        var uri2 = h.Store.FindAll().Single(t => t.Title == "T2").IconUri;
        Assert.AreNotEqual(uri1, uri2, "without grouping intent, sessions must NOT accidentally glom.");
    }

    [TestMethod]
    public void Grouping_OwnerRemoved_NextCreateInRepoBecomesTheNewOwner()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyGroup] = "true" });
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session-1", "T1", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
        var uri1 = h.Store.FindAll().Single().IconUri;

        h.Ops.Remove("session-1", Now.AddMinutes(1));
        Assert.IsEmpty(h.Store.FindAll());

        h.Engine.Working("session-2", "T2", "S", DefaultIconUri, Link, "goal", Now.AddMinutes(2), iconToken: IconTokens.Default, iconExplicit: false);
        var uri2 = h.Store.FindAll().Single().IconUri;

        Assert.AreNotEqual(uri1, uri2, "the old owner's file was reaped on remove -- the next creator self-heals into a fresh ownership.");
    }

    [TestMethod]
    public void Grouping_NoGitRootFound_DegradesGracefully_NeverThrows()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyGroup] = "true" }, repoRootDir: null, repoName: null, branch: null);
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        var outcome = h.Engine.Working("session", "T", "S", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        Assert.IsTrue(outcome.Success, "grouping with no discoverable repo root must degrade gracefully, never throw/fail the create.");
    }

    // ==== AC5: full precedence, end-to-end through the engine ==================

    [TestMethod]
    public void Subtitle_RepoBeatsUserFile_EndToEnd()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeySubtitle] = "from-repo" });
        var userFile = new Dictionary<string, string> { [RepoSettings.KeySubtitle] = "from-user" };
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery, presentationUserFile: userFile);

        h.Engine.Working("session", "T", null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        Assert.AreEqual("from-repo", h.Store.FindAll().Single().Subtitle);
    }

    [TestMethod]
    public void Subtitle_EnvBeatsRepo_EndToEnd()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeySubtitle] = "from-repo" });
        var env = new Dictionary<string, string> { [RepoSettings.KeySubtitle] = "from-env" };
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery, presentationEnv: env);

        h.Engine.Working("session", "T", null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        Assert.AreEqual("from-env", h.Store.FindAll().Single().Subtitle);
    }

    [TestMethod]
    public void Subtitle_FlagBeatsEverything_EndToEnd()
    {
        var discovery = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeySubtitle] = "from-repo" });
        var env = new Dictionary<string, string> { [RepoSettings.KeySubtitle] = "from-env" };
        var userFile = new Dictionary<string, string> { [RepoSettings.KeySubtitle] = "from-user" };
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery, presentationEnv: env, presentationUserFile: userFile);

        // The caller's own explicit --subtitle (a non-null `subtitle` param).
        h.Engine.Working("session", "T", "from-flag", DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        Assert.AreEqual("from-flag", h.Store.FindAll().Single().Subtitle);
    }

    [TestMethod]
    public void NoDiscoverRepoWired_DegradesToPrePhase17Behavior_ButIconStillPlaces()
    {
        // No discoverRepo at all -- e.g. every other pre-phase-17 test's harness.
        // Title/subtitle still degrade to "" (unaffected -- unreachable in the
        // shipped CLI, CompositionRoot always wires discoverRepo).
        //
        // Phase 22 deliberate flip: the ICON assertion changes. Part 1 item 3
        // ("create always produces the file") is unconditional -- independent
        // of whether repo-file discovery is wired -- because the dispatcher no
        // longer places at all; if the engine didn't place here either, NO
        // icon file would ever be written for this call. So `view.IconUri` is
        // now a REAL placed file (byte-identical to a direct
        // Icons.Place(handle, IconTokens.Default) render), not the raw
        // fallback Uri constant passed in.
        using var h = new SemanticEngineHarness(withIcons: true);

        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        var view = h.Store.FindAll().Single();
        Assert.AreEqual("", view.Title);
        Assert.AreEqual("", view.Subtitle);
        Assert.AreNotEqual(DefaultIconUri, view.IconUri, "the raw fallback constant must no longer pass through untouched -- the engine now places a real file even with no discoverRepo wired.");
        byte[] actual = File.ReadAllBytes(view.IconUri.LocalPath);
        byte[] expected = File.ReadAllBytes(h.Icons!.Place("reference-default", IconTokens.Default).LocalPath);
        CollectionAssert.AreEqual(expected, actual);
    }

    // ==== ERGO-33 (phase 19B): the never-blank title/subtitle default terminus =

    /// <summary>
    /// Unlike <see cref="FakeDiscovery"/> (which fixes the anchor at
    /// <c>C:\repo</c>, matching the repo root by construction -- fine for the
    /// pre-existing AC3-AC6 tests, which always pass an explicit title/subtitle
    /// to sidestep the default entirely), the ERGO-33 table rows need an
    /// anchor INDEPENDENT of the repo root -- e.g. anchored below it, or with
    /// no repo at all.
    /// </summary>
    private static RepoDiscoveryResult FakeDiscoveryAt(
        string anchorPath,
        IReadOnlyDictionary<string, string>? allowed = null,
        string? repoRootDir = null,
        string? repoName = null,
        string? branch = null)
        => new(anchorPath, AnchorSource.CwdFlag, null, anchorPath, RepoConfigParseStatus.NotFound,
            allowed ?? new Dictionary<string, string>(), [], repoRootDir, repoName, branch);

    [TestMethod]
    public void BuiltInDefault_AnchorEqualsRepoRoot_TitleIsBareAnchorName()
    {
        // Table row 1: C:\Source\AppTaskInfoCli, same .git root -> "AppTaskInfoCli".
        var discovery = FakeDiscoveryAt(@"C:\Source\AppTaskInfoCli", repoRootDir: @"C:\Source\AppTaskInfoCli", repoName: "AppTaskInfoCli", branch: "main");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        var view = h.Store.FindAll().Single();
        Assert.AreEqual("AppTaskInfoCli", view.Title);
        Assert.AreEqual("main", view.Subtitle, "default subtitle is the branch when a git root resolves.");
    }

    [TestMethod]
    public void BuiltInDefault_AnchorBelowRepoRoot_TitleIsAnchorParenRepoName()
    {
        // Table row 2: C:\src\monorepo\packages\web under C:\src\monorepo -> "web (monorepo)".
        var discovery = FakeDiscoveryAt(@"C:\src\monorepo\packages\web", repoRootDir: @"C:\src\monorepo", repoName: "monorepo", branch: "feature-x");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        var view = h.Store.FindAll().Single();
        Assert.AreEqual("web (monorepo)", view.Title);
        Assert.AreEqual("feature-x", view.Subtitle);
    }

    [TestMethod]
    public void BuiltInDefault_NoGitRoot_TitleIsPlainAnchorFolderName_SubtitleEmpty()
    {
        // Table row 3: C:\Users\dhopt\Downloads, no .git anywhere -> "Downloads".
        var discovery = FakeDiscoveryAt(@"C:\Users\dhopt\Downloads", repoRootDir: null, repoName: null, branch: null);
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        var view = h.Store.FindAll().Single();
        Assert.AreEqual("Downloads", view.Title);
        Assert.AreEqual("", view.Subtitle, "no git root -> no branch -> empty default subtitle (subtitle has no never-blank invariant).");
    }

    [TestMethod]
    public void BuiltInDefault_EqualNamesAtDifferentDepth_ParentheticalStillSuppressed()
    {
        // The suppression rule is NAME equality, not path equality: an anchor
        // NESTED below the repo root whose own folder happens to share the
        // repo's name must still render bare, never "myrepo (myrepo)".
        var discovery = FakeDiscoveryAt(@"C:\src\myrepo\myrepo", repoRootDir: @"C:\src\myrepo", repoName: "myrepo", branch: "main");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        Assert.AreEqual("myrepo", h.Store.FindAll().Single().Title, "equal names suppress the parenthetical even when the anchor sits below the root.");
    }

    [TestMethod]
    public void BuiltInDefault_DriveRootAnchor_FallsBackToBrandName()
    {
        // Floor: an anchor with no last path segment (a drive root) has
        // nothing to derive a folder name from -- falls back to the brand.
        var discovery = FakeDiscoveryAt(@"C:\", repoRootDir: null, repoName: null, branch: null);
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        var view = h.Store.FindAll().Single();
        Assert.AreEqual(Codevoid.AgentTaskVoid.Branding.DisplayName, view.Title, "never re-literal the brand -- derived from Branding.DisplayName.");
        Assert.AreEqual("", view.Subtitle);
    }

    [TestMethod]
    public void BuiltInDefault_RepoTemplateExpandsToEmpty_FallsThroughToBuiltInDefault()
    {
        // .atv.json sets "title-template": "{repo}" but no .git root resolves --
        // ERGO-30's token-drop rule expands {repo} to "". Must fall through to
        // the built-in default rather than landing a blank title.
        var discovery = FakeDiscoveryAt(@"C:\work\scratch",
            allowed: new Dictionary<string, string> { [RepoSettings.KeyTitleTemplate] = "{repo}" },
            repoRootDir: null, repoName: null, branch: null);
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);

        Assert.AreEqual("scratch", h.Store.FindAll().Single().Title, "an empty template expansion must fall through to the built-in default, never land blank.");
    }

    [TestMethod]
    public void Title_FullChain_EachLayerWinsInTurn_TerminatingAtTheBuiltInDefault()
    {
        // AC9: extends AC5's precedence coverage (Subtitle_*_EndToEnd above,
        // and SettingsLoaderPresentationTests' ResolvePresentationKey_AllFiveLayers_
        // FlagWinsOverAllFour, which stops at "absence") with the fifth,
        // engine-only rung: --title > env > repo template > user file >
        // built-in default. Proves the default is reached ONLY when every
        // layer above is absent, and never overrides a layer above it.
        var discoveryWithTemplate = FakeDiscovery(allowed: new Dictionary<string, string> { [RepoSettings.KeyTitleTemplate] = "from-repo" }, repoRootDir: @"C:\repo", repoName: "repo");
        var discoveryNoTemplate = FakeDiscovery(repoRootDir: @"C:\repo", repoName: "repo");
        var env = new Dictionary<string, string> { [RepoSettings.KeyTitleTemplate] = "from-env" };
        var userFile = new Dictionary<string, string> { [RepoSettings.KeyTitleTemplate] = "from-user" };

        using (var h1 = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discoveryWithTemplate, presentationEnv: env, presentationUserFile: userFile))
        {
            h1.Engine.Working("s1", "from-flag", null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
            Assert.AreEqual("from-flag", h1.Store.FindAll().Single().Title, "layer 1: an explicit --title beats every layer below it, including the default.");
        }

        using (var h2 = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discoveryWithTemplate, presentationEnv: env, presentationUserFile: userFile))
        {
            h2.Engine.Working("s2", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
            Assert.AreEqual("from-env", h2.Store.FindAll().Single().Title, "layer 2: env beats repo template and user file once the flag is absent.");
        }

        using (var h3 = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discoveryWithTemplate, presentationUserFile: userFile))
        {
            h3.Engine.Working("s3", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
            Assert.AreEqual("from-repo", h3.Store.FindAll().Single().Title, "layer 3: the repo template beats the user file once flag/env are absent.");
        }

        using (var h4 = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discoveryNoTemplate, presentationUserFile: userFile))
        {
            h4.Engine.Working("s4", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
            Assert.AreEqual("from-user", h4.Store.FindAll().Single().Title, "layer 4: the user file is the last resort before the built-in default.");
        }

        using (var h5 = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discoveryNoTemplate))
        {
            h5.Engine.Working("s5", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
            Assert.AreEqual("repo", h5.Store.FindAll().Single().Title, "layer 5: with every layer above absent, the built-in default (anchor folder name) is the terminus.");
        }
    }

    [TestMethod]
    public void ChildCards_NeverReachTheBuiltInDefaultChain_KeepNameOrAgentIdTitle_EmptySubtitle()
    {
        // Locking test (ratified 2026-07-15 boundary): MintChildCard bypasses
        // ApplyRepoDefaults entirely -- name ?? agentId is a BETTER child
        // title than the generic anchor-folder default would be. Wires a
        // real discoverRepo so a regression that accidentally routed
        // children through the default chain would surface as the anchor
        // folder name ("repo") instead of the agent's own name/id.
        var discovery = FakeDiscovery(repoRootDir: @"C:\repo", repoName: "repo", branch: "main");
        using var h = new SemanticEngineHarness(withIcons: true, discoverRepo: () => discovery);

        h.Engine.Working("session", null, null, DefaultIconUri, Link, "goal", Now, iconToken: IconTokens.Default, iconExplicit: false);
        h.Engine.AgentStarted("session", null, null, DefaultIconUri, Link, agentId: "a1", name: "Worker One", Now.AddMinutes(1), iconToken: IconTokens.Default, iconExplicit: false);
        h.Engine.AgentStarted("session", null, null, DefaultIconUri, Link, agentId: "a2", name: null, Now.AddMinutes(2), iconToken: IconTokens.Default, iconExplicit: false);

        var childA1Entry = h.Sidecar.Read("session#a1");
        var childA2Entry = h.Sidecar.Read("session#a2");
        Assert.IsNotNull(childA1Entry);
        Assert.IsNotNull(childA2Entry);
        var childA1 = h.Store.Find(childA1Entry!.Id)!;
        var childA2 = h.Store.Find(childA2Entry!.Id)!;

        Assert.AreEqual("Worker One", childA1.Title, "a named agent keeps its own name as title -- never the repo default.");
        Assert.AreEqual("", childA1.Subtitle, "children never get a subtitle -- the default chain never reaches them.");
        Assert.AreEqual("a2", childA2.Title, "an unnamed agent falls back to its bare agent id -- never the repo default.");
        Assert.AreEqual("", childA2.Subtitle);
    }
}
