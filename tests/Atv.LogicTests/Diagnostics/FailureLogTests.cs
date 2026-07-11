using Atv.Diagnostics;
using Atv.LogicTests.Persistence;

namespace Atv.LogicTests.Diagnostics;

/// <summary>FAIL-1/FAIL-3: the durable failure log, its {timestamp, verb, handle, error, buildKind} shape (the trailing marker field is DIST-3's 2026-07-10 amendment), size/age rotation, and the hard "never throws" requirement (phase-06 AC3).</summary>
[TestClass]
public sealed class FailureLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Append_WritesOneEntry_WithAllFourFields()
    {
        using var dir = new TempDirectory();
        var log = new FailureLog(Path.Combine(dir.Path, "atv.log"), maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));

        log.Append("step", "h1", "boom", Now);

        var entries = log.ReadAll();
        Assert.HasCount(1, entries);
        Assert.AreEqual(Now, entries[0].Timestamp);
        Assert.AreEqual("step", entries[0].Verb);
        Assert.AreEqual("h1", entries[0].Handle);
        Assert.AreEqual("boom", entries[0].Error);
    }

    // ---- DIST-3 (2026-07-10 amendment): the (dev)/(test) build-kind marker --------

    [TestMethod]
    public void Append_NoBuildKindMarkerSupplied_DefaultsToNull_BackwardCompatible()
    {
        using var dir = new TempDirectory();
        var log = new FailureLog(Path.Combine(dir.Path, "atv.log"), maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));

        log.Append("step", "h1", "boom", Now);

        Assert.IsNull(log.ReadAll()[0].BuildKind);
    }

    [TestMethod]
    public void Append_BuildKindMarkerSupplied_StampsEveryEntry()
    {
        using var dir = new TempDirectory();
        var log = new FailureLog(Path.Combine(dir.Path, "atv.log"), maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14), buildKindMarker: "(dev)");

        log.Append("start", "h1", "e1", Now);
        log.Append("step", "h2", "e2", Now.AddSeconds(1));

        var entries = log.ReadAll();
        Assert.HasCount(2, entries);
        Assert.AreEqual("(dev)", entries[0].BuildKind);
        Assert.AreEqual("(dev)", entries[1].BuildKind);
    }

    [TestMethod]
    public void Append_NullHandle_IsPreserved()
    {
        using var dir = new TempDirectory();
        var log = new FailureLog(Path.Combine(dir.Path, "atv.log"), maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));

        log.Append("doctor", null, "no identity", Now);

        Assert.IsNull(log.ReadAll()[0].Handle);
    }

    [TestMethod]
    public void Append_MultipleEntries_AppendsInOrder()
    {
        using var dir = new TempDirectory();
        var log = new FailureLog(Path.Combine(dir.Path, "atv.log"), maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));

        log.Append("start", "h1", "e1", Now);
        log.Append("step", "h1", "e2", Now.AddSeconds(1));
        log.Append("remove", "h2", "e3", Now.AddSeconds(2));

        var entries = log.ReadAll();
        Assert.HasCount(3, entries);
        Assert.AreEqual("e1", entries[0].Error);
        Assert.AreEqual("e2", entries[1].Error);
        Assert.AreEqual("e3", entries[2].Error);
    }

    [TestMethod]
    public void ReadAll_NoFileYet_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var log = new FailureLog(Path.Combine(dir.Path, "does-not-exist.log"), maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));

        Assert.IsEmpty(log.ReadAll());
    }

    [TestMethod]
    public void ReadAll_SkipsCorruptLines_DegradesGracefully()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Path);
        string path = Path.Combine(dir.Path, "atv.log");
        var log = new FailureLog(path, maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));
        log.Append("start", "h1", "good", Now);

        File.AppendAllText(path, "{ not valid json at all" + Environment.NewLine);

        var entries = log.ReadAll();
        Assert.HasCount(1, entries);
        Assert.AreEqual("good", entries[0].Error);
    }

    [TestMethod]
    public void Append_NeverThrows_WhenTheDirectoryCannotBeCreated()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Path);
        // Create a FILE where the log's parent directory would need to go --
        // Directory.CreateDirectory must fail against an existing file of the same name.
        string blockingFile = Path.Combine(dir.Path, "blocked");
        File.WriteAllText(blockingFile, "im a file, not a directory");
        string logPath = Path.Combine(blockingFile, "nested", "atv.log");

        var log = new FailureLog(logPath, maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));

        // Must not throw (FAIL-1: logging must never violate the non-disruptive posture).
        log.Append("start", "h1", "boom", Now);
        Assert.IsEmpty(log.ReadAll());
    }

    [TestMethod]
    public void RotateIfNeeded_TriggersOnSize_KeepsOnlyNewestGenerationActive()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "atv.log");
        // maxBytes=1 guarantees any first entry already exceeds it, forcing rotation on entry #2.
        var log = new FailureLog(path, maxBytes: 1, maxAge: TimeSpan.FromDays(365));

        log.Append("start", "h1", "first", Now);
        log.Append("step", "h1", "second", Now.AddSeconds(1));

        var entries = log.ReadAll();
        Assert.HasCount(1, entries);
        Assert.AreEqual("second", entries[0].Error);
        Assert.IsTrue(File.Exists(path + ".1"), "Expected a rotated backup file.");
    }

    [TestMethod]
    public void RotateIfNeeded_TriggersOnAge_MeasuredFromOldestEntryTimestamp()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "atv.log");
        var log = new FailureLog(path, maxBytes: 1_000_000, maxAge: TimeSpan.FromHours(1));

        log.Append("start", "h1", "first", Now);
        // Second call happens (by caller-supplied wall clock) 2h later -- past the 1h maxAge.
        log.Append("step", "h1", "second", Now.AddHours(2));

        var entries = log.ReadAll();
        Assert.HasCount(1, entries);
        Assert.AreEqual("second", entries[0].Error);
        Assert.IsTrue(File.Exists(path + ".1"), "Expected a rotated backup file.");
    }

    [TestMethod]
    public void RotateIfNeeded_DoesNotTrigger_WhenUnderBothThresholds()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "atv.log");
        var log = new FailureLog(path, maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));

        log.Append("start", "h1", "first", Now);
        log.Append("step", "h1", "second", Now.AddMinutes(5));

        Assert.HasCount(2, log.ReadAll());
        Assert.IsFalse(File.Exists(path + ".1"));
    }

    [TestMethod]
    public void Rotate_OverwritesAnyPreviousBackup_OnlyOneGenerationKept()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "atv.log");
        var log = new FailureLog(path, maxBytes: 1, maxAge: TimeSpan.FromDays(365));

        log.Append("start", "h1", "gen1", Now);
        log.Append("step", "h1", "gen2", Now.AddSeconds(1)); // rotates gen1 into .1
        log.Append("step", "h1", "gen3", Now.AddSeconds(2)); // rotates gen2 into .1, replacing gen1's backup

        string backup = File.ReadAllText(path + ".1");
        StringAssert.Contains(backup, "gen2");
        StringAssert.DoesNotMatch(backup, new System.Text.RegularExpressions.Regex("gen1"));
    }
}
