using System.Text.Json;
using Atv.Cli.Verbs;
using Atv.Config;
using Atv.Diagnostics;
using Atv.Store;

namespace Atv.LogicTests.Cli;

/// <summary>
/// AC1's `list` coverage: empty, multiple tasks, `--json` array shape,
/// handle<->task correlation via the sidecar, and ENTRYLESS tasks (a live
/// API task with no sidecar entry) still listed -- ERGO-16's identity-global
/// truth. Also: not a write-path verb (no watchdog-ensure, no store
/// mutation), and the standard Capability-gated non-disruptive posture.
/// </summary>
[TestClass]
public sealed class ListVerbTests
{
    [TestMethod]
    public void List_EmptyStore_ExitsZero_NoStdoutRows()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "list");

        Assert.AreEqual(0, exit);
        Assert.AreEqual("", h.Stdout.ToString());
    }

    [TestMethod]
    public void List_EmptyStore_Json_PrintsEmptyArray()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(json: true);

        h.Run(dispatcher, "list");

        Assert.AreEqual("[]", h.Stdout.ToString().Trim());
    }

    [TestMethod]
    public void List_MultipleLiveHandles_CorrelatesEachHandleToItsTask()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1", "--title", "Title One");
        h.Run(dispatcher, "working", "h2", "--title", "Title Two");

        h.Run(dispatcher, "list");

        string output = h.Stdout.ToString();
        StringAssert.Contains(output, "h1");
        StringAssert.Contains(output, "Title One");
        StringAssert.Contains(output, "h2");
        StringAssert.Contains(output, "Title Two");
    }

    [TestMethod]
    public void List_Json_ArrayShape_ContainsHandleTitleStateExecutingStepLastUpdate()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(json: true);
        h.Run(dispatcher, "working", "h1", "--title", "T");
        h.Stdout.GetStringBuilder().Clear(); // drop start's own {"ok":..} json line

        h.Run(dispatcher, "list");

        using var doc = JsonDocument.Parse(h.Stdout.ToString());
        Assert.AreEqual(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.HasCount(1, doc.RootElement.EnumerateArray().ToArray());
        var row = doc.RootElement[0];
        Assert.AreEqual("h1", row.GetProperty("handle").GetString());
        Assert.AreEqual("T", row.GetProperty("title").GetString());
        Assert.AreEqual("running", row.GetProperty("state").GetString());
        Assert.IsTrue(row.TryGetProperty("executingStep", out _));
        Assert.IsTrue(row.TryGetProperty("lastUpdate", out _));
    }

    [TestMethod]
    public void List_EntrylessTask_StillListed_WithNullHandle()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(json: true);
        h.Store.SeedEntrylessTask("Orphan Title", "Sub");

        h.Run(dispatcher, "list");

        using var doc = JsonDocument.Parse(h.Stdout.ToString());
        Assert.HasCount(1, doc.RootElement.EnumerateArray().ToArray());
        var row = doc.RootElement[0];
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("handle").ValueKind);
        Assert.AreEqual("Orphan Title", row.GetProperty("title").GetString());
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("lastUpdate").ValueKind);
    }

    [TestMethod]
    public void List_MixOfEntryTrackedAndEntryless_BothAppear()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(json: true);
        h.Run(dispatcher, "working", "h1");
        h.Store.SeedEntrylessTask("Orphan", "Sub");
        h.Stdout.GetStringBuilder().Clear(); // drop start's own {"ok":..} json line

        h.Run(dispatcher, "list");

        using var doc = JsonDocument.Parse(h.Stdout.ToString());
        Assert.HasCount(2, doc.RootElement.EnumerateArray().ToArray());
    }

    [TestMethod]
    public void List_DoesNotMutateStoreOrSidecar()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");

        h.Run(dispatcher, "list");
        h.Run(dispatcher, "list");

        Assert.HasCount(1, h.Store.FindAll());
        Assert.IsNotNull(h.Sidecar.Read("h1"));
    }

    [TestMethod]
    public void List_NeverEnsuresWatchdog_NotAWritePathVerb()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(watchdogMode: WatchdogMode.Spawn);

        h.Run(dispatcher, "list");

        Assert.AreEqual(0, h.ProcessHost.StartCallCount, "list is read-only -- it must never trigger the LIFE-17 watchdog-ensure gate.");
    }

    [TestMethod]
    public void List_NoIdentity_NonStrict_ExitsZero_Silent_LogsOneEntry()
    {
        using var h = new DispatcherHarness { HasIdentity = false };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "list");

        Assert.AreEqual(0, exit);
        Assert.AreEqual("", h.Stdout.ToString());
        Assert.HasCount(1, h.Log.ReadAll());
    }

    [TestMethod]
    public void List_NoIdentity_Strict_ReturnsIdentityNotRegisteredExitCode()
    {
        using var h = new DispatcherHarness { HasIdentity = false };
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "list");

        Assert.AreEqual((int)FailureKind.IdentityNotRegistered, exit);
    }

    [TestMethod]
    public void List_ApiUnsupported_Strict_ReturnsApiUnavailableExitCode()
    {
        using var h = new DispatcherHarness();
        h.Store.Supported = false;
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "list");

        Assert.AreEqual((int)FailureKind.ApiUnavailable, exit);
    }

    [TestMethod]
    public void List_Json_OnFailure_DoesNotEmitTheGenericMutatingResultShape()
    {
        using var h = new DispatcherHarness { HasIdentity = false };
        var dispatcher = h.BuildDispatcher(json: true);

        h.Run(dispatcher, "list");

        // list --json's ONLY shape is a task array (ERGO-27 C5) -- it must
        // never fall back to the generic {"ok":..} mutating-verb shape.
        Assert.IsFalse(h.Stdout.ToString().Contains("\"ok\"", StringComparison.Ordinal));
    }
}
