using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.Operations;

/// <summary>
/// Which <see cref="AppTaskContentDto"/> factory shape a piece of content was
/// built from -- the row axis of the ERGO-10 safe-combination matrix. Only the
/// two shapes <see cref="AppTaskContentDto"/> currently models
/// (<c>SequenceOfSteps</c>/<c>TextSummaryResult</c> -- see that type's own
/// remarks for why <c>CreatePreviewThumbnail</c>/<c>CreateGeneratedAssetsResult</c>
/// are deferred) have a cell here.
/// </summary>
public enum ContentShape
{
    SequenceOfSteps,
    TextSummaryResult,
}

/// <summary>
/// ERGO-10 ("Guarding unsupported state x content x mutator combinations"):
/// the empirically-documented safe set of (content shape, state, has-question)
/// cells, encoded AS DATA IN ONE PLACE (plan/README.md standing invariant #8).
/// <see cref="Validator"/> is the only consumer -- everything else that needs
/// to know what's safe goes through it, never re-derives this table.
///
/// Encoded verbatim from the "Supported scenarios" table in
/// docs/windows-ui-shell-tasks/state-content-compatibility.md, restricted to
/// the two shapes <see cref="AppTaskContentDto"/> models:
///
/// | Content shape      | State(s)                              | Mutators         |
/// |---------------------|---------------------------------------|-------------------|
/// | SequenceOfSteps     | Running, Completed, Paused, Error      | none              |
/// | SequenceOfSteps     | NeedsAttention                          | SetQuestion only  |
/// | TextSummaryResult   | Completed, Error                       | none              |
///
/// (The doc's <c>CreatePreviewThumbnail</c> row shares identical state/mutator
/// support with <c>CreateSequenceOfSteps</c> but isn't modeled by
/// <see cref="AppTaskContentDto"/> at all, so it has no cell here; likewise
/// <c>CreateGeneratedAssetsResult</c>.)
///
/// Every other (shape, state, hasQuestion) triple in <see cref="AllCells"/> is
/// unsafe -- including <c>TextSummaryResult</c> + a question under ANY state
/// (a real <c>explorer.exe</c> stack-buffer-overrun crash per the doc's crash
/// matrix, unless paired with <c>SetTextInput</c>, which this codebase doesn't
/// model at all) and <c>SequenceOfSteps</c> + <c>NeedsAttention</c> without a
/// question (rejected by the API itself with <c>E_INVALIDARG</c>, never a
/// crash, but still outside the documented safe set so still refused here).
/// </summary>
public static class SafeCombinationMatrix
{
    private static readonly HashSet<(ContentShape Shape, AppTaskState State, bool HasQuestion)> SafeCells =
    [
        (ContentShape.SequenceOfSteps, AppTaskState.Running, false),
        (ContentShape.SequenceOfSteps, AppTaskState.Completed, false),
        (ContentShape.SequenceOfSteps, AppTaskState.Paused, false),
        (ContentShape.SequenceOfSteps, AppTaskState.Error, false),
        (ContentShape.SequenceOfSteps, AppTaskState.NeedsAttention, true),
        (ContentShape.TextSummaryResult, AppTaskState.Completed, false),
        (ContentShape.TextSummaryResult, AppTaskState.Error, false),
    ];

    /// <summary>
    /// Every (shape, state, hasQuestion) triple this codebase can construct --
    /// the exhaustive walk space for the ERGO-10 data-driven test (2 shapes x
    /// 5 states x 2 question-presence values = 20 cells, 7 of them safe).
    /// </summary>
    public static IEnumerable<(ContentShape Shape, AppTaskState State, bool HasQuestion)> AllCells()
    {
        foreach (ContentShape shape in Enum.GetValues<ContentShape>())
            foreach (AppTaskState state in Enum.GetValues<AppTaskState>())
                foreach (bool hasQuestion in new[] { false, true })
                    yield return (shape, state, hasQuestion);
    }

    /// <summary>Whether the given cell is in the documented safe set.</summary>
    public static bool IsSafe(ContentShape shape, AppTaskState state, bool hasQuestion)
        => SafeCells.Contains((shape, state, hasQuestion));

    /// <summary>Maps a concrete <see cref="AppTaskContentDto"/> instance to its <see cref="ContentShape"/> row.</summary>
    public static ContentShape ShapeOf(AppTaskContentDto content) => content switch
    {
        AppTaskContentDto.SequenceOfSteps => ContentShape.SequenceOfSteps,
        AppTaskContentDto.TextSummaryResult => ContentShape.TextSummaryResult,
        _ => throw new NotSupportedException($"Unknown {nameof(AppTaskContentDto)} shape: {content.GetType()}"),
    };
}
