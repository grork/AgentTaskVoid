using Atv.Cli.Verbs;
using Atv.Config;
using Atv.Diagnostics;
using Atv.Icons;
using Atv.Operations;
using Atv.Run;
using Atv.Semantics;
using Atv.Store;

namespace Atv.Cli;

/// <summary>
/// Dispatches one already-parsed <see cref="ParseResult"/> to the matching
/// verb body. The eight ERGO-31 v2 semantic verbs are inline private methods
/// over an injected <see cref="SemanticEngine"/> (the v1 lifecycle verbs --
/// <c>start</c>/<c>step</c>/<c>state</c>/<c>attention</c>/<c>done</c>/
/// <c>fail</c> -- are retired, phase 15); <c>remove</c> stays a thin method
/// over the surviving <see cref="TaskOperations"/> core -- the fake-testable
/// half of the CLI (everything real-only lives in
/// <see cref="CompositionRoot"/>). The three phase-10 utility verbs
/// (`list`/`clear`/`doctor`) are dedicated static classes in
/// <see cref="Atv.Cli.Verbs"/> that each own their own <see cref="Posture"/>
/// call instead -- `list`/`doctor` need <see cref="Posture.RunQuery"/>
/// (their own `--json` shape, ERGO-27 C5), not the generic mutating-verb
/// wrapper every lifecycle verb (and `clear`) uses.
///
/// Exactly one <see cref="Posture.Run"/>/<see cref="Posture.RunQuery"/> call
/// per verb invocation wraps: (1) pure argument-shape validation (handle
/// presence, icon-token/deep-link-URI parsing, the closed kind/reason
/// vocabularies) -- runs first and unconditionally, independent of platform
/// state; (2) <see cref="Capability.Check"/> (identity, then API support --
/// skipped entirely by `doctor`, whose whole job is diagnosing exactly that);
/// (3) the LIFE-17/INFRA-19 <see cref="Atv.Watchdog.EnsureWatchdog.Run"/>
/// liveness gate on every WRITE-path verb (semantic verbs + `remove` +
/// `clear`, never `list`/`doctor`); (4) the actual operation. Every failure
/// mode -- bad args, platform down, ERGO-10 validator refusal, unknown
/// handle -- therefore goes through the identical non-disruptive pipe
/// (ERGO-27: "all behavior identical... only the logged reason and strict
/// exit code differ").
/// </summary>
public sealed class Dispatcher
{
    private readonly TaskOperations _ops;
    private readonly SemanticEngine _engine;
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
    private readonly TextReader _stdin;

    public Dispatcher(
        TaskOperations ops,
        SemanticEngine engine,
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
        Stream stderrMirror,
        TextReader stdin)
    {
        _ops = ops;
        _engine = engine;
        _posture = posture;
        _output = output;
        _icons = icons;
        _defaultDeepLink = defaultDeepLink;
        _hasIdentity = hasIdentity;
        _isSupported = isSupported;
        _ensureWatchdog = ensureWatchdog;
        _doctorContext = doctorContext;
        _settings = settings;
        _clock = clock;
        _sleep = sleep;
        _spawnChild = spawnChild;
        _stdoutMirror = stdoutMirror;
        _stderrMirror = stderrMirror;
        _stdin = stdin;
    }

