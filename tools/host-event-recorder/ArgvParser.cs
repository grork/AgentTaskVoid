namespace HostEventRecorder;

/// <summary>Thrown for any argv problem (missing required flag, unrecognized flag, missing value). Caught once at the composition root (<see cref="Program"/>) and reported to stderr.</summary>
public sealed class ArgvException(string message) : Exception(message);

/// <summary>
/// Parses the recorder's argv surface: <c>--host &lt;tag&gt; --event &lt;name&gt;</c>
/// (required, static per hook line) plus <c>--session &lt;id&gt;</c> and
/// <c>--capture-dir &lt;dir&gt;</c> (optional overrides -- see
/// <see cref="SessionResolution"/>/<see cref="PathResolution"/> for their
/// precedence against the env vars).
/// </summary>
public static class ArgvParser
{
    public sealed record Options(string Host, string Event, string? Session, string? CaptureDir);

    public static Options Parse(string[] args)
    {
        string? host = null, evt = null, session = null, captureDir = null;

        int i = 0;
        while (i < args.Length)
        {
            string flag = args[i];
            switch (flag)
            {
                case "--host":
                    host = RequireValue(args, ref i, flag);
                    break;
                case "--event":
                    evt = RequireValue(args, ref i, flag);
                    break;
                case "--session":
                    session = RequireValue(args, ref i, flag);
                    break;
                case "--capture-dir":
                    captureDir = RequireValue(args, ref i, flag);
                    break;
                default:
                    throw new ArgvException($"Unrecognized argument: '{flag}'.");
            }
        }

        if (string.IsNullOrEmpty(host))
            throw new ArgvException("--host <tag> is required.");
        if (string.IsNullOrEmpty(evt))
            throw new ArgvException("--event <name> is required.");

        return new Options(host, evt, session, captureDir);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        int valueIndex = i + 1;
        if (valueIndex >= args.Length)
            throw new ArgvException($"'{flag}' requires a value.");

        string value = args[valueIndex];
        i = valueIndex + 1;
        return value;
    }
}
