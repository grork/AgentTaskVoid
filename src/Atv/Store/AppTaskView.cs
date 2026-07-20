namespace Codevoid.AgentTaskVoid.Store;

/// <summary>
/// DTO mirror of <c>Windows.UI.Shell.Tasks.AppTaskState</c> (INFRA-8: no
/// projected WinRT type crosses the <see cref="IAppTaskStore"/> seam, so this
/// namespace hand-rolls its own copy rather than reusing the CsWinRT-projected
/// enum). Values and ordering match the platform enum 1:1 as of the
/// AppTaskContract v1.0 shape documented in
/// <c>docs/windows-ui-shell-tasks/AppTaskState.md</c> -- <c>AppTaskStore</c>
/// maps between the two explicitly (a switch, not a cast) so a future platform
/// reorder can't silently corrupt state.
/// </summary>
public enum AppTaskState
{
    Running = 0,
    Completed = 1,
    NeedsAttention = 2,
    Paused = 3,
    Error = 4,
}

/// <summary>
/// DTO mirror of <c>Windows.UI.Shell.Tasks.AppTaskContent</c> (write side only
/// -- see <see cref="AppTaskView"/>'s remarks for why there is no read-side
/// equivalent). Mirrors the platform's own shape: exactly one immutable
/// content shape, picked via one of the nested factory-shaped records below,
/// plus the optional <see cref="Question"/> mutator layered on top
/// (<c>AppTaskContent.SetQuestion</c>) -- required for
/// <see cref="AppTaskState.NeedsAttention"/>, valid under any other state too.
/// Use <c>with { Question = "..." }</c> to layer a question onto any shape.
///
/// Only the two shapes v1 verbs need are modeled (ERGO-9: sequence-of-steps
/// and text-summary-result). <c>CreatePreviewThumbnail</c> and
/// <c>CreateGeneratedAssetsResult</c> are deferred, additive shapes -- add a
/// new nested record here first if a later phase needs one. <c>AddButton</c>/
/// <c>SetTextInput</c> are not modeled either: no v1 verb (ERGO-9) uses them,
/// and the ERGO-10 safe-cell validator only ever needs bare
/// <c>SetQuestion</c>.
///
/// The private constructor closes the shape set to the two nested records
/// below, mirroring "not directly constructable, build via exactly one
/// factory method" (<c>docs/windows-ui-shell-tasks/AppTaskContent.md</c>).
/// There is deliberately no convenience merge/append -- every write through
/// <see cref="IAppTaskStore"/> is whole-content replacement, exactly like the
/// platform (ERGO-8; INFRA-15's negative fidelity obligation).
/// </summary>
public abstract record AppTaskContentDto
{
    /// <summary>
    /// <c>AppTaskContent.SetQuestion</c>. <see langword="null"/> = no question
    /// layered on this content. Required (non-null) for
    /// <see cref="AppTaskState.NeedsAttention"/> -- enforcing that is the
    /// ERGO-10 validator's job, above this seam; this DTO itself doesn't
    /// enforce it.
    /// </summary>
    public string? Question { get; init; }

    private AppTaskContentDto() { }

    /// <summary>
    /// Mirrors <c>AppTaskContent.CreateSequenceOfSteps(completedSteps, executingStep)</c>.
    /// Pairs with <see cref="AppTaskView.CompletedSteps"/>/<see cref="AppTaskView.ExecutingStep"/>
    /// when building the next update (the ERGO-8 "advance" model).
    /// </summary>
    public sealed record SequenceOfSteps(IReadOnlyList<string> CompletedSteps, string ExecutingStep) : AppTaskContentDto;

    /// <summary>Mirrors <c>AppTaskContent.CreateTextSummaryResult(text)</c>.</summary>
    public sealed record TextSummaryResult(string Text) : AppTaskContentDto;
}

/// <summary>
/// DTO mirror of <c>Windows.UI.Shell.Tasks.AppTaskInfo</c>'s readable surface
/// -- its get-only properties plus its two content-readback instance methods
/// (<c>GetCompletedSteps()</c>/<c>GetExecutingStep()</c>). Returned by
/// <see cref="IAppTaskStore.FindAll"/>, <see cref="IAppTaskStore.Find"/>, and
/// <see cref="IAppTaskStore.Create"/>.
///
/// Deliberately does NOT expose an arbitrary "current content" readback: the
/// real <c>AppTaskInfo</c> has no such member either (only the two
/// step-content getters below) -- a text-summary result's text, or a question
/// set via <c>SetQuestion</c>, once written, cannot be read back through the
/// platform API at all. Mirroring that asymmetry here (the write side is a
/// full <see cref="AppTaskContentDto"/>, the read side is only these two
/// fields) keeps the seam honest to what the real platform can actually do,
/// rather than inventing a richer one a fake could satisfy but the real
/// adapter couldn't.
/// </summary>
public sealed record AppTaskView(
    string Id,
    string Title,
    string Subtitle,
    AppTaskState State,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    Uri DeepLink,
    Uri IconUri,
    bool HiddenByUser,
    IReadOnlyList<string> CompletedSteps,
    string ExecutingStep);
