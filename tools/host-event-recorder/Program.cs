namespace HostEventRecorder;

/// <summary>
/// The composition root: the only place that reads the real process's argv,
/// stdin, environment variables, clock, and pid. Everything else in this
/// project is pure/explicit-input logic (<see cref="Recorder.Capture"/> and
/// its collaborators).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        ArgvParser.Options options;
        try
        {
            options = ArgvParser.Parse(args);
        }
        catch (ArgvException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        byte[] stdinBytes;
        using (Stream stdin = Console.OpenStandardInput())
        using (var buffer = new MemoryStream())
        {
            stdin.CopyTo(buffer);
            stdinBytes = buffer.ToArray();
        }

        string? envSession = Environment.GetEnvironmentVariable(Constants.SessionEnvVar);
        string? envCaptureDir = Environment.GetEnvironmentVariable(Constants.CaptureDirEnvVar);

        try
        {
            Recorder.Capture(
                options,
                stdinBytes,
                envSession,
                envCaptureDir,
                AppContext.BaseDirectory,
                DateTimeOffset.UtcNow,
                Environment.ProcessId);
        }
        catch (TimeoutException ex)
        {
            // A hook-spawned recorder is fire-and-forget from the host's
            // perspective; a stuck append mutex is a genuine anomaly worth
            // surfacing loudly (nonzero exit, stderr message) rather than
            // silently swallowing -- silent swallow would violate the
            // byte-faithful capture guarantee this tool exists for.
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        return 0;
    }
}