    /// <summary>Dispatches one lifecycle-verb invocation. Callers must have already handled <see cref="ParseResult.ShowHelp"/>/<see cref="ParseResult.ShowVersion"/>/a bare (no-verb) invocation -- those never reach here (Program.cs's job, needs no identity/platform/Posture at all).</summary>
    public int Run(ParseResult parsed, DateTimeOffset now)
    {
        if (parsed.ShowHelp || parsed.ShowVersion)
            throw new ArgumentException("Help/version requests must be handled by the caller before reaching Dispatcher.Run.", nameof(parsed));

        if (parsed.Error is not null)
            return _posture.Run(parsed.Verb ?? "(parse)", FirstOrNull(parsed.Positionals), () => VerbResult.Failure(FailureKind.InvalidArguments, parsed.Error), now);

        return parsed.Verb switch
        {
            "working" => _posture.Run("working", FirstOrNull(parsed.Positionals), () => WorkingBody(parsed, now), now),
            "activity" => _posture.Run("activity", FirstOrNull(parsed.Positionals), () => ActivityBody(parsed, now), now),
            "blocked" => _posture.Run("blocked", FirstOrNull(parsed.Positionals), () => BlockedBody(parsed, now), now),
            "ready" => _posture.Run("ready", FirstOrNull(parsed.Positionals), () => ReadyBody(parsed, now), now),
            "broken" => _posture.Run("broken", FirstOrNull(parsed.Positionals), () => BrokenBody(parsed, now), now),
            "agent-started" => _posture.Run("agent-started", FirstOrNull(parsed.Positionals), () => AgentStartedBody(parsed, now), now),
            "agent-stopped" => _posture.Run("agent-stopped", FirstOrNull(parsed.Positionals), () => AgentStoppedBody(parsed, now), now),
            "session-ended" => _posture.Run("session-ended", FirstOrNull(parsed.Positionals), () => SessionEndedBody(parsed, now), now),
            "remove" => _posture.Run("remove", FirstOrNull(parsed.Positionals), () => RemoveBody(parsed, now), now),
            "list" => ListVerb.Run(_output, _posture, _hasIdentity, _isSupported, _ops, now),
            "clear" => ClearVerb.Run(_posture, _hasIdentity, _isSupported, _ensureWatchdog, _ops, parsed.IncludeRecycleBin, now),
            "doctor" => DoctorVerb.Run(_output, _posture, _doctorContext, now),
            "run" => RunVerb.Run(BuildRunDeps(), parsed, now),
            null => throw new ArgumentException("A bare (no-verb) invocation must be handled by the caller before reaching Dispatcher.Run.", nameof(parsed)),
            _ => _posture.Run(parsed.Verb, null, () => VerbResult.Failure(FailureKind.InvalidArguments, $"Unknown verb '{parsed.Verb}'."), now),
        };
    }

    // ---- working ------------------------------------------------------------------

