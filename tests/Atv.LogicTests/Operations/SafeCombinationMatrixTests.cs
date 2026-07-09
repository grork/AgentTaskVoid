using Atv.Operations;
using Atv.Store;

namespace Atv.LogicTests.Operations;

/// <summary>
/// Covers phase-05 acceptance criterion 1's exhaustive half: every one of the
/// 2 shapes x 5 states x 2 question-presence values = 20 cells is walked, and
/// each is asserted against the "Supported scenarios" table in
/// docs/windows-ui-shell-tasks/state-content-compatibility.md, transcribed by
/// hand here as the independent expectation (never by calling into
/// <see cref="SafeCombinationMatrix"/> itself -- that would make the test
/// vacuous).
/// </summary>
[TestClass]
public sealed class SafeCombinationMatrixTests
{
    [TestMethod]
    public void AllCells_HasExactlyTwentyEntries_NoDuplicates()
    {
        var cells = SafeCombinationMatrix.AllCells().ToArray();
        Assert.HasCount(20, cells);
        Assert.HasCount(20, cells.Distinct().ToArray(), "AllCells must not repeat a cell");
    }

    [TestMethod]
    public void ExhaustiveWalk_EveryCellMatchesTheDocTranscribedExpectation()
    {
        foreach (var (shape, state, hasQuestion) in SafeCombinationMatrix.AllCells())
        {
            bool expected = IsSafePerDoc(shape, state, hasQuestion);
            bool actual = SafeCombinationMatrix.IsSafe(shape, state, hasQuestion);
            Assert.AreEqual(expected, actual, $"{shape} x {state} x question={hasQuestion}");
        }
    }

    [TestMethod]
    public void ExactlySevenCellsAreSafe()
    {
        int safeCount = SafeCombinationMatrix.AllCells().Count(c => SafeCombinationMatrix.IsSafe(c.Shape, c.State, c.HasQuestion));
        Assert.AreEqual(7, safeCount);
    }

    // ---- Individually named spot checks (one per doc table row, plus the two "not in the safe table" refusal rules) ----

    [TestMethod]
    public void SequenceOfSteps_NoQuestion_SafeUnder_Running_Completed_Paused_Error()
    {
        Assert.IsTrue(SafeCombinationMatrix.IsSafe(ContentShape.SequenceOfSteps, AppTaskState.Running, false));
        Assert.IsTrue(SafeCombinationMatrix.IsSafe(ContentShape.SequenceOfSteps, AppTaskState.Completed, false));
        Assert.IsTrue(SafeCombinationMatrix.IsSafe(ContentShape.SequenceOfSteps, AppTaskState.Paused, false));
        Assert.IsTrue(SafeCombinationMatrix.IsSafe(ContentShape.SequenceOfSteps, AppTaskState.Error, false));
    }

    [TestMethod]
    public void SequenceOfSteps_WithQuestion_SafeOnlyUnder_NeedsAttention()
    {
        Assert.IsTrue(SafeCombinationMatrix.IsSafe(ContentShape.SequenceOfSteps, AppTaskState.NeedsAttention, true));
        Assert.IsFalse(SafeCombinationMatrix.IsSafe(ContentShape.SequenceOfSteps, AppTaskState.Running, true));
        Assert.IsFalse(SafeCombinationMatrix.IsSafe(ContentShape.SequenceOfSteps, AppTaskState.Completed, true));
        Assert.IsFalse(SafeCombinationMatrix.IsSafe(ContentShape.SequenceOfSteps, AppTaskState.Paused, true));
        Assert.IsFalse(SafeCombinationMatrix.IsSafe(ContentShape.SequenceOfSteps, AppTaskState.Error, true));
    }

    [TestMethod]
    public void SequenceOfSteps_NoQuestion_NeedsAttention_IsNotSafe_ApiWouldReturnInvalidArg()
    {
        Assert.IsFalse(SafeCombinationMatrix.IsSafe(ContentShape.SequenceOfSteps, AppTaskState.NeedsAttention, false));
    }

    [TestMethod]
    public void TextSummaryResult_NoQuestion_SafeOnlyUnder_Completed_Error()
    {
        Assert.IsTrue(SafeCombinationMatrix.IsSafe(ContentShape.TextSummaryResult, AppTaskState.Completed, false));
        Assert.IsTrue(SafeCombinationMatrix.IsSafe(ContentShape.TextSummaryResult, AppTaskState.Error, false));
        Assert.IsFalse(SafeCombinationMatrix.IsSafe(ContentShape.TextSummaryResult, AppTaskState.Running, false));
        Assert.IsFalse(SafeCombinationMatrix.IsSafe(ContentShape.TextSummaryResult, AppTaskState.Paused, false));
        Assert.IsFalse(SafeCombinationMatrix.IsSafe(ContentShape.TextSummaryResult, AppTaskState.NeedsAttention, false));
    }

    [TestMethod]
    public void TextSummaryResult_WithQuestion_NeverSafe_UnderAnyState_CrashMatrix()
    {
        foreach (AppTaskState state in Enum.GetValues<AppTaskState>())
            Assert.IsFalse(SafeCombinationMatrix.IsSafe(ContentShape.TextSummaryResult, state, true), $"TextSummaryResult+question under {state} must be refused -- crashes explorer.exe per the doc's crash matrix");
    }

    [TestMethod]
    public void ShapeOf_MapsDtoInstancesToTheCorrectRow()
    {
        Assert.AreEqual(ContentShape.SequenceOfSteps, SafeCombinationMatrix.ShapeOf(new AppTaskContentDto.SequenceOfSteps([], "")));
        Assert.AreEqual(ContentShape.TextSummaryResult, SafeCombinationMatrix.ShapeOf(new AppTaskContentDto.TextSummaryResult("x")));
    }

    /// <summary>
    /// Independent, hand-transcribed expectation from the doc's "Supported
    /// scenarios" table -- restricted to the two shapes this codebase models.
    /// Deliberately NOT implemented by delegating to
    /// <see cref="SafeCombinationMatrix"/> (that would make the exhaustive
    /// walk test above vacuous).
    /// </summary>
    private static bool IsSafePerDoc(ContentShape shape, AppTaskState state, bool hasQuestion) => (shape, state, hasQuestion) switch
    {
        (ContentShape.SequenceOfSteps, AppTaskState.Running, false) => true,
        (ContentShape.SequenceOfSteps, AppTaskState.Completed, false) => true,
        (ContentShape.SequenceOfSteps, AppTaskState.Paused, false) => true,
        (ContentShape.SequenceOfSteps, AppTaskState.Error, false) => true,
        (ContentShape.SequenceOfSteps, AppTaskState.NeedsAttention, true) => true,
        (ContentShape.TextSummaryResult, AppTaskState.Completed, false) => true,
        (ContentShape.TextSummaryResult, AppTaskState.Error, false) => true,
        _ => false,
    };
}
