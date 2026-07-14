using Atv.Store;

namespace Atv.LogicTests.Semantics;

/// <summary>
/// AC1's idempotency clause applied to the identity flags themselves:
/// absent <c>--title</c>/<c>--subtitle</c> must make NO claim (preserve
/// whatever the card already has), exactly like every other optional field
/// (goal/label/question/summary/detail). This is also a real-adapter-driven
/// fix (phase-15 discovery, see <c>docs/windows-ui-shell-tasks/README.md</c>'s
/// "Local gotchas" and <c>tests/Atv.AdapterTests/SemanticVerbsEndToEndTests.cs</c>'s
/// <c>Activity_OmittedTitleOnFollowUpCall_...</c> test): the real platform's
/// <c>UpdateTitles</c> throws when called with an empty title on an
/// already-live card, so defaulting an absent flag to <c>""</c> and always
/// force-calling <c>UpdateTitles</c> (the naive reading of "every verb
/// accepts identity flags") would crash on the very first follow-up call a
/// real translator makes.
/// </summary>
[TestClass]
public sealed class IdentityClaimTests
{
    private static readonly DateTimeOffset Now = SemanticEngineHarness.Now;
    private static readonly Uri Icon = SemanticEngineHarness.IconUri;
    private static readonly Uri Link = SemanticEngineHarness.DeepLink;

    [TestMethod]
    public void AbsentTitleAndSubtitle_OnFollowUpCall_PreservesTheExistingIdentity()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "Original Title", "Original Sub", Icon, Link, "goal", Now);

        var outcome = h.Engine.Activity("h1", title: null, subtitle: null, Icon, Link, Atv.Semantics.ActivityKind.Read, "x", null, null, Now.AddMinutes(1));

        Assert.AreEqual("Original Title", outcome.View!.Title, "an absent --title must never wipe the existing title to empty.");
        Assert.AreEqual("Original Sub", outcome.View.Subtitle);
    }

    [TestMethod]
    public void ExplicitEmptyTitleIsNeverProduced_ByAnUpsertingVerb_OnAnExistingCard()
    {
        // Regression guard for the real-platform "Title cannot be empty" discovery:
        // even the FIRST call (create) with no --title lands "" (Create tolerates it),
        // but every SUBSEQUENT call with no --title must never re-assert "" via
        // UpdateTitles -- it must fall back to whatever the live title already is.
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", title: null, subtitle: null, Icon, Link, "goal", Now);
        Assert.AreEqual("", h.Store.FindAll().Single().Title, "sanity: Create with no title lands empty.");

        var outcome = h.Engine.Activity("h1", title: null, subtitle: null, Icon, Link, Atv.Semantics.ActivityKind.Shell, "cmd", null, null, Now.AddMinutes(1));

        Assert.IsTrue(outcome.Success, "a follow-up call with no --title on a card whose title is already empty must still succeed, not crash.");
    }

    [TestMethod]
    public void ExplicitTitleChange_OnFollowUpCall_IsApplied()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "First", "S1", Icon, Link, "goal", Now);

        var outcome = h.Engine.Ready("h1", "Second", "S2", Icon, Link, summary: null, Now.AddMinutes(1));

        Assert.AreEqual("Second", outcome.View!.Title);
        Assert.AreEqual("S2", outcome.View.Subtitle);
    }

    [TestMethod]
    public void SubtitleOnlyClaim_PreservesTheExistingTitle()
    {
        using var h = new SemanticEngineHarness();
        h.Engine.Working("h1", "Kept Title", "S1", Icon, Link, "goal", Now);

        var outcome = h.Engine.Activity("h1", title: null, subtitle: "New Sub", Icon, Link, Atv.Semantics.ActivityKind.Read, "x", null, null, Now.AddMinutes(1));

        Assert.AreEqual("Kept Title", outcome.View!.Title);
        Assert.AreEqual("New Sub", outcome.View.Subtitle);
    }
}
