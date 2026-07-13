using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace HostEventRecorder.Tests;

/// <summary>
/// Real subprocess tests -- these must spawn the actual built
/// <c>host-event-recorder.exe</c> (not call <see cref="Recorder.Capture"/>
/// in-process) because the property under test, "the fallback resolves
/// against the exe's own directory regardless of the process's current
/// working directory", is only meaningfully observable from a real OS
/// process boundary: this test process's own <c>AppContext.BaseDirectory</c>
/// is the test host's, not the recorder's.
/// </summary>
[TestClass]
public sealed class RecorderProcessTests
{
    [TestMethod]
    public void NoArgOverride_NoEnvOverride_DifferentCwd_LogLandsExeAdjacent()
    {
        string exePath = RecorderExeLocator.FindBuiltExePath();
        string exeDir = Path.GetDirectoryName(exePath)!;
        string capturesDir = Path.Combine(exeDir, Constants.DefaultCaptureDirName);

        // A cwd deliberately far from the exe's own directory -- the point
        // of this test is that the fallback must NOT depend on this value.
        string farCwd = Path.GetTempPath();

        // Clean slate: today's ad-hoc file may already exist from a prior
        // run of this same test earlier the same day.
        string expectedFile = Path.Combine(capturesDir, string.Format(
            Constants.JsonlFilenameFormat,
            Constants.AdhocSessionPrefix + DateTimeOffset.UtcNow.ToString(Constants.AdhocSessionDateFormat, CultureInfo.InvariantCulture)));
        if (File.Exists(expectedFile))
            File.Delete(expectedFile);

        var psi = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = farCwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--host");
        psi.ArgumentList.Add("process-test-host");
        psi.ArgumentList.Add("--event");
        psi.ArgumentList.Add("DefaultPathFallbackSmoke");
        // Explicitly absent: --session, --capture-dir, and this test
        // process does not export HOSTREC_SESSION/HOSTREC_CAPTURE_DIR, so
        // the child inherits none unless they happen to already be set in
        // this machine's real environment -- strip them defensively so the
        // test is deterministic regardless of the ambient environment.
        psi.Environment.Remove(Constants.SessionEnvVar);
        psi.Environment.Remove(Constants.CaptureDirEnvVar);

        using var process = Process.Start(psi)!;
        process.StandardInput.Write("{\"probe\":\"default-path-fallback\"}");
        process.StandardInput.Close();

        bool exited = process.WaitForExit(TimeSpan.FromSeconds(15));
        string stderr = process.StandardError.ReadToEnd();
        Assert.IsTrue(exited, "the recorder process did not exit in time.");
        Assert.AreEqual(0, process.ExitCode, $"recorder exited non-zero; stderr: {stderr}");

        Assert.IsTrue(File.Exists(expectedFile), $"expected the log to land exe-adjacent at '{expectedFile}' regardless of the spawning cwd ('{farCwd}').");

        string[] lines = File.ReadAllLines(expectedFile);
        Assert.IsGreaterThanOrEqualTo(1, lines.Length);
        using var doc = JsonDocument.Parse(lines[^1]);
        JsonElement root = doc.RootElement;
        Assert.AreEqual("process-test-host", root.GetProperty("host").GetString());
        Assert.AreEqual("DefaultPathFallbackSmoke", root.GetProperty("event").GetString());
        Assert.AreEqual("{\"probe\":\"default-path-fallback\"}", root.GetProperty("payload").GetString());
        StringAssert.StartsWith(root.GetProperty("session").GetString()!, Constants.AdhocSessionPrefix);

        // Best-effort cleanup so repeated local test runs don't accumulate lines across days of dev iteration.
        try { File.Delete(expectedFile); } catch (IOException) { }
    }
}
