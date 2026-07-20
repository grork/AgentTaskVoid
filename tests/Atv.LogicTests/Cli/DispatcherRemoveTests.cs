using Codevoid.AgentTaskVoid.Diagnostics;

namespace Codevoid.AgentTaskVoid.LogicTests.Cli;

/// <summary>AC1's `remove` coverage: task+sidecar+icon removal, the ERGO-2 hidden sweep on remove, and required-handle enforcement.</summary>
[TestClass]
public sealed class DispatcherRemoveTests
{
    [TestMethod]
    public void Remove_ExistingHandle_RemovesTaskSidecarAndIcon()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");
        var view = h.Store.FindAll().Single();
        string iconPath = view.IconUri.LocalPath;
        Assert.IsTrue(File.Exists(iconPath));

        int exit = h.Run(dispatcher, "remove", "h1");

        Assert.AreEqual(0, exit);
        Assert.IsEmpty(h.Store.FindAll());
        Assert.IsNull(h.Sidecar.Read("h1"));
        Assert.IsFalse(File.Exists(iconPath), "ERGO-23: remove must reap the per-handle icon copy too.");
    }

    [TestMethod]
    public void Remove_TriggersHiddenSweep()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        h.Run(dispatcher, "working", "h1");
        h.Run(dispatcher, "working", "h2");
        var h1Id = h.Sidecar.Read("h1")!.Id;
        h.Store.SetHiddenByUser(h1Id, true);

        h.Run(dispatcher, "remove", "h2");

        Assert.IsNull(h.Store.Find(h1Id), "ERGO-2: remove also sweeps HiddenByUser tasks.");
        Assert.IsNull(h.Sidecar.Read("h1"));
    }

    [TestMethod]
    public void Remove_UnknownHandle_NonStrictSilentZero_WithLogEntry()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "remove", "never-seen");

        Assert.AreEqual(0, exit);
        Assert.HasCount(1, h.LogEntriesExcludingTrace());
    }

    [TestMethod]
    public void Remove_UnknownHandle_Strict_ReturnsNonZeroExit()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "remove", "never-seen");

        Assert.AreNotEqual(0, exit, "ERGO-27: an unknown-handle failure behaves like every other failure -- strict exits non-zero.");
    }

    [TestMethod]
    public void Remove_MissingHandle_NonStrictSilentZero_StrictExitFour()
    {
        using var h = new DispatcherHarness();

        Assert.AreEqual(0, h.Run(h.BuildDispatcher(), "remove"));
        Assert.AreEqual((int)FailureKind.InvalidArguments, h.Run(h.BuildDispatcher(strict: true), "remove"));
    }

    [TestMethod]
    public void Remove_Json_Success_PrintsOkTrueShape()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(json: true);
        h.Run(dispatcher, "working", "h1");
        h.Stdout.GetStringBuilder().Clear();

        h.Run(dispatcher, "remove", "h1");

        StringAssert.Contains(h.Stdout.ToString(), "\"ok\":true");
    }
}
