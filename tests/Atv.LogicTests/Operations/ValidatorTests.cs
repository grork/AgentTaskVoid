using Codevoid.AgentTaskVoid.Operations;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Operations;

/// <summary>Covers the non-store half of phase-05 acceptance criterion 1: <see cref="Validator"/>'s Safe/Refused/UnsafeBypassed classification and its <see cref="ValidationResult.Allowed"/> projection.</summary>
[TestClass]
public sealed class ValidatorTests
{
    [TestMethod]
    public void SafeCombination_IsAllowed_WithoutBypass()
    {
        var result = Validator.Validate(new AppTaskContentDto.SequenceOfSteps([], "x"), AppTaskState.Running, bypass: false);
        Assert.AreEqual(ValidationOutcome.Safe, result.Outcome);
        Assert.IsTrue(result.Allowed);
    }

    [TestMethod]
    public void UnsafeCombination_WithoutBypass_IsRefused()
    {
        var result = Validator.Validate(new AppTaskContentDto.TextSummaryResult("x"), AppTaskState.Running, bypass: false);
        Assert.AreEqual(ValidationOutcome.Refused, result.Outcome);
        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public void UnsafeCombination_WithBypass_IsAllowed_ButFlaggedUnsafeBypassed()
    {
        var result = Validator.Validate(new AppTaskContentDto.TextSummaryResult("x"), AppTaskState.Running, bypass: true);
        Assert.AreEqual(ValidationOutcome.UnsafeBypassed, result.Outcome);
        Assert.IsTrue(result.Allowed);
    }

    [TestMethod]
    public void SafeCombination_WithBypassSet_StaysClassifiedSafe_NotUnsafeBypassed()
    {
        // --unsafe is a no-op for a combination that was already safe.
        var result = Validator.Validate(new AppTaskContentDto.SequenceOfSteps([], "x"), AppTaskState.Running, bypass: true);
        Assert.AreEqual(ValidationOutcome.Safe, result.Outcome);
    }

    [TestMethod]
    public void QuestionPresence_IsReadFromContent_NotAnExplicitParameter()
    {
        var withQuestion = new AppTaskContentDto.SequenceOfSteps([], "x") { Question = "continue?" };
        var result = Validator.Validate(withQuestion, AppTaskState.NeedsAttention, bypass: false);
        Assert.AreEqual(ValidationOutcome.Safe, result.Outcome);
        Assert.IsTrue(result.HasQuestion);
    }

    [TestMethod]
    public void ReasonString_IsNonEmpty_ForEveryOutcome()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(Validator.Validate(new AppTaskContentDto.SequenceOfSteps([], ""), AppTaskState.Running, false).Reason));
        Assert.IsFalse(string.IsNullOrWhiteSpace(Validator.Validate(new AppTaskContentDto.TextSummaryResult("x"), AppTaskState.Running, false).Reason));
        Assert.IsFalse(string.IsNullOrWhiteSpace(Validator.Validate(new AppTaskContentDto.TextSummaryResult("x"), AppTaskState.Running, true).Reason));
    }
}
