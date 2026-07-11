using Atv.Store;

namespace Atv.Operations;

/// <summary>
/// ERGO-8's "advance" model: the caller never manages the
/// <c>completedSteps</c> array directly. <see cref="Advance"/> archives the
/// previous executing step into it and sets the new executing step, capping
/// the array at <see cref="MaxCompletedSteps"/> (oldest drops first, a FIFO).
///
/// The read half of the RMW (fetching the current steps to pass in here)
/// always comes from the live store (<see cref="TaskOperations"/>'s job),
/// never a cache -- this type is pure and stateless, taking exactly the
/// values the caller read.
/// </summary>
public static class AdvanceModel
{
    /// <summary>The FIFO cap on <c>completedSteps</c> (ERGO-8) -- keeps a long session's card from growing unbounded.</summary>
    public const int MaxCompletedSteps = 10;

    /// <summary>
    /// The non-empty placeholder <see cref="TaskOperations"/> uses (in place
    /// of <c>""</c>) for every "no real step content yet" baseline -- a
    /// brand-new <c>start</c>, a <c>--reset</c>, an icon-forced Remove+Create,
    /// or a resurrection's fresh content. Required because the real
    /// platform's <c>AppTaskContent.CreateSequenceOfSteps</c> throws
    /// <c>E_INVALIDARG</c> for an empty <c>executingStep</c> (empirically
    /// discovered via phase 08's real-adapter suite, 2026-07-09 -- see
    /// docs/windows-ui-shell-tasks/AppTaskContent.md's gotcha). Recognized
    /// here, alongside a genuinely blank string, as "nothing meaningful to
    /// archive" -- otherwise the first real <see cref="Advance"/> after a
    /// fresh <c>start</c> would wrongly archive this placeholder text into
    /// <c>completedSteps</c> as if it were a real step.
    /// </summary>
    public const string NoStepsYetPlaceholder = "Not started yet.";

    /// <summary>
    /// Archives <paramref name="currentExecutingStep"/> onto the end of
    /// <paramref name="currentCompletedSteps"/> (dropping the oldest entry if
    /// that would exceed <see cref="MaxCompletedSteps"/>), then sets
    /// <paramref name="newExecutingStep"/> as the new executing step. A blank
    /// (or <see cref="NoStepsYetPlaceholder"/>) current executing step (a
    /// freshly-created or just-resurrected card that has never had a step
    /// set) is NOT archived -- there is nothing meaningful to record.
    /// </summary>
    public static AppTaskContentDto.SequenceOfSteps Advance(
        IReadOnlyList<string> currentCompletedSteps, string currentExecutingStep, string newExecutingStep)
    {
        var next = new List<string>(currentCompletedSteps);
        if (!string.IsNullOrEmpty(currentExecutingStep) && currentExecutingStep != NoStepsYetPlaceholder)
        {
            next.Add(currentExecutingStep);
            if (next.Count > MaxCompletedSteps)
                next.RemoveAt(0);
        }

        return new AppTaskContentDto.SequenceOfSteps(next, newExecutingStep);
    }
}
