namespace Atv.Diagnostics;

/// <summary>FAIL-2's stable exit vocabulary -- meaningful only under `--strict` (default mode always exits 0, FAIL-1). Numeric values ARE the process exit codes.</summary>
public enum FailureKind
{
    Generic = 1,
    ApiUnavailable = 2,
    IdentityNotRegistered = 3,
    InvalidArguments = 4,
}

/// <summary>What a verb body reports back to <see cref="Posture.Run"/>.</summary>
public readonly struct VerbResult
{
    public bool Ok { get; }
    public FailureKind Kind { get; }
    public string Reason { get; }

    private VerbResult(bool ok, FailureKind kind, string reason)
    {
        Ok = ok;
        Kind = kind;
        Reason = reason;
    }

    public static VerbResult Success(string reason = "") => new(true, default, reason ?? throw new ArgumentNullException(nameof(reason)));
    public static VerbResult Failure(FailureKind kind, string reason) => new(false, kind, reason ?? throw new ArgumentNullException(nameof(reason)));
}

/// <summary>
/// FAIL-1/FAIL-2's non-disruptive wrapper -- the one place a verb's outcome
/// gets mapped onto the failure log, stdout/stderr, and the exit code.
///
/// Default (non-strict): ALWAYS returns 0. A failure still writes exactly
/// one <see cref="FailureLog"/> entry (FAIL-1 hard requirement) and, if
/// <see cref="Output.Json"/> is set, the `{"ok":false,...}` shape -- but
/// never stderr, never a nonzero exit.
///
/// `--strict`: additionally writes to stderr and returns the mapped
/// <see cref="FailureKind"/> as the process exit code.
///
/// `--verbose`: live detail to stderr regardless of strict, plus (FAIL-3) a
/// minimal success log entry -- success is not logged otherwise.
///
/// An exception escaping <paramref name="body"/> in <see cref="Run"/> is
/// itself caught and folded into a <see cref="FailureKind.Generic"/> result
/// -- this wrapper is the backstop that keeps a caller from ever seeing an
/// unhandled-exception nonzero exit / stack trace in non-strict mode.
/// </summary>
public sealed class Posture
{
    private readonly FailureLog _log;
    private readonly Output _output;
    private readonly bool _strict;
    private readonly bool _verbose;

    public Posture(FailureLog log, Output output, bool strict, bool verbose = false)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _strict = strict;
        _verbose = verbose;
    }

    /// <summary>Runs <paramref name="body"/> for <paramref name="verb"/> (optionally scoped to <paramref name="handle"/>, for the log entry) and returns the process exit code.</summary>
    public int Run(string verb, string? handle, Func<VerbResult> body, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(verb);
        ArgumentNullException.ThrowIfNull(body);

        VerbResult result;
        try
        {
            result = body();
        }
        catch (Exception ex)
        {
            result = VerbResult.Failure(FailureKind.Generic, $"Unhandled exception: {ex.Message}");
        }

        if (_verbose || (_strict && !result.Ok))
            _output.Diagnostic($"{verb}: {(result.Ok ? "ok" : "failed")} -- {result.Reason}");

        if (!result.Ok)
            _log.Append(verb, handle, result.Reason, now);
        else if (_verbose)
            _log.Append(verb, handle, result.Reason.Length > 0 ? result.Reason : "ok", now);

        _output.MutatingResult(result.Ok, result.Reason);

        return !result.Ok && _strict ? (int)result.Kind : 0;
    }
}

/// <summary>
/// INFRA-13's outcome mapping: classifies why the platform isn't usable
/// right now onto the FAIL-2 exit vocabulary. Two independent runtime checks
/// -- package identity presence and API presence
/// (<see cref="Atv.Store.IAppTaskStore.IsSupported"/>, already wrapped for
/// CLASS_E_CLASSNOTAVAILABLE by the adapter) -- checked identity-first, since
/// asking "is the API supported" is only meaningful once the process has
/// identity at all. Delegate-injected (INFRA-8-style seam) so callers/tests
/// never need real package identity or the WinRT API to exercise this
/// mapping.
/// </summary>
public static class Capability
{
    public static VerbResult Check(Func<bool> hasIdentity, Func<bool> isSupported)
    {
        ArgumentNullException.ThrowIfNull(hasIdentity);
        ArgumentNullException.ThrowIfNull(isSupported);

        if (!hasIdentity())
            return VerbResult.Failure(FailureKind.IdentityNotRegistered,
                "No package identity -- AppTaskInfo requires a registered sparse package (see Register-Identity.ps1).");

        if (!isSupported())
            return VerbResult.Failure(FailureKind.ApiUnavailable,
                "AppTaskInfo.IsSupported() reports false on this build (gradual rollout, or CLASS_E_CLASSNOTAVAILABLE) -- no version pinning, runtime detection is authoritative.");

        return VerbResult.Success("AppTaskInfo is supported and package identity is present.");
    }
}
