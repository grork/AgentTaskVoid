using System.Text.Json;
using Codevoid.AgentTaskVoid.Persistence;

namespace Codevoid.AgentTaskVoid.LogicTests.Persistence;

/// <summary>
/// Covers phase-04 acceptance criterion 2: write/read round-trip; atomic
/// replace never yields a torn file; handle encoding round-trips hostile
/// handles through actual file names; <c>lastUpdate</c> refreshes on every
/// write.
/// </summary>
[TestClass]
public sealed class SidecarStoreTests
{
    [TestMethod]
    public void Write_Then_Read_RoundTrips()
    {
        using var dir = new TempDirectory();
        var store = new SidecarStore(dir.Path);
        var now = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

        store.Write("handle-1", "task-id-1", now);
        var entry = store.Read("handle-1");

        Assert.IsNotNull(entry);
        Assert.AreEqual("task-id-1", entry!.Id);
        Assert.AreEqual(now, entry.LastUpdate);
        Assert.AreEqual(SidecarEntry.CurrentSchemaVersion, entry.SchemaVersion);
    }

    [TestMethod]
    public void Read_UnknownHandle_ReturnsNull_NoThrow()
    {
        using var dir = new TempDirectory();
        var store = new SidecarStore(dir.Path);
        Assert.IsNull(store.Read("never-written"));
    }

    [TestMethod]
    public void Write_Twice_RefreshesLastUpdate_OnEveryWrite()
    {
        using var dir = new TempDirectory();
        var store = new SidecarStore(dir.Path);
        var first = new DateTimeOffset(2026, 7, 7, 9, 0, 0, TimeSpan.Zero);
        var second = first.AddHours(3);

        store.Write("h", "id-a", first);
        Assert.AreEqual(first, store.Read("h")!.LastUpdate);

        store.Write("h", "id-a", second); // same id, later stamp
        Assert.AreEqual(second, store.Read("h")!.LastUpdate, "lastUpdate must refresh on every write, even when the Id is unchanged");
    }

    [TestMethod]
    public void Write_Overwrites_PreviousEntry_LeavesExactlyOneFile_NoLeftoverTemp()
    {
        using var dir = new TempDirectory();
        var store = new SidecarStore(dir.Path);
        var now = DateTimeOffset.Now;

        store.Write("h", "id-1", now);
        store.Write("h", "id-2", now);

        var entry = store.Read("h");
        Assert.AreEqual("id-2", entry!.Id);
        Assert.HasCount(1, Directory.GetFiles(dir.Path));
    }

    [TestMethod]
    public void Delete_RemovesEntry_AndIsFalseOnSecondCall()
    {
        using var dir = new TempDirectory();
        var store = new SidecarStore(dir.Path);
        store.Write("h", "id", DateTimeOffset.Now);

        Assert.IsTrue(store.Delete("h"));
        Assert.IsNull(store.Read("h"));
        Assert.IsFalse(store.Delete("h"));
    }

    [TestMethod]
    public void ReadAll_ReturnsEveryEntry_WithHandleRecoveredFromFilename()
    {
        using var dir = new TempDirectory();
        var store = new SidecarStore(dir.Path);
        var now = DateTimeOffset.Now;
        store.Write("session/one", "id-1", now);
        store.Write("session/two", "id-2", now);

        var all = store.ReadAll();

        Assert.HasCount(2, all);
        CollectionAssert.AreEquivalent(
            new[] { "session/one", "session/two" },
            all.Select(x => x.Handle).ToArray());
    }

    [TestMethod]
    public void ReadAll_OnMissingDirectory_ReturnsEmpty_NoThrow()
    {
        using var dir = new TempDirectory(); // never written to -- directory doesn't exist yet
        var store = new SidecarStore(dir.Path);
        Assert.IsEmpty(store.ReadAll());
    }

    [TestMethod]
    public void HostileHandles_RoundTripThroughActualFileNames_PathSeparatorsUnicodeAndLongStrings()
    {
        using var dir = new TempDirectory();
        var store = new SidecarStore(dir.Path);
        string[] hostileHandles =
        [
            "path/with/slashes",
            @"C:\Users\dev\transcript.jsonl",
            "unicode-japanese-and-emoji-\U0001F680",
            new string('x', 120) + "-long-handle-with-suffix",
        ];

        int i = 0;
        foreach (string handle in hostileHandles)
            store.Write(handle, $"id-for-handle-{i++}", DateTimeOffset.Now);

        var all = store.ReadAll();
        CollectionAssert.AreEquivalent(hostileHandles, all.Select(x => x.Handle).ToArray());
        foreach (string handle in hostileHandles)
            Assert.IsNotNull(store.Read(handle), $"round trip failed for handle: {handle}");
    }

    [TestMethod]
    public void Read_CorruptFile_ReturnsNull_DegradesGracefully_NoThrow()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Path);
        string encoded = HandleEncoding.Encode("h");
        File.WriteAllText(Path.Combine(dir.Path, encoded + ".json"), "{ not valid json ][");

        var store = new SidecarStore(dir.Path);
        Assert.IsNull(store.Read("h"));
    }

    [TestMethod]
    public void ConcurrentWriteAndRead_AtomicReplace_NeverYieldsATornFile()
    {
        using var dir = new TempDirectory();
        var store = new SidecarStore(dir.Path);
        const string handle = "atomic-handle";
        const int iterations = 300;
        Exception? failure = null;

        var writer = Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
                store.Write(handle, $"id-{i:D6}", DateTimeOffset.Now);
        });

        // Direct raw-bytes verification loop -- deliberately bypasses the
        // store's own graceful degrade-to-null-on-corrupt Read() path, so a
        // genuinely torn write would be caught here rather than silently
        // read as "not yet written".
        string path = Path.Combine(dir.Path, HandleEncoding.Encode(handle) + ".json");
        var rawReader = Task.Run(() =>
        {
            while (!writer.IsCompleted)
            {
                if (!File.Exists(path)) continue;

                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(path);
                }
                catch (IOException)
                {
                    continue; // benign sharing-violation race with the rename itself
                }
                if (bytes.Length == 0) continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<SidecarEntry>(bytes);
                    if (entry is null || !entry.Id.StartsWith("id-", StringComparison.Ordinal))
                        throw new InvalidOperationException($"Torn/invalid read: {System.Text.Encoding.UTF8.GetString(bytes)}");
                }
                catch (JsonException ex)
                {
                    failure = new InvalidOperationException($"Torn file: {System.Text.Encoding.UTF8.GetString(bytes)}", ex);
                    return;
                }
            }
        });

        Task.WaitAll(writer, rawReader);
        Assert.IsNull(failure, failure?.ToString());
    }
}
