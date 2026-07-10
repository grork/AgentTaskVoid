using Atv.Diagnostics;
using Atv.Store;

namespace Atv.LogicTests.Cli;

/// <summary>
/// AC1's `start` coverage: defaults (icon per-handle path, deepLink file:
/// URI), the ERGO-2 hidden sweep firing on create, required-handle
/// enforcement, and argument-shape validation for `--icon`/`--deep-link`.
/// </summary>
[TestClass]
public sealed class DispatcherStartTests
{
    [TestMethod]
    public void Start_NoIconFlag_RendersDefaultGlyphToAPerHandleFile()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "start", "h1");

        Assert.AreEqual(0, exit);
        var view = h.Store.FindAll().Single();
        Assert.IsTrue(File.Exists(view.IconUri.LocalPath), "the default glyph must be rendered to a real per-handle file.");
    }

    [TestMethod]
    public void Start_DifferentHandles_GetDistinctIconFiles()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "start", "h1");
        h.Run(dispatcher, "start", "h2");

        var views = h.Store.FindAll();
        Assert.HasCount(2, views);
        Assert.AreNotEqual(views[0].IconUri, views[1].IconUri, "ERGO-15: separation-by-session preserved even on defaults.");
    }

    [TestMethod]
    public void Start_WithIconToken_Succeeds()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "start", "h1", "--icon", "Bug");

        Assert.AreEqual(0, exit);
        var view = h.Store.FindAll().Single();
        Assert.IsTrue(File.Exists(view.IconUri.LocalPath));
    }

    [TestMethod]
    public void Start_EmptyIconToken_IsInvalidArguments()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "start", "h1", "--icon", "");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
        Assert.IsEmpty(h.Store.FindAll());
    }

    [TestMethod]
    public void Start_NoDeepLinkFlag_DefaultsToFileUriUnderAppData()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "start", "h1");

        var view = h.Store.FindAll().Single();
        Assert.AreEqual("file", view.DeepLink.Scheme);
        StringAssert.StartsWith(view.DeepLink.LocalPath, h.AppDataRoot);
    }

    [TestMethod]
    public void Start_WithDeepLinkFlag_UsesProvidedUri()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "start", "h1", "--deep-link", "https://example.com/custom");

        var view = h.Store.FindAll().Single();
        Assert.AreEqual(new Uri("https://example.com/custom"), view.DeepLink);
    }

    [TestMethod]
    public void Start_InvalidDeepLinkUri_IsInvalidArguments_NonStrictSilentZero_WithLogEntry()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "start", "h1", "--deep-link", "not a uri");

        Assert.AreEqual(0, exit, "non-strict default: silent zero even on invalid args.");
        Assert.AreEqual("", h.Stdout.ToString());
        Assert.IsEmpty(h.Store.FindAll());
        Assert.HasCount(1, h.Log.ReadAll());
    }

    [TestMethod]
    public void Start_InvalidDeepLinkUri_Strict_ReturnsExitFour()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "start", "h1", "--deep-link", "not a uri");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
    }

    [TestMethod]
    public void Start_MissingHandle_NonStrictSilentZero_WithLogEntry()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "start");

        Assert.AreEqual(0, exit);
        Assert.IsEmpty(h.Store.FindAll());
        Assert.HasCount(1, h.Log.ReadAll());
    }

    [TestMethod]
    public void Start_MissingHandle_Strict_ReturnsExitFour()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "start");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
    }

    [TestMethod]
    public void Start_TriggersHiddenSweep_OnCreate()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "start", "h1");
        var h1Id = h.Store.FindAll().Single().Id;
        h.Store.SetHiddenByUser(h1Id, true);

        h.Run(dispatcher, "start", "h2");

        Assert.IsNull(h.Store.Find(h1Id), "ERGO-2: start (create) sweeps HiddenByUser tasks.");
        Assert.IsNull(h.Sidecar.Read("h1"));
        Assert.IsNotNull(h.Sidecar.Read("h2"));
    }

    [TestMethod]
    public void Start_Json_Success_PrintsOkTrueShape()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(json: true);

        h.Run(dispatcher, "start", "h1", "--title", "T");

        StringAssert.Contains(h.Stdout.ToString(), "\"ok\":true");
    }

    [TestMethod]
    public void Start_TitleAndSubtitle_AreAppliedFromFlags()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "start", "h1", "--title", "My Title", "--subtitle", "My Subtitle");

        var view = h.Store.FindAll().Single();
        Assert.AreEqual("My Title", view.Title);
        Assert.AreEqual("My Subtitle", view.Subtitle);
    }

    [TestMethod]
    public void Start_NoTitleFlag_DefaultsToEmptyString()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "start", "h1");

        var view = h.Store.FindAll().Single();
        Assert.AreEqual("", view.Title);
        Assert.AreEqual("", view.Subtitle);
    }
}
