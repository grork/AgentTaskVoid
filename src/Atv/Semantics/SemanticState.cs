using Atv.Store;

namespace Atv.Semantics;

/// <summary>
/// LIFE-24's five-state semantic model ("The host-event → task-state
/// integration semantics"), ranked by cost of ignoring: Blocked &gt; Broken
/// &gt; Ready &gt; Working &gt; Idle. Each has a FIXED <see cref="AppTaskState"/>
/// projection (<see cref="SemanticStateMapping"/>) -- deliberately not
/// persisted anywhere of its own: "what state is this card in" is always
/// read back off the live card's own <see cref="AppTaskView.State"/>, so
/// there is exactly one source of truth (no drift risk against a second
/// copy in the sidecar).
///
/// 15A note: <see cref="Idle"/> has NO verb -- it is reachable only via the
/// engine's Ready→Idle presence-gated decay clock, which is 15B's job. No
/// 15A code path ever produces it.
/// </summary>
public enum SemanticState
{
    /// <summary>Mid-work, stalled on the user's decision (permission / question / form). Never decays. Projects to <see cref="AppTaskState.NeedsAttention"/> + <c>SetQuestion</c>.</summary>
    Blocked,

    /// <summary>Turn/session died without delivering. Never decays. Projects to <see cref="AppTaskState.Error"/>.</summary>
    Broken,

    /// <summary>Turn finished; fresh output awaits review. The only decaying state (15B). Projects to <see cref="AppTaskState.Completed"/>.</summary>
    Ready,

    /// <summary>Progressing; needs nothing. Projects to <see cref="AppTaskState.Running"/> + <c>SequenceOfSteps</c>.</summary>
    Working,

    /// <summary>Open, nothing owed either way -- reachable only via decay (15B), no verb claims it directly. Projects to <see cref="AppTaskState.Paused"/>.</summary>
    Idle,
}

/// <summary>Bidirectional mapping between <see cref="SemanticState"/> and the platform-facing <see cref="AppTaskState"/> -- LIFE-24's table, encoded once.</summary>
public static class SemanticStateMapping
{
    public static AppTaskState ToAppTaskState(this SemanticState state) => state switch
    {
        SemanticState.Blocked => AppTaskState.NeedsAttention,
        SemanticState.Broken => AppTaskState.Error,
        SemanticState.Ready => AppTaskState.Completed,
        SemanticState.Working => AppTaskState.Running,
        SemanticState.Idle => AppTaskState.Paused,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown semantic state."),
    };

    public static SemanticState ToSemanticState(this AppTaskState state) => state switch
    {
        AppTaskState.NeedsAttention => SemanticState.Blocked,
        AppTaskState.Error => SemanticState.Broken,
        AppTaskState.Completed => SemanticState.Ready,
        AppTaskState.Running => SemanticState.Working,
        AppTaskState.Paused => SemanticState.Idle,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown platform state."),
    };
}
