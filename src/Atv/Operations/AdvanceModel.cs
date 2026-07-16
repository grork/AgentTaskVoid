using Atv.Store;

namespace Atv.Operations;

/// <summary>
/// ERGO-8's "advance" model: the caller never manages the
/// <c>completedSteps</c> array directly. <see cref="Advance"/> archives the
/// previous executing step into it and sets the new executing step, capping
/// the array at <see cref="MaxCompletedSteps"/> (oldest drops first, a FIFO).
/// A <paramref name="newExecutingStep"/> byte-identical to the current
/// executing step is DROPPED as a no-op, never archived (bug found via live
/// dogfood, 2026-07-15) -- a translator can legitimately submit the same
/// activity claim twice for one real event (Claude Code's <c>PreToolUse</c>
/// AND <c>PostToolUse</c> both map to the same label, since both derive from
/// <c>tool_input</c>, never <c>tool_response</c>), and without this guard
/// every such duplicate call would archive the step it had JUST set,
/// producing a visible doubled entry in the step history.
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
    ///
    /// A no-op short-circuit comes first: when <paramref name="newExecutingStep"/>
    /// is byte-identical to <paramref name="currentExecutingStep"/>, this
    /// returns the input pair completely untouched -- neither archived NOR
    /// re-set. Applying the SAME step twice in a row is indistinguishable from
    /// a duplicate submission of one real event, and archiving a step into its
    /// own history the moment after setting it is never a meaningful claim.
    /// </summary>
    public static AppTaskContentDto.SequenceOfSteps Advance(
        IReadOnlyList<string> currentCompletedSteps, string currentExecutingStep, string newExecutingStep)
    {
        if (newExecutingStep == currentExecutingStep)
            return new AppTaskContentDto.SequenceOfSteps(currentCompletedSteps, currentExecutingStep);

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
