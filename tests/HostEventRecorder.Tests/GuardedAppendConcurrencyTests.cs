using System.Text;
using System.Text.Json;

namespace HostEventRecorder.Tests;

/// <summary>
/// Real parallel writers (real OS <see cref="Thread"/>s, not fakes/mocks) all
/// appending to the SAME log file at once -- proves the named-mutex guard
/// (phase-14 Part A "Guarded append" spec) makes tearing structurally
/// impossible, not merely unlikely. Half the writers use a differently
/// spelled (but equivalent) path to the same file, so this simultaneously
/// exercises the mutex-name-agreement property under real contention, not
/// just in isolated unit assertions.
/// </summary>
[TestClass]
public sealed class GuardedAppendConcurrencyTests
{
    [TestMethod]
    public void ParallelWriters_SameFile_NoTornOrInterleavedLines_EveryLineParses()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Path);
        string canonicalPath = Path.Combine(dir.Path, "session-concurrent.jsonl");

        const int writerCount = 40;
        var threads = new List<Thread>();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        for (int i = 0; i < writerCount; i++)
        {
            int index = i;
            var thread = new Thread(() =>
            {
                try
                {
                    // Alternate between differently-spelled paths to the
                    // SAME file: uppercase drive/case variant, forward
                    // slashes, and the canonical spelling. All three must
                    // guard under the identical mutex (MutexNamingTests
                    // proves this in isolation; this proves it holds up
                    // under genuine concurrent contention).
                    string path = (index % 3) switch
                    {
                        0 => canonicalPath,
                        1 => canonicalPath.ToUpperInvariant(),
                        _ => canonicalPath.Replace('\\', '/'),
                    };

                    // A payload long enough that a torn/interleaved write
                    // (partial line from two writers landing on one line)
                    // would very likely break JSON parsing. Two DISTINCT
                    // payloads per writer (one per write below) so every
                    // line in the file is expected to be unique -- a
                    // repeated payload would itself indicate corruption.
                    string payloadViaCapture = $"payload-from-writer-{index:D3}-via-capture-" + new string('x', 200);
                    string payloadDirect = $"payload-from-writer-{index:D3}-via-direct-append-" + new string('x', 200);
                    byte[] stdin = Encoding.UTF8.GetBytes(payloadViaCapture);

                    var options = new ArgvParser.Options("concurrency-test", "Event" + index, "concurrent", dir.Path);
                    Recorder.Capture(options, stdin, envSession: null, envCaptureDir: null, baseDirectory: dir.Path, DateTimeOffset.UtcNow, pid: index);

                    // Exercise GuardedAppender.Append directly too (not only
                    // through Recorder.Capture) against the varied spelling,
                    // for a second, lower-level line on the same file.
                    GuardedAppender.Append(path, EnvelopeSerialization.Serialize(
                        new EventEnvelope { Ts = DateTimeOffset.UtcNow.ToString("O"), Host = "direct", Event = "Direct" + index, Pid = index, Session = "concurrent", Payload = payloadDirect }));
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
            threads.Add(thread);
        }

        foreach (var t in threads) t.Start();
        foreach (var t in threads) Assert.IsTrue(t.Join(TimeSpan.FromSeconds(30)), "a writer thread did not complete in time.");

        Assert.IsEmpty(exceptions, $"no writer thread may throw; first: {(exceptions.IsEmpty ? "" : exceptions.First())}");

        string resultFile = Path.Combine(dir.Path, "session-concurrent.jsonl");
        string[] lines = File.ReadAllLines(resultFile);

        // Recorder.Capture (writerCount lines) + the direct GuardedAppender.Append call (writerCount lines).
        Assert.HasCount(writerCount * 2, lines, "every writer's line must be present -- no line lost to a lost/overwritten write.");

        var seenIndices = new HashSet<string>();
        foreach (string line in lines)
        {
            // Must parse cleanly as JSON with all six fields -- a torn or
            // interleaved write would produce a line that fails to parse.
            using var doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            string payload = root.GetProperty("payload").GetString()!;
            Assert.IsTrue(seenIndices.Add(payload), $"duplicate/corrupted payload indicates a torn or interleaved write: {payload}");
            Assert.AreEqual(6, root.EnumerateObject().Count(), "every line must have exactly six fields even under contention.");
        }
    }
}
