namespace HostEventRecorder;

/// <summary>
/// Pure resolution logic for the capture directory and log file path
/// (phase-14 Part A "Capture location" spec). Takes every input the
/// decision depends on as an explicit parameter -- in particular
/// <paramref name="baseDirectory"/> rather than reading
/// <c>AppContext.BaseDirectory</c> itself, and never touches
/// <c>Environment.CurrentDirectory</c> at all -- so the "never
/// cwd-relative" requirement is structural (there is no code path here that
/// could consult the process's working directory), not merely tested for.
/// The composition root (<see cref="Program"/>) supplies the real values.
/// </summary>
public static class PathResolution
{
    /// <summary>
    /// Precedence: explicit argv flag &gt; explicit env var &gt; the
    /// exe-adjacent fallback (<paramref name="baseDirectory"/> +
    /// <see cref="Constants.DefaultCaptureDirName"/>). The driver always
    /// sets the env var, so the fallback only engages for manual/ad-hoc
    /// invocations.
    /// </summary>
    public static string ResolveCaptureDir(string? argvCaptureDir, string? envCaptureDir, string baseDirectory)
    {
        if (!string.IsNullOrEmpty(argvCaptureDir))
            return argvCaptureDir;

        if (!string.IsNullOrEmpty(envCaptureDir))
            return envCaptureDir;

        return Path.Combine(baseDirectory, Constants.DefaultCaptureDirName);
    }

    /// <summary>Combines the resolved capture directory with the session-id-embedded filename (<see cref="Constants.JsonlFilenameFormat"/>).</summary>
    public static string ResolveLogFilePath(string captureDir, string sessionId)
    {
        string filename = string.Format(Constants.JsonlFilenameFormat, SanitizeForFilename(sessionId));
        return Path.Combine(captureDir, filename);
    }

    /// <summary>
    /// Defensive: session ids normally come from a trusted driver, but a
    /// manual <c>--session</c> value could contain filesystem-invalid
    /// characters. Replaces each with <c>_</c> rather than throwing --
    /// losing a raw capture line to a rejected argument is worse than a
    /// slightly-mangled filename.
    /// </summary>
    private static string SanitizeForFilename(string sessionId)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        if (sessionId.IndexOfAny(invalid) < 0)
            return sessionId;

        var chars = sessionId.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }

        return new string(chars);
    }
}
