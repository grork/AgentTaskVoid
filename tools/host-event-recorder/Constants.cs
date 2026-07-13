namespace HostEventRecorder;

/// <summary>
/// Cross-artifact plumbing names -- the contract between the driver harness,
/// this recorder, its tests, and every future <c>hosts/*</c> tree (INFRA-25).
/// Pinned here, in ONE place, as the first act of Part A's execution, and
/// mirrored verbatim in <c>docs/host-events/README.md</c>.
///
/// Standing invariant #2 (brand parameterization) deliberately INVERTS for
/// this tool (INFRA-24, phase-14's "Invariant note"): the recorder consumes
/// no brand constant, no <c>Atv.*</c> reference, no <c>$(AtvBrandName)</c>,
/// no package identity. Every name below is recorder-derived
/// (<c>HOSTREC_</c>), never <c>ATV_*</c> -- the absence of any Atv-prefixed
/// name here is itself part of the separation this class exists to pin.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Env var carrying the capture session id. The driver mints one session
    /// id per capture run and exports this so every hook-spawned recorder
    /// invocation inherits it; the <c>--session</c> argv flag overrides it.
    /// Absent both, <see cref="AdhocSessionPrefix"/> supplies a fallback.
    /// </summary>
    public const string SessionEnvVar = "HOSTREC_SESSION";

    /// <summary>
    /// Env var carrying the capture directory. The driver ALWAYS sets this
    /// explicitly, pointing at the canonical gitignored
    /// <c>tools/host-event-recorder/captures/</c>; the <c>--capture-dir</c>
    /// argv flag overrides it. Absent both, the recorder falls back to
    /// <see cref="DefaultCaptureDirName"/> resolved against the exe's own
    /// directory (<c>AppContext.BaseDirectory</c>) -- NEVER the process's
    /// current working directory, since hooks spawn the recorder with an
    /// arbitrary cwd and a cwd-relative default would drop raw payloads
    /// (prompts, paths, possibly secrets) into whatever un-gitignored
    /// directory a stray invocation happens to run in.
    /// </summary>
    public const string CaptureDirEnvVar = "HOSTREC_CAPTURE_DIR";

    /// <summary>
    /// The exe-adjacent fallback capture directory's name, resolved against
    /// <c>AppContext.BaseDirectory</c> when neither <see cref="CaptureDirEnvVar"/>
    /// nor <c>--capture-dir</c> is supplied.
    /// </summary>
    public const string DefaultCaptureDirName = "captures";

    /// <summary>
    /// JSONL filename format: one file per capture session, the session id
    /// embedded in the name. <c>{0}</c> is replaced with the resolved
    /// (filename-sanitized) session id.
    /// </summary>
    public const string JsonlFilenameFormat = "session-{0}.jsonl";

    /// <summary>
    /// Prefix for the ad-hoc fallback session id used when neither
    /// <c>--session</c> nor <see cref="SessionEnvVar"/> is supplied (manual,
    /// non-driver invocations). The id is DATED, not random, so repeated
    /// manual runs on the same UTC day land deterministically in the same
    /// session file rather than scattering across a new file per invocation.
    /// </summary>
    public const string AdhocSessionPrefix = "adhoc-";

    /// <summary>Date format for the dated portion of the ad-hoc fallback session id (invariant culture, UTC).</summary>
    public const string AdhocSessionDateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Prefix for the named <see cref="System.Threading.Mutex"/> guarding an
    /// append to a given log file -- <c>Local\</c> (named-mutex names cannot
    /// contain path separators) followed by a hash of the log file's
    /// normalized full path. See <c>MutexNaming.DeriveMutexName</c>.
    /// </summary>
    public const string MutexNamePrefix = "Local\\HostEventRecorder-";
}
