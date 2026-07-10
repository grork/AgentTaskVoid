using Atv.Cli.Verbs;
using Atv.Config;
using Atv.Diagnostics;
using Atv.Icons;
using Atv.Operations;
using Atv.Run;
using Atv.Store;

namespace Atv.Cli;

/// <summary>
/// Dispatches one already-parsed <see cref="ParseResult"/> to the matching
/// verb body. The seven lifecycle verbs are inline private methods over an
/// injected <see cref="TaskOperations"/> core -- the fake-testable half of
/// the CLI (everything real-only lives in <see cref="CompositionRoot"/>).
/// The three phase-10 utility verbs (`list`/`clear`/`doctor`) are dedicated
/// static classes in <see cref="Atv.Cli.Verbs"/> that each own their own
/// <see cref="Posture"/> call instead -- `list`/`doctor` need
/// <see cref="Posture.RunQuery"/> (their own `--json` shape, ERGO-27 C5),
/// not the generic mutating-verb wrapper every lifecycle verb (and `clear`)
/// uses.
///
/// Exactly one <see cref="Posture.Run"/>/<see cref="Posture.RunQuery"/> call
/// per verb invocation wraps: (1) pure argument-shape validation (handle
/// presence, icon-token/deep-link-URI parsing, the C7 running|paused
/// restriction) -- runs first and unconditionally, independent of platform
/// state; (2) <see cref="Capability.Check"/> (identity, then API support --
/// skipped entirely by `doctor`, whose whole job is diagnosing exactly that);
/// (3) the LIFE-17/INFRA-19 <see cref="Atv.Watchdog.EnsureWatchdog.Run"/>
/// liveness gate on every WRITE-path verb (lifecycle verbs + `clear`, never
/// `list`/`doctor`); (4) the actual operation. Every failure mode -- bad
/// args, platform down, ERGO-10 validator refusal, unknown handle --
/// therefore goes through the identical non-disruptive pipe (ERGO-27: "all
/// behavior identical... only the logged reason and strict exit code
/// differ").
/// </summary>
public sealed class Dispatcher
{
    private readonly TaskOperations _ops;
    private readonly Posture _posture;
    private readonly Output _output;
    private readonly IconService _icons;
    private readonly Uri _defaultDeepLink;
    private readonly Func<bool> _hasIdentity;
    private readonly Func<bool> _isSupported;
    private readonly Action _ensureWatchdog;
    private readonly DoctorContext _doctorContext;
    private readonly Settings _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<TimeSpan> _sleep;
    private readonly Func<IReadOnlyList<string>, IChildProcess> _spawnChild;
    private readonly Stream _stdoutMirror;
    private readonly Stream _stderrMirror;

