using System.Globalization;

namespace HostEventRecorder;

/// <summary>
/// Pure resolution logic for the capture session id (phase-14 Part A
/// "Session id" spec). Every input is an explicit parameter, so it is
/// independently testable and free of hidden clock/environment reads.
/// </summary>
public static class SessionResolution
{
    /// <summary>
    /// Precedence: explicit <c>--session</c> argv flag &gt; the
    /// <see cref="Constants.SessionEnvVar"/> env var (the driver exports
    /// this into the host process's environment so every hook-spawned
    /// recorder inherits it) &gt; a dated ad-hoc fallback
    /// (<see cref="Constants.AdhocSessionPrefix"/> + the UTC date) so manual
    /// captures still land deterministically -- same day, same file --
    /// rather than scattering across a new file per invocation.
    /// </summary>
    public static string ResolveSessionId(string? argvSession, string? envSession, DateTimeOffset utcNow)
    {
        if (!string.IsNullOrEmpty(argvSession))
            return argvSession;

        if (!string.IsNullOrEmpty(envSession))
            return envSession;

        return Constants.AdhocSessionPrefix + utcNow.ToString(Constants.AdhocSessionDateFormat, CultureInfo.InvariantCulture);
    }
}
