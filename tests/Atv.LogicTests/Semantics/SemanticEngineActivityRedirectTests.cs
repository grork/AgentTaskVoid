using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.LogicTests.Persistence;
using Codevoid.AgentTaskVoid.Operations;
using Codevoid.AgentTaskVoid.Semantics;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Semantics;

/// <summary>
/// Phase 19A (ERGO-31 §5's redirect, root-caused in the phase-18 live
/// dogfood): a carded subagent's own <c>activity</c> claim must land its
/// CONTENT on the child's own card, not the parent's, while the parent's
/// same-locus block-clearing still runs on the parent (decision points 1-5
/// in <c>plan/phase-19-card-fidelity.md</c>). Companion to
/// <see cref="SemanticEngineFanOutTests"/>'s lifecycle-only coverage
/// (mint/retire/cascade, zero <c>Activity</c> occurrences) -- this file is
/// the missing content half: the redirect itself (AC1-6) plus the phase-15
/// baseline that should have existed already (AC7) -- a child's own
/// activity/ready range, exercised with real content, not just state
/// transitions.
/// </summary>
[TestClass]
public sealed class SemanticEngineActivityRedirectTests
{
    private static readonly Uri Icon = SemanticEngineHarness.IconUri;
    private static readonly Uri Link = SemanticEngineHarness.DeepLink;
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;

    private static AppTaskView? FindChild(SemanticEngineHarness h, string childHandle)
    {
        var entry = h.Sidecar.Read(childHandle);
        return entry is null ? null : h.Store.Find(entry.Id);
    }

    private static AppTaskView Parent(SemanticEngineHarness h, string handle)
        => h.Store.Find(h.Sidecar.Read(handle)!.Id)!;

    /// <summary>Working, then two concurrent `agent-started` calls -- carding both a1/a2 (the 2nd-concurrent-start mint, retroactively carding the 1st too).</summary>
    private static void CardTwoAgents(SemanticEngineHarness h, string session, string a1 = "a1", string a2 = "a2")
    {
        h.Engine.Working(session, "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted(session, "T", "S", Icon, Link, agentId: a1, name: null, Now.AddMinutes(1));
        h.Engine.AgentStarted(session, "T", "S", Icon, Link, agentId: a2, name: null, Now.AddMinutes(2));
    }

    // ---- AC1: redirect ---------------------------------------------------------

    [TestMethod]
    public void Redirect_LandsOnChildsExecutingStep_ParentStepsByteUnchanged()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");
        var parentBefore = Parent(h, "session");

        h.Engine.Activity("session", "T", "S", Icon, Link, ActivityKind.Read, "foo.txt", agentId: "a1", name: null, Now.AddMinutes(3));

        var child = FindChild(h, "session#a1")!;
        Assert.AreEqual(Rendering.BuildActivityLine(ActivityKind.Read, "foo.txt", null), child.ExecutingStep, "the activity line must land on the child's own executing step.");

        var parentAfter = Parent(h, "session");
        Assert.AreEqual(parentBefore.ExecutingStep, parentAfter.ExecutingStep, "the parent's own executing step must be byte-unchanged by a redirect.");
        CollectionAssert.AreEqual(parentBefore.CompletedSteps.ToArray(), parentAfter.CompletedSteps.ToArray(), "the parent's own completed steps must be byte-unchanged by a redirect.");
    }

    // ---- AC2: equivalence -------------------------------------------------------