    public Dispatcher(
        TaskOperations ops,
        Posture posture,
        Output output,
        IconService icons,
        Uri defaultDeepLink,
        Func<bool> hasIdentity,
        Func<bool> isSupported,
        Action ensureWatchdog,
        DoctorContext doctorContext,
        Settings settings,
        Func<DateTimeOffset> clock,
        Action<TimeSpan> sleep,
        Func<IReadOnlyList<string>, IChildProcess> spawnChild,
        Stream stdoutMirror,
        Stream stderrMirror)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        _posture = posture ?? throw new ArgumentNullException(nameof(posture));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _defaultDeepLink = defaultDeepLink ?? throw new ArgumentNullException(nameof(defaultDeepLink));
        _hasIdentity = hasIdentity ?? throw new ArgumentNullException(nameof(hasIdentity));
        _isSupported = isSupported ?? throw new ArgumentNullException(nameof(isSupported));
        _ensureWatchdog = ensureWatchdog ?? throw new ArgumentNullException(nameof(ensureWatchdog));
        _doctorContext = doctorContext ?? throw new ArgumentNullException(nameof(doctorContext));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _sleep = sleep ?? throw new ArgumentNullException(nameof(sleep));
        _spawnChild = spawnChild ?? throw new ArgumentNullException(nameof(spawnChild));
        _stdoutMirror = stdoutMirror ?? throw new ArgumentNullException(nameof(stdoutMirror));
        _stderrMirror = stderrMirror ?? throw new ArgumentNullException(nameof(stderrMirror));
    }

    /// <summary>Dispatches one lifecycle-verb invocation. Callers must have already handled <see cref="ParseResult.ShowHelp"/>/<see cref="ParseResult.ShowVersion"/>/a bare (no-verb) invocation -- those never reach here (Program.cs's job, needs no identity/platform/Posture at all).</summary>
    public int Run(ParseResult parsed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        if (parsed.ShowHelp || parsed.ShowVersion)
            throw new ArgumentException("Help/version requests must be handled by the caller before reaching Dispatcher.Run.", nameof(parsed));

        if (parsed.Error is not null)
            return _posture.Run(parsed.Verb ?? "(parse)", FirstOrNull(parsed.Positionals), () => VerbResult.Failure(FailureKind.InvalidArguments, parsed.Error), now);

        return parsed.Verb switch
        {
            "start" => _posture.Run("start", FirstOrNull(parsed.Positionals), () => StartBody(parsed, now), now),
            "step" => _posture.Run("step", FirstOrNull(parsed.Positionals), () => StepBody(parsed, now), now),
            "state" => _posture.Run("state", FirstOrNull(parsed.Positionals), () => StateBody(parsed, now), now),
            "attention" => _posture.Run("attention", FirstOrNull(parsed.Positionals), () => AttentionBody(parsed, now), now),
            "done" => _posture.Run("done", FirstOrNull(parsed.Positionals), () => DoneBody(parsed, now), now),
            "fail" => _posture.Run("fail", FirstOrNull(parsed.Positionals), () => FailBody(parsed, now), now),
            "remove" => _posture.Run("remove", FirstOrNull(parsed.Positionals), () => RemoveBody(parsed, now), now),
            "list" => ListVerb.Run(_output, _posture, _hasIdentity, _isSupported, _ops, now),
            "clear" => ClearVerb.Run(_posture, _hasIdentity, _isSupported, _ensureWatchdog, _ops, parsed.IncludeRecycleBin, now),
            "doctor" => DoctorVerb.Run(_output, _posture, _doctorContext, now),
            "run" => RunVerb.Run(BuildRunDeps(), parsed, now),
            null => throw new ArgumentException("A bare (no-verb) invocation must be handled by the caller before reaching Dispatcher.Run.", nameof(parsed)),
            _ => _posture.Run(parsed.Verb, null, () => VerbResult.Failure(FailureKind.InvalidArguments, $"Unknown verb '{parsed.Verb}'."), now),
        };
    }

    // ---- start ------------------------------------------------------------------

    private VerbResult StartBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetSingleHandle(p, out string handle, out var handleErr)) return handleErr!.Value;
        if (!TryResolveDeepLink(p, out Uri deepLink, out var deepLinkErr)) return deepLinkErr!.Value;
        if (!TryResolveIconToken(p, out IconToken token, out var iconErr)) return iconErr!.Value;

        string title = p.Flags.GetValueOrDefault("title", "");
        string subtitle = p.Flags.GetValueOrDefault("subtitle", "");

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        Uri iconUri = _icons.Place(handle, token);
        var outcome = _ops.Start(handle, title, subtitle, iconUri, deepLink, now, reset: p.Reset, unsafeBypass: p.Global.Unsafe);
        return MapOutcome(outcome);
    }

    // ---- step ---------------------------------------------------------------------

    private VerbResult StepBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetHandleAndOne(p, "message", out string handle, out string message, out var err)) return err!.Value;

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        return MapOutcome(_ops.Step(handle, message, now, unsafeBypass: p.Global.Unsafe));
    }

    // ---- state (C7: running|paused only) ------------------------------------------

    private VerbResult StateBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetHandleAndOne(p, "state", out string handle, out string stateArg, out var err)) return err!.Value;

        AppTaskState? state = stateArg.ToLowerInvariant() switch
        {
            "running" => AppTaskState.Running,
            "paused" => AppTaskState.Paused,
            _ => null,
        };
        if (state is null)
        {
            return VerbResult.Failure(FailureKind.InvalidArguments,
                $"state accepts only 'running' or 'paused' (got '{stateArg}'); use done/fail/attention for their own states.");
        }

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        return MapOutcome(_ops.SetState(handle, state.Value, now, unsafeBypass: p.Global.Unsafe));
    }

    // ---- attention --------------------------------------------------------------------

    private VerbResult AttentionBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetHandleAndOne(p, "question", out string handle, out string question, out var err)) return err!.Value;
        if (string.IsNullOrWhiteSpace(question))
            return VerbResult.Failure(FailureKind.InvalidArguments, "attention requires a non-empty <question>.");

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        return MapOutcome(_ops.Attention(handle, question, now, unsafeBypass: p.Global.Unsafe));
    }

    // ---- done / fail --------------------------------------------------------------------

    private VerbResult DoneBody(ParseResult p, DateTimeOffset now) => FinishBody(p, now, _ops.Done);

    private VerbResult FailBody(ParseResult p, DateTimeOffset now) => FinishBody(p, now, _ops.Fail);

    private VerbResult FinishBody(ParseResult p, DateTimeOffset now, Func<string, DateTimeOffset, string?, bool, OperationOutcome> finish)
    {
        if (!TryGetSingleHandle(p, out string handle, out var err)) return err!.Value;
        string? summary = p.Flags.TryGetValue("summary", out string? raw) ? raw : null;

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        return MapOutcome(finish(handle, now, summary, p.Global.Unsafe));
    }

    // ---- remove -----------------------------------------------------------------------

    private VerbResult RemoveBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetSingleHandle(p, out string handle, out var err)) return err!.Value;

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        return MapOutcome(_ops.Remove(handle, now));
    }

    // ---- run (phase 11) ---------------------------------------------------------

    private RunDeps BuildRunDeps() => new(
        _ops, _icons, _posture, _hasIdentity, _isSupported, _ensureWatchdog, _defaultDeepLink,
        _settings, _clock, _sleep, _spawnChild, _stdoutMirror, _stderrMirror);

    // ---- shared argument-shape validation -----------------------------------------------

    private static bool TryGetSingleHandle(ParseResult p, out string handle, out VerbResult? error)
    {
        if (p.Positionals.Count != 1 || string.IsNullOrEmpty(p.Positionals[0]))
        {
            handle = "";
            error = VerbResult.Failure(FailureKind.InvalidArguments, $"{p.Verb} requires exactly one positional <handle>.");
            return false;
        }
        handle = p.Positionals[0];
        error = null;
        return true;
    }

    private static bool TryGetHandleAndOne(ParseResult p, string secondName, out string handle, out string second, out VerbResult? error)
    {
        if (p.Positionals.Count != 2 || string.IsNullOrEmpty(p.Positionals[0]))
        {
            handle = "";
            second = "";
            error = VerbResult.Failure(FailureKind.InvalidArguments, $"{p.Verb} requires a <handle> and a <{secondName}>.");
            return false;
        }
        handle = p.Positionals[0];
        second = p.Positionals[1];
        error = null;
        return true;
    }

    private bool TryResolveDeepLink(ParseResult p, out Uri deepLink, out VerbResult? error)
    {
        if (!p.Flags.TryGetValue("deep-link", out string? raw))
        {
            deepLink = _defaultDeepLink;
            error = null;
            return true;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
        {
            deepLink = _defaultDeepLink;
            error = VerbResult.Failure(FailureKind.InvalidArguments, $"--deep-link '{raw}' is not a valid absolute URI.");
            return false;
        }

        deepLink = parsed;
        error = null;
        return true;
    }

    private static bool TryResolveIconToken(ParseResult p, out IconToken token, out VerbResult? error)
    {
        if (!p.Flags.TryGetValue("icon", out string? raw))
        {
            token = IconTokens.Default;
            error = null;
            return true;
        }

        if (!IconTokens.TryParse(raw, out token, out string? parseError))
        {
            error = VerbResult.Failure(FailureKind.InvalidArguments, parseError ?? $"--icon '{raw}' is invalid.");
            return false;
        }

        error = null;
        return true;
    }

    private static VerbResult MapOutcome(OperationOutcome outcome) => outcome.Kind switch
    {
        OutcomeKind.Accepted or OutcomeKind.AcceptedUnsafe or OutcomeKind.Resurrected or OutcomeKind.Removed
            => VerbResult.Success(outcome.Reason),
        OutcomeKind.RefusedInvalidArgument or OutcomeKind.RefusedUnsafeCombo
            => VerbResult.Failure(FailureKind.InvalidArguments, outcome.Reason),
        // UnknownHandleNoOp / WriteGateUnavailable: no dedicated FailureKind exists for
        // these (only 4 codes total, FailureLog.cs) -- ERGO-27's "all behavior identical...
        // only the logged reason and strict exit code differ" is satisfied by bucketing
        // them as Generic, still logged/exit-coded like every other failure.
        _ => VerbResult.Failure(FailureKind.Generic, outcome.Reason),
    };

    private static string? FirstOrNull(IReadOnlyList<string> positionals) => positionals.Count > 0 ? positionals[0] : null;
}
