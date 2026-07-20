using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.Operations;

/// <summary>How <see cref="Validator.Validate"/> classified a (content, state) pair.</summary>
public enum ValidationOutcome
{
    /// <summary>In the documented safe set (<see cref="SafeCombinationMatrix"/>).</summary>
    Safe,

    /// <summary>Outside the safe set, but let through anyway because the caller passed <c>--unsafe</c>.</summary>
    UnsafeBypassed,

    /// <summary>Outside the safe set and not bypassed -- the caller must not write this combination.</summary>
    Refused,
}

/// <summary>Result of one <see cref="Validator.Validate"/> call, with a human-readable reason suitable for logging/`--json`.</summary>
public readonly record struct ValidationResult(ValidationOutcome Outcome, ContentShape Shape, AppTaskState State, bool HasQuestion, string Reason)
{
    /// <summary><see langword="true"/> for <see cref="ValidationOutcome.Safe"/> or <see cref="ValidationOutcome.UnsafeBypassed"/> -- the caller may proceed to write.</summary>
    public bool Allowed => Outcome != ValidationOutcome.Refused;
}

/// <summary>
/// ERGO-10's enforcement point: every content-emitting write in
/// <see cref="TaskOperations"/> passes its (content, state) pair through
/// <see cref="Validate"/> before touching the store. Reads
/// <see cref="SafeCombinationMatrix"/> -- the one encoding of the doc-derived
/// safe set -- never re-derives it itself.
///
/// <c>--unsafe</c> (the <paramref name="bypass"/> parameter below) lets a
/// documented-unsafe combination through anyway; off by default
/// (experimentation only, per ERGO-10's decision record). The fake backing
/// every test here does NOT model crash-on-bad-combo (INFRA-15's "must not"
/// list) -- this validator is our own guard, not something the platform
/// enforces for us.
/// </summary>
public static class Validator
{
    public static ValidationResult Validate(AppTaskContentDto content, AppTaskState state, bool bypass)
    {
        ContentShape shape = SafeCombinationMatrix.ShapeOf(content);
        bool hasQuestion = content.Question is not null;
        bool safe = SafeCombinationMatrix.IsSafe(shape, state, hasQuestion);

        if (safe)
            return new ValidationResult(ValidationOutcome.Safe, shape, state, hasQuestion,
                $"{shape} x {state} x question={hasQuestion} is in the documented safe set (state-content-compatibility.md).");

        if (bypass)
            return new ValidationResult(ValidationOutcome.UnsafeBypassed, shape, state, hasQuestion,
                $"{shape} x {state} x question={hasQuestion} is OUTSIDE the documented safe set -- emitted anyway (--unsafe).");

        return new ValidationResult(ValidationOutcome.Refused, shape, state, hasQuestion,
            $"{shape} x {state} x question={hasQuestion} is outside the documented safe set (state-content-compatibility.md); refused. Pass --unsafe to bypass.");
    }
}