    private VerbResult WorkingBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetSingleHandle(p, out string handle, out var handleErr)) return handleErr!.Value;
        if (!TryResolveDeepLink(p, out Uri deepLink, out var deepLinkErr)) return deepLinkErr!.Value;
        if (!TryResolveIconToken(p, out IconToken token, out var iconErr)) return iconErr!.Value;

        string? title = p.Flags.GetValueOrDefault("title");
        string? subtitle = p.Flags.GetValueOrDefault("subtitle");
        string? goal = ResolveFreeText(p, "goal");

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        Uri iconUri = _icons.Place(handle, token);
        var outcome = _engine.Working(handle, title, subtitle, iconUri, deepLink, goal, now, unsafeBypass: p.Global.Unsafe);
        return MapOutcome(outcome);
    }

    // ---- activity -------------------------------------------------------------------

    private VerbResult ActivityBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetSingleHandle(p, out string handle, out var handleErr)) return handleErr!.Value;
        if (!TryResolveDeepLink(p, out Uri deepLink, out var deepLinkErr)) return deepLinkErr!.Value;
        if (!TryResolveIconToken(p, out IconToken token, out var iconErr)) return iconErr!.Value;
        if (!TryResolveKind(p, out ActivityKind kind, out var kindErr)) return kindErr!.Value;

        string? title = p.Flags.GetValueOrDefault("title");
        string? subtitle = p.Flags.GetValueOrDefault("subtitle");
        string? label = ResolveFreeText(p, "label");
        string? agentId = p.Flags.GetValueOrDefault("agent");
        string? name = p.Flags.GetValueOrDefault("name");

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        Uri iconUri = _icons.Place(handle, token);
        var outcome = _engine.Activity(handle, title, subtitle, iconUri, deepLink, kind, label, agentId, name, now, unsafeBypass: p.Global.Unsafe);
        return MapOutcome(outcome);
    }

    // ---- blocked --------------------------------------------------------------------

    private VerbResult BlockedBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetSingleHandle(p, out string handle, out var handleErr)) return handleErr!.Value;
        if (!TryResolveDeepLink(p, out Uri deepLink, out var deepLinkErr)) return deepLinkErr!.Value;
        if (!TryResolveIconToken(p, out IconToken token, out var iconErr)) return iconErr!.Value;

        string? question = ResolveFreeText(p, "question");
        if (string.IsNullOrWhiteSpace(question))
            return VerbResult.Failure(FailureKind.InvalidArguments, "blocked requires a non-empty --question (platform-enforced: NeedsAttention requires SetQuestion).");

        string? title = p.Flags.GetValueOrDefault("title");
        string? subtitle = p.Flags.GetValueOrDefault("subtitle");
        string? agentId = p.Flags.GetValueOrDefault("agent");

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        Uri iconUri = _icons.Place(handle, token);
        var outcome = _engine.Blocked(handle, title, subtitle, iconUri, deepLink, question, agentId, now, unsafeBypass: p.Global.Unsafe);
        return MapOutcome(outcome);
    }

    // ---- ready ----------------------------------------------------------------------

    private VerbResult ReadyBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetSingleHandle(p, out string handle, out var handleErr)) return handleErr!.Value;
        if (!TryResolveDeepLink(p, out Uri deepLink, out var deepLinkErr)) return deepLinkErr!.Value;
        if (!TryResolveIconToken(p, out IconToken token, out var iconErr)) return iconErr!.Value;

        string? title = p.Flags.GetValueOrDefault("title");
        string? subtitle = p.Flags.GetValueOrDefault("subtitle");
        string? summary = ResolveFreeText(p, "summary");

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        Uri iconUri = _icons.Place(handle, token);
        var outcome = _engine.Ready(handle, title, subtitle, iconUri, deepLink, summary, now, unsafeBypass: p.Global.Unsafe);
        return MapOutcome(outcome);
    }

    // ---- broken ---------------------------------------------------------------------

    private VerbResult BrokenBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetSingleHandle(p, out string handle, out var handleErr)) return handleErr!.Value;
        if (!TryResolveDeepLink(p, out Uri deepLink, out var deepLinkErr)) return deepLinkErr!.Value;
        if (!TryResolveIconToken(p, out IconToken token, out var iconErr)) return iconErr!.Value;

        if (!p.Flags.TryGetValue("reason", out string? reasonRaw) || !BrokenReasons.TryParse(reasonRaw, out BrokenReasonToken reason))
        {
            return VerbResult.Failure(FailureKind.InvalidArguments,
                $"broken requires --reason from the closed vocabulary (rate-limit/overloaded/api-error/timeout/fatal); got '{reasonRaw}'.");
        }

        string? title = p.Flags.GetValueOrDefault("title");
        string? subtitle = p.Flags.GetValueOrDefault("subtitle");
        string? detail = ResolveFreeText(p, "detail");

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        Uri iconUri = _icons.Place(handle, token);
        var outcome = _engine.Broken(handle, title, subtitle, iconUri, deepLink, reason, detail, now, unsafeBypass: p.Global.Unsafe);
        return MapOutcome(outcome);
    }

    // ---- agent-started / agent-stopped -----------------------------------------------

    private VerbResult AgentStartedBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetSingleHandle(p, out string handle, out var handleErr)) return handleErr!.Value;
        if (!TryResolveDeepLink(p, out Uri deepLink, out var deepLinkErr)) return deepLinkErr!.Value;
        if (!TryResolveIconToken(p, out IconToken token, out var iconErr)) return iconErr!.Value;

        string? title = p.Flags.GetValueOrDefault("title");
        string? subtitle = p.Flags.GetValueOrDefault("subtitle");
        string? agentId = p.Flags.GetValueOrDefault("agent");
        string? name = p.Flags.GetValueOrDefault("name");

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        Uri iconUri = _icons.Place(handle, token);
        var outcome = _engine.AgentStarted(handle, title, subtitle, iconUri, deepLink, agentId, name, now, unsafeBypass: p.Global.Unsafe);
        return MapOutcome(outcome);
    }

    private VerbResult AgentStoppedBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetSingleHandle(p, out string handle, out var handleErr)) return handleErr!.Value;
        if (!TryResolveDeepLink(p, out Uri deepLink, out var deepLinkErr)) return deepLinkErr!.Value;
        if (!TryResolveIconToken(p, out IconToken token, out var iconErr)) return iconErr!.Value;

        string? title = p.Flags.GetValueOrDefault("title");
        string? subtitle = p.Flags.GetValueOrDefault("subtitle");
        string? agentId = p.Flags.GetValueOrDefault("agent");

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        Uri iconUri = _icons.Place(handle, token);
        var outcome = _engine.AgentStopped(handle, title, subtitle, iconUri, deepLink, agentId, now, unsafeBypass: p.Global.Unsafe);
        return MapOutcome(outcome);
    }

    // ---- session-ended (no identity flags, no upsert -- ERGO-31 §1 intro) -----------

    private VerbResult SessionEndedBody(ParseResult p, DateTimeOffset now)
    {
        if (!TryGetSingleHandle(p, out string handle, out var handleErr)) return handleErr!.Value;

        if (!p.Flags.TryGetValue("reason", out string? reasonRaw) || !SessionEndedReasons.TryParse(reasonRaw, out SessionEndedReasonToken reason))
        {
            return VerbResult.Failure(FailureKind.InvalidArguments,
                $"session-ended requires --reason from the closed vocabulary (finished/error); got '{reasonRaw}'.");
        }

        var cap = Capability.Check(_hasIdentity, _isSupported);
        if (!cap.Ok) return cap;

        _ensureWatchdog();

        return MapOutcome(_engine.SessionEnded(handle, reason, now));
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

    // ---- run (phase 11; re-seated onto the v2 engine, phase 15) -----------------

    private RunDeps BuildRunDeps() => new(
        _ops, _engine, _icons, _posture, _hasIdentity, _isSupported, _ensureWatchdog, _defaultDeepLink,
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

    private static bool TryResolveKind(ParseResult p, out ActivityKind kind, out VerbResult? error)
    {
        if (!p.Flags.TryGetValue("kind", out string? raw) || !ActivityKinds.TryParse(raw, out kind))
        {
            kind = default;
            error = VerbResult.Failure(FailureKind.InvalidArguments,
                $"activity requires --kind from the closed vocabulary (read/edit/write/search/shell/fetch/web-search/plan/compacting/tool); got '{raw}'.");
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// ERGO-31's free-text convention: a flag's value of literally <c>"-"</c>
    /// means "read this field from stdin" (UTF-8, to EOF, TRAILING whitespace
    /// trimmed -- LIFE-24 S2-walk item 1). Any OTHER value is used verbatim
    /// as the literal text -- a pragmatic superset of the documented
    /// mechanism: short/simple text is just as valid typed directly on argv
    /// (handy for manual invocation/scripting), and nothing about accepting
    /// it compromises the stdin path's own guarantees. Absent flag -&gt;
    /// <see langword="null"/> (no claim, per the idempotency rule -- an
    /// absent optional field is never conflated with an explicit empty one).
    /// </summary>
    private string? ResolveFreeText(ParseResult p, string flagName)
    {
        if (!p.Flags.TryGetValue(flagName, out string? raw)) return null;
        return raw == "-" ? _stdin.ReadToEnd().TrimEnd() : raw;
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

    /// <summary>
    /// Resolves the icon source for one v2 upserting verb call: <c>--icon</c>
    /// (the ERGO-20 curated-name/emoji/raw-path space) or <c>--icon-file</c>
    /// (ERGO-29's dedicated bring-your-own-image flag), never both -- pure
    /// argument-shape validation, so this runs before <see cref="Capability.Check"/>
    /// just like <see cref="TryGetSingleHandle"/>/<see cref="TryResolveDeepLink"/>.
    /// Absent either flag, falls back to <see cref="IconTokens.Default"/>.
    /// </summary>
    private static bool TryResolveIconToken(ParseResult p, out IconToken token, out VerbResult? error)
    {
        bool hasIcon = p.Flags.TryGetValue("icon", out string? iconRaw);
        bool hasIconFile = p.Flags.TryGetValue("icon-file", out string? iconFileRaw);

        if (hasIcon && hasIconFile)
        {
            token = default;
            error = VerbResult.Failure(FailureKind.InvalidArguments, "--icon and --icon-file cannot both be specified on the same call -- choose one icon source.");
            return false;
        }

        if (hasIconFile)
        {
            // The path is carried through unvalidated at parse time -- same
            // posture as --icon's own RawPath fallback tier; IconService
            // validates/normalizes it at render time (RasterNormalizer),
            // degrading to the fallback chain rather than erroring here.
            token = IconToken.RawPath(iconFileRaw!);
            error = null;
            return true;
        }

        if (!hasIcon)
        {
            token = IconTokens.Default;
            error = null;
            return true;
        }

        if (!IconTokens.TryParse(iconRaw, out token, out string? parseError))
        {
            error = VerbResult.Failure(FailureKind.InvalidArguments, parseError ?? $"--icon '{iconRaw}' is invalid.");
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
