using Atv.Operations;

namespace Atv.LogicTests.Operations;

/// <summary>Covers phase-05 acceptance criterion 2's pure half: the ERGO-8 advance model -- archive-then-set, blank-executing-step is not archived, and the 10-deep FIFO cap.</summary>
[TestClass]
public sealed class AdvanceModelTests
{
    [TestMethod]
    public void FirstStep_FromBlankExecutingStep_DoesNotArchiveTheBlank()
    {
        var result = AdvanceModel.Advance([], "", "step one");

        Assert.IsEmpty(result.CompletedSteps);
        Assert.AreEqual("step one", result.ExecutingStep);
    }

    [TestMethod]
    public void SecondStep_ArchivesThePreviousExecutingStep()
    {
        var first = AdvanceModel.Advance([], "", "step one");
        var second = AdvanceModel.Advance(first.CompletedSteps, first.ExecutingStep, "step two");

        CollectionAssert.AreEqual(new[] { "step one" }, second.CompletedSteps.ToArray());
        Assert.AreEqual("step two", second.ExecutingStep);
    }

    [TestMethod]
    public void SequentialSteps_YieldExecutingEqualsLatest_CompletedStepsInOrder_UpToCap()
    {
        IReadOnlyList<string> completed = [];
        string executing = "";

        for (int i = 1; i <= 10; i++)
        {
            var advanced = AdvanceModel.Advance(completed, executing, $"step{i}");
            completed = advanced.CompletedSteps;
            executing = advanced.ExecutingStep;
        }

        Assert.AreEqual("step10", executing);
        CollectionAssert.AreEqual(
            new[] { "step1", "step2", "step3", "step4", "step5", "step6", "step7", "step8", "step9" },
            completed.ToArray());
    }

    [TestMethod]
    public void MoreThanTenSteps_CapsCompletedStepsAtTen_DroppingOldestFirst()
    {
        IReadOnlyList<string> completed = [];
        string executing = "";

        // 13 sequential steps -- exercises the cap being hit and re-hit.
        for (int i = 1; i <= 13; i++)
        {
            var advanced = AdvanceModel.Advance(completed, executing, $"step{i}");
            completed = advanced.CompletedSteps;
            executing = advanced.ExecutingStep;
        }

        Assert.AreEqual("step13", executing, "executing is always the latest (Nth) step");
        Assert.HasCount(10, completed, "completedSteps is capped at 10");
        CollectionAssert.AreEqual(
            new[] { "step3", "step4", "step5", "step6", "step7", "step8", "step9", "step10", "step11", "step12" },
            completed.ToArray(),
            "the two oldest (step1, step2) have dropped; the 10 most recent completed steps remain, in order");
        CollectionAssert.DoesNotContain(completed.ToArray(), "step1");
        CollectionAssert.DoesNotContain(completed.ToArray(), "step2");
    }

    [TestMethod]
    public void Advance_NeverMutatesTheInputCollection()
    {
        var original = new List<string> { "a", "b" };
        _ = AdvanceModel.Advance(original, "c", "d");

        CollectionAssert.AreEqual(new[] { "a", "b" }, original, "the caller's own list must be untouched -- Advance returns a new list");
    }
}
