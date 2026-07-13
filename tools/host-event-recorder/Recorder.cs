using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HostEventRecorder;

/// <summary>
/// The one-call orchestration seam: resolves session id and capture path,
/// decodes the payload, builds the envelope, and performs the guarded
/// append. Every ambient input (stdin bytes, env values, base directory,
/// wall clock, own pid) is an explicit parameter -- <see cref="Program.Main"/>
/// is the only place any of those are actually read from the real process --
/// so this whole pipeline is testable in-process without spawning a child
/// process (mirrors src/Atv's seam-purity convention).
/// </summary>
public static class Recorder
{
    /// <summary>Runs the full capture pipeline once; returns the log file path written to.</summary>
    public static string Capture(
        ArgvParser.Options options,
        byte[] stdinBytes,
        string? envSession,
        string? envCaptureDir,
        string baseDirectory,
        DateTimeOffset utcNow,
        int pid)
    {
        string sessionId = SessionResolution.ResolveSessionId(options.Session, envSession, utcNow);
        string captureDir = PathResolution.ResolveCaptureDir(options.CaptureDir, envCaptureDir, baseDirectory);
        string filePath = PathResolution.ResolveLogFilePath(captureDir, sessionId);

        // Raw bytes decoded UTF-8 -- exact byte control end to end (compiled
        // C#, not a PS-5.1 pipeline) is why this tool exists (LIFE-24).
        string payload = Encoding.UTF8.GetString(stdinBytes);

        var envelope = new EventEnvelope
        {
            Ts = utcNow.ToString("O", CultureInfo.InvariantCulture),
            Host = options.Host,
            Event = options.Event,
            Pid = pid,
            Session = sessionId,
            Payload = payload,
        };

        string json = JsonSerializer.Serialize(envelope, EnvelopeJsonContext.Default.EventEnvelope);
        GuardedAppender.Append(filePath, json);

        return filePath;
    }
}
