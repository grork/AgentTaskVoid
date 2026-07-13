using System.Text;
using System.Text.Json;

namespace HostEventRecorder.Tests;

/// <summary>
/// Drives the full <see cref="Recorder.Capture"/> pipeline in-process
/// (argv options in, stdin bytes in, real file on disk out) -- no
/// subprocess needed since <see cref="Program"/> is a thin composition root
/// over this exact call.
/// </summary>
[TestClass]
public sealed class RecorderCaptureTests
{
    [TestMethod]
    public void SingleCapture_WritesOneLine_WithAllSixFieldsCorrect()
    {
        using var dir = new TempDirectory();
        var options = new ArgvParser.Options("claude-code", "PostToolUse", "sess-a", dir.Path);
        byte[] stdin = Encoding.UTF8.GetBytes("{\"tool\":\"Bash\"}");
        var now = new DateTimeOffset(2026, 7, 12, 18, 3, 44, 191, TimeSpan.Zero);

        string filePath = Recorder.Capture(options, stdin, envSession: null, envCaptureDir: null, baseDirectory: @"C:\unused", now, pid: 4321);

        Assert.AreEqual(Path.Combine(dir.Path, "session-sess-a.jsonl"), filePath);
        string[] lines = File.ReadAllLines(filePath);
        Assert.HasCount(1, lines);

        using var doc = JsonDocument.Parse(lines[0]);
        JsonElement root = doc.RootElement;
        Assert.AreEqual("claude-code", root.GetProperty("host").GetString());
        Assert.AreEqual("PostToolUse", root.GetProperty("event").GetString());
        Assert.AreEqual(4321, root.GetProperty("pid").GetInt32());
        Assert.AreEqual("sess-a", root.GetProperty("session").GetString());
        Assert.AreEqual("{\"tool\":\"Bash\"}", root.GetProperty("payload").GetString());

        // ts is a parseable, round-trippable ISO-8601 UTC instant equal to the supplied clock value.
        var parsedTs = DateTimeOffset.Parse(root.GetProperty("ts").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.AreEqual(now, parsedTs);
    }

    [TestMethod]
    public void TwoDifferentSessions_ProduceTwoSeparateFiles()
    {
        using var dir = new TempDirectory();
        var now = DateTimeOffset.UtcNow;

        string fileA = Recorder.Capture(new ArgvParser.Options("h", "e", "session-A", dir.Path), [], envSession: null, envCaptureDir: null, baseDirectory: dir.Path, now, pid: 1);
        string fileB = Recorder.Capture(new ArgvParser.Options("h", "e", "session-B", dir.Path), [], envSession: null, envCaptureDir: null, baseDirectory: dir.Path, now, pid: 1);

        Assert.AreNotEqual(fileA, fileB);
        Assert.IsTrue(File.Exists(fileA));
        Assert.IsTrue(File.Exists(fileB));
    }

    [TestMethod]
    public void SameSession_TwoEvents_AppendToSameFile_TwoLines()
    {
        using var dir = new TempDirectory();
        var now = DateTimeOffset.UtcNow;

        string file1 = Recorder.Capture(new ArgvParser.Options("h", "PreToolUse", "same-session", dir.Path), Encoding.UTF8.GetBytes("first"), null, null, dir.Path, now, 1);
        string file2 = Recorder.Capture(new ArgvParser.Options("h", "PostToolUse", "same-session", dir.Path), Encoding.UTF8.GetBytes("second"), null, null, dir.Path, now, 2);

        Assert.AreEqual(file1, file2);
        string[] lines = File.ReadAllLines(file1);
        Assert.HasCount(2, lines);

        using var doc1 = JsonDocument.Parse(lines[0]);
        using var doc2 = JsonDocument.Parse(lines[1]);
        Assert.AreEqual("first", doc1.RootElement.GetProperty("payload").GetString());
        Assert.AreEqual("second", doc2.RootElement.GetProperty("payload").GetString());
    }

    [TestMethod]
    public void EnvCaptureDir_UsedWhenNoArgvOverride()
    {
        using var dir = new TempDirectory();
        var options = new ArgvParser.Options("h", "e", "s1", CaptureDir: null);

        string filePath = Recorder.Capture(options, [], envSession: null, envCaptureDir: dir.Path, baseDirectory: @"C:\unused", DateTimeOffset.UtcNow, 1);

        Assert.AreEqual(Path.Combine(dir.Path, "session-s1.jsonl"), filePath);
        Assert.IsTrue(File.Exists(filePath));
    }

    [TestMethod]
    public void EnvSession_UsedWhenNoArgvOverride()
    {
        using var dir = new TempDirectory();
        var options = new ArgvParser.Options("h", "e", Session: null, CaptureDir: dir.Path);

        string filePath = Recorder.Capture(options, [], envSession: "env-minted-session", envCaptureDir: null, baseDirectory: @"C:\unused", DateTimeOffset.UtcNow, 1);

        Assert.AreEqual(Path.Combine(dir.Path, "session-env-minted-session.jsonl"), filePath);
    }
}