    [TestMethod]
    public void Redirect_ProducesIdenticalChildCard_ToADirectChildAddressedCall()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "s1");
        CardTwoAgents(h, "s2");

        // Neither call carries --title/--subtitle -- a translator never sends
        // identity flags on a plain activity call, direct or redirected, so
        // this is the realistic shape for both sides of the comparison (the
        // redirect path never re-titles the child either -- see
        // ApplyRedirectedActivity's remarks).
        var redirectedOutcome = h.Engine.Activity("s1", null, null, Icon, Link, ActivityKind.Edit, "bar.cs", agentId: "a1", name: null, Now.AddMinutes(3));
        var directOutcome = h.Engine.Activity("s2#a1", null, null, Icon, Link, ActivityKind.Edit, "bar.cs", agentId: null, name: null, Now.AddMinutes(3));

        var redirected = FindChild(h, "s1#a1")!;
        var direct = FindChild(h, "s2#a1")!;

        Assert.AreEqual(direct.Title, redirected.Title);
        Assert.AreEqual(direct.Subtitle, redirected.Subtitle);
        Assert.AreEqual(direct.State, redirected.State);
        Assert.AreEqual(direct.ExecutingStep, redirected.ExecutingStep);
        CollectionAssert.AreEqual(direct.CompletedSteps.ToArray(), redirected.CompletedSteps.ToArray());
        Assert.AreEqual(direct.IconUri, redirected.IconUri);

        // The OUTCOME itself, not just the store's end state, must be
        // equivalent -- decision point 2's "exact equivalence" made
        // observable on what the caller gets back.
        Assert.AreEqual(directOutcome.Kind, redirectedOutcome.Kind);
        Assert.AreEqual(direct.ExecutingStep, redirectedOutcome.View!.ExecutingStep);
        Assert.AreEqual("s1#a1", redirectedOutcome.Handle, "the redirected outcome must report the CHILD handle -- what a direct child-addressed call would also report.");
    }

    // ---- AC3: locus bookkeeping survives the redirect ---------------------------

    [TestMethod]
    public void Redirect_ClearsParentsPendingBlockForThatAgent_WhileContentLandsOnChild()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");
        h.Engine.Blocked("session", "T", "S", Icon, Link, "Can I proceed?", agentId: "a1", Now.AddMinutes(3));
        Assert.AreEqual(AppTaskState.NeedsAttention, Parent(h, "session").State, "sanity: a1's question landed on the parent.");

        h.Engine.Activity("session", "T", "S", Icon, Link, ActivityKind.Read, "foo.txt", agentId: "a1", name: null, Now.AddMinutes(4));

        Assert.AreEqual(AppTaskState.Running, Parent(h, "session").State, "a1's own activity must clear a1's pending block -- the only pending locus, so the parent re-enters Working.");
        var child = FindChild(h, "session#a1")!;
        Assert.AreEqual(Rendering.BuildActivityLine(ActivityKind.Read, "foo.txt", null), child.ExecutingStep, "the line itself still lands on the child.");
    }

    [TestMethod]
    public void Redirect_ConcurrentBlocks_ClearingOneLocus_LeavesTheOtherStillBlocking()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");
        h.Engine.Blocked("session", "T", "S", Icon, Link, "a1 question", agentId: "a1", Now.AddMinutes(3));
        h.Engine.Blocked("session", "T", "S", Icon, Link, "a2 question", agentId: "a2", Now.AddMinutes(4));
        Assert.AreEqual(AppTaskState.NeedsAttention, Parent(h, "session").State, "sanity: both loci are pending.");

        h.Engine.Activity("session", "T", "S", Icon, Link, ActivityKind.Read, "foo.txt", agentId: "a1", name: null, Now.AddMinutes(5));

        Assert.AreEqual(AppTaskState.NeedsAttention, Parent(h, "session").State, "a2 is still blocking -- the parent must stay Blocked, showing a2's question.");
    }

    // ---- AC4: sibling isolation --------------------------------------------------

    [TestMethod]
    public void Redirect_TouchesOnlyThatAgentsCard_SiblingAndParentUntouched()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");
        var siblingBefore = FindChild(h, "session#a2")!;
        var parentBefore = Parent(h, "session");

        h.Engine.Activity("session", "T", "S", Icon, Link, ActivityKind.Read, "foo.txt", agentId: "a1", name: null, Now.AddMinutes(3));

        var siblingAfter = FindChild(h, "session#a2")!;
        Assert.AreEqual(siblingBefore.ExecutingStep, siblingAfter.ExecutingStep, "sibling B's card must be untouched by A's activity.");
        Assert.AreEqual(siblingBefore.State, siblingAfter.State);

        var parentAfter = Parent(h, "session");
        Assert.AreEqual(parentBefore.ExecutingStep, parentAfter.ExecutingStep, "the parent must be untouched (beyond locus bookkeeping, inert here since nothing was blocked).");
        Assert.AreEqual(parentBefore.State, parentAfter.State);
    }

    // ---- AC5: fallbacks -----------------------------------------------------------

    [TestMethod]
    public void UncardedAgentId_NeverCarded_LandsOnTheAddressedParentHandle()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("session", "T", "S", Icon, Link, "goal", Now);
        h.Engine.AgentStarted("session", "T", "S", Icon, Link, agentId: "solo", name: null, Now.AddMinutes(1)); // solo -- never carded (needs a 2nd concurrent worker).

        h.Engine.Activity("session", "T", "S", Icon, Link, ActivityKind.Read, "foo.txt", agentId: "ghost", name: null, Now.AddMinutes(2));

        Assert.IsNull(FindChild(h, "session#ghost"), "an uncarded agent id must never mint a card.");
        Assert.AreEqual(Rendering.BuildActivityLine(ActivityKind.Read, "foo.txt", null), Parent(h, "session").ExecutingStep, "falls back to the addressed (parent) handle.");
    }

    [TestMethod]
    public void RetiredAgentsLateActivity_NeverResurrectsItsChildCard_LandsOnParent()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");
        h.Engine.AgentStopped("session", "T", "S", Icon, Link, agentId: "a1", Now.AddMinutes(3));
        Assert.IsNull(FindChild(h, "session#a1"), "sanity: a1 retired.");

        h.Engine.Activity("session", "T", "S", Icon, Link, ActivityKind.Read, "foo.txt", agentId: "a1", name: null, Now.AddMinutes(4));

        Assert.IsNull(FindChild(h, "session#a1"), "a retired agent's late activity must never resurrect its child card.");
        Assert.AreEqual(Rendering.BuildActivityLine(ActivityKind.Read, "foo.txt", null), Parent(h, "session").ExecutingStep, "lands on the parent instead.");
    }

    [TestMethod]
    public void ActivityWithNoAgent_Unchanged_ChildrenUntouched()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");

        h.Engine.Activity("session", "T", "S", Icon, Link, ActivityKind.Read, "foo.txt", agentId: null, name: null, Now.AddMinutes(3));

        Assert.AreEqual(Rendering.BuildActivityLine(ActivityKind.Read, "foo.txt", null), Parent(h, "session").ExecutingStep);
        Assert.AreEqual(AdvanceModel.NoStepsYetPlaceholder, FindChild(h, "session#a1")!.ExecutingStep, "no --agent -- neither child may be touched.");
        Assert.AreEqual(AdvanceModel.NoStepsYetPlaceholder, FindChild(h, "session#a2")!.ExecutingStep);
    }

    [TestMethod]
    public void NameOnlyActivity_NoAgentId_StillRendersOnTheParentsStream()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");

        h.Engine.Activity("session", "T", "S", Icon, Link, ActivityKind.Tool, label: null, agentId: null, name: "mcp__jira__create_ticket", Now.AddMinutes(3));

        Assert.AreEqual(Rendering.BuildActivityLine(ActivityKind.Tool, null, "mcp__jira__create_ticket"), Parent(h, "session").ExecutingStep);
    }

    // ---- AC6: invariants under redirect --------------------------------------------

    [TestMethod]
    public void Redirect_ReusesChildsIconUriByteForByte_NeverCallsCreate_SameValidatorPath()
    {
        using var h = new SemanticEngineHarness(withIcons: true);
        var countingStore = new CountingAppTaskStore(h.Store);
        var engine = new SemanticEngine(countingStore, h.Sidecar, h.RecycleBin, h.Gate, SemanticEngineHarness.Ttl, h.Ops, h.Icons, h.Logs.Add, groupRegistry: h.GroupRegistry);

        // Mirrors what the CLI resolves for the ADDRESSED (parent) handle
        // before calling into the engine (Dispatcher.ActivityBody's own
        // `_icons.Place(handle, token)` step) -- a REAL rendered/placed icon
        // file, not just a bare test Uri constant.
        Uri placedIcon = h.Icons!.Place("session", IconTokens.Default);

        engine.Working("session", "T", "S", placedIcon, Link, "goal", Now);
        engine.AgentStarted("session", "T", "S", placedIcon, Link, agentId: "a1", name: null, Now.AddMinutes(1));
        engine.AgentStarted("session", "T", "S", placedIcon, Link, agentId: "a2", name: null, Now.AddMinutes(2));

        var childBefore = FindChild(h, "session#a1")!;
        Assert.AreEqual(placedIcon, childBefore.IconUri, "sanity: the child was minted reusing the parent's resolved icon URI.");

        int createsBefore = countingStore.CreateCallCount;

        var outcome = engine.Activity("session", "T", "S", placedIcon, Link, ActivityKind.Read, "foo.txt", agentId: "a1", name: null, Now.AddMinutes(3));

        var childAfter = FindChild(h, "session#a1")!;
        Assert.AreEqual(Rendering.BuildActivityLine(ActivityKind.Read, "foo.txt", null), childAfter.ExecutingStep, "sanity: this really is the redirect -- the content actually moved to the child.");
        Assert.AreEqual(createsBefore, countingStore.CreateCallCount,
            "a content-only redirect must never call IAppTaskStore.Create -- the only path IconService.Place is reachable from inside the engine, so this structurally proves no re-mint/re-place happened.");
        Assert.AreEqual(placedIcon, childAfter.IconUri, "the child's icon URI must be reused byte-for-byte, never re-placed.");
        Assert.IsTrue(outcome.Success, "the redirected claim must pass the same validator path a direct child call takes.");
        Assert.AreEqual(OutcomeKind.Accepted, outcome.Kind);
    }

    // ---- AC7: the missing baseline (should have existed since phase 15) -----------

    [TestMethod]
    public void FreshlyMintedChild_ContentIsTheBareNotStartedYetBaseline()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");

        var child = FindChild(h, "session#a1")!;
        Assert.AreEqual(AdvanceModel.NoStepsYetPlaceholder, child.ExecutingStep);
        Assert.IsEmpty(child.CompletedSteps);
        Assert.AreEqual(AppTaskState.Running, child.State);
    }

    [TestMethod]
    public void DirectActivityCall_AgainstAChildHandle_LandsOnThatChild()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");

        var outcome = h.Engine.Activity("session#a1", "T", "S", Icon, Link, ActivityKind.Read, "foo.cs", agentId: null, name: null, Now.AddMinutes(3));

        Assert.IsTrue(outcome.Success);
        var child = FindChild(h, "session#a1")!;
        Assert.AreEqual(Rendering.BuildActivityLine(ActivityKind.Read, "foo.cs", null), child.ExecutingStep);
        Assert.AreEqual(AppTaskState.Running, child.State);
        Assert.AreEqual(AdvanceModel.NoStepsYetPlaceholder, FindChild(h, "session#a2")!.ExecutingStep, "sibling untouched.");
    }

    [TestMethod]
    public void ChildReachesReadyViaItsOwnReadyCall_AndReturnsToWorkingOnFurtherActivity_RealContentExercised()
    {
        using var h = new SemanticEngineHarness();
        CardTwoAgents(h, "session");

        h.Engine.Activity("session#a1", "T", "S", Icon, Link, ActivityKind.Read, "foo.cs", agentId: null, name: null, Now.AddMinutes(3));
        Assert.AreEqual(AppTaskState.Running, FindChild(h, "session#a1")!.State);

        var readyOutcome = h.Engine.Ready("session#a1", "T", "S", Icon, Link, summary: "Reviewed foo.cs", Now.AddMinutes(4));
        Assert.IsTrue(readyOutcome.Success);
        Assert.AreEqual(AppTaskState.Completed, FindChild(h, "session#a1")!.State, "the child's own `ready` call must land it in Ready.");

        h.Engine.Activity("session#a1", "T", "S", Icon, Link, ActivityKind.Edit, "bar.cs", agentId: null, name: null, Now.AddMinutes(5));

        var childAfter = FindChild(h, "session#a1")!;
        Assert.AreEqual(AppTaskState.Running, childAfter.State, "further activity after Ready must return the child to Working.");
        Assert.AreEqual(Rendering.BuildActivityLine(ActivityKind.Edit, "bar.cs", null), childAfter.ExecutingStep);
    }
}
