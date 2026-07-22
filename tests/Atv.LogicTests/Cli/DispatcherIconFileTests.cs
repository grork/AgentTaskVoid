using Codevoid.AgentTaskVoid.Diagnostics;
using Codevoid.AgentTaskVoid.IconRendering;

namespace Codevoid.AgentTaskVoid.LogicTests.Cli;

/// <summary>
/// Phase-16 acceptance criterion 5 at the full CLI-dispatch level:
/// <c>--icon-file &lt;path&gt;</c> resolves through <c>Dispatcher.TryResolveIconToken</c>
/// exactly like <c>--icon</c> does, into a real per-handle icon written by
/// <c>IconService</c>; supplying BOTH <c>--icon</c> and <c>--icon-file</c> on
/// one call is a usage error under the same non-disruptive posture every
/// other argument-shape failure uses (ERGO-27). Fake-backed, same
/// <see cref="DispatcherHarness"/> rig as <see cref="DispatcherSemanticVerbsTests"/>.
/// </summary>
[TestClass]
public sealed class DispatcherIconFileTests
{
    [TestMethod]
    public void Working_IconFile_ValidImage_ProducesNormalizedPerHandleIcon()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        byte[] source = ShapeRenderer.RenderDefaultShape(128).PngBytes!;
        string sourcePath = Path.Combine(Path.GetTempPath(), $"atv-dispatcher-icon-file-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(sourcePath, source);
        try
        {
            int exit = h.Run(dispatcher, "working", "h1", "--icon-file", sourcePath);

            Assert.AreEqual(0, exit);
            var view = h.Store.FindAll().Single();
            Assert.IsTrue(File.Exists(view.IconUri.LocalPath));
            byte[] expected = RasterNormalizer.Normalize(source, Codevoid.AgentTaskVoid.Icons.IconService.DefaultSizePx).PngBytes!;
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(view.IconUri.LocalPath));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public void Working_IconFlagWithAPathLikeValue_AlsoNormalizes_TheFoldedRawPathHatch()
    {
        // The previously-hidden IconTokenKind.RawPath hatch (any --icon
        // value that's neither a curated name nor a single character) is
        // folded into the SAME supported/validated/normalized path as the
        // dedicated --icon-file flag (phase 16, ERGO-29) -- not just
        // documented as such, actually exercised end to end here.
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        byte[] source = ShapeRenderer.RenderDefaultShape(100).PngBytes!;
        string sourcePath = Path.Combine(Path.GetTempPath(), $"atv-dispatcher-icon-file-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(sourcePath, source);
        try
        {
            int exit = h.Run(dispatcher, "working", "h1", "--icon", sourcePath);

            Assert.AreEqual(0, exit);
            var view = h.Store.FindAll().Single();
            byte[] expected = RasterNormalizer.Normalize(source, Codevoid.AgentTaskVoid.Icons.IconService.DefaultSizePx).PngBytes!;
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(view.IconUri.LocalPath), "a path-shaped --icon value must go through the same validation/normalization as --icon-file, not a raw unvalidated byte-copy.");
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public void AgentStarted_IconFile_SharedResolution_AlsoWorks()
    {
        // TryResolveIconToken is one shared implementation every upserting
        // verb body calls -- a second verb exercises the same code path
        // structurally (not a per-verb reimplementation to regress).
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        byte[] source = ShapeRenderer.RenderDefaultShape(96).PngBytes!;
        string sourcePath = Path.Combine(Path.GetTempPath(), $"atv-dispatcher-icon-file-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(sourcePath, source);
        try
        {
            int exit = h.Run(dispatcher, "agent-started", "h1", "--icon-file", sourcePath);
            Assert.AreEqual(0, exit);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public void Working_IconAndIconFileTogether_NonStrict_SilentZero_LogsUsageError()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "working", "h1", "--icon", "Robot", "--icon-file", "logo.png");

        Assert.AreEqual(0, exit);
        Assert.IsEmpty(h.Store.FindAll(), "a usage-error call must never reach the engine as a claim.");
        Assert.HasCount(1, h.LogEntriesExcludingTrace());
    }

    [TestMethod]
    public void Working_IconAndIconFileTogether_Strict_ReturnsInvalidArgumentsExitCode()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "working", "h1", "--icon", "Robot", "--icon-file", "logo.png");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
    }

    [TestMethod]
    public void Broken_IconAndIconFileTogether_AlsoRejected()
    {
        // Same shared TryResolveIconToken -- a second verb confirms the
        // conflict rule isn't a one-off check special-cased to `working`.
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "broken", "h1", "--reason", "fatal", "--icon", "Bug", "--icon-file", "logo.png");

        Assert.AreEqual((int)FailureKind.InvalidArguments, exit);
    }

    [TestMethod]
    public void Working_IconFile_MissingSourceFile_FallsBackToDefault_NonDisruptive()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "working", "h1", "--icon-file", Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.png"));

        Assert.AreEqual(0, exit, "a bad --icon-file source must degrade down the fallback chain (FAIL-1), never fail the whole verb.");
        var view = h.Store.FindAll().Single();
        Assert.IsTrue(File.Exists(view.IconUri.LocalPath));
    }

    // ==== AC1/AC9 at the full CLI-dispatch level: the dispatcher no longer places,
    // so a burst of plain (no --icon) updates must leave the create-time icon file
    // byte-for-byte untouched, and an explicit --icon on an update must rewrite it. ==

    [TestMethod]
    public void ExplicitIconFile_SurvivesABurstOfPlainUpdates_ByteIdentical()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        byte[] source = ShapeRenderer.RenderDefaultShape(64).PngBytes!;
        string sourcePath = Path.Combine(Path.GetTempPath(), $"atv-dispatcher-icon-survive-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(sourcePath, source);
        try
        {
            h.Run(dispatcher, "working", "h1", "--icon-file", sourcePath, "--goal", "g");
            var afterCreate = h.Store.FindAll().Single();
            Uri createTimeIconUri = afterCreate.IconUri;
            byte[] createTimeBytes = File.ReadAllBytes(createTimeIconUri.LocalPath);

            h.Run(dispatcher, "activity", "h1", "--kind", "read", "--label", "a.txt");
            h.Run(dispatcher, "activity", "h1", "--kind", "edit", "--label", "b.txt");
            h.Run(dispatcher, "working", "h1", "--goal", "g2");

            var afterBurst = h.Store.FindAll().Single();
            Assert.AreEqual(createTimeIconUri, afterBurst.IconUri, "IconUri must be unchanged across a burst of plain updates.");
            CollectionAssert.AreEqual(createTimeBytes, File.ReadAllBytes(afterBurst.IconUri.LocalPath), "the on-disk PNG must be byte-identical to the create-time bytes.");
            Assert.IsNotEmpty(afterBurst.CompletedSteps, "no forced recreate must have fired -- step history survives.");
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [TestMethod]
    public void ExplicitIconFile_OnAnUpdate_DoesRewriteTheFile()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();
        byte[] firstSource = ShapeRenderer.RenderDefaultShape(64).PngBytes!;
        byte[] secondSource = ShapeRenderer.RenderDefaultShape(96).PngBytes!;
        string firstPath = Path.Combine(Path.GetTempPath(), $"atv-first-{Guid.NewGuid():N}.png");
        string secondPath = Path.Combine(Path.GetTempPath(), $"atv-second-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(firstPath, firstSource);
        File.WriteAllBytes(secondPath, secondSource);
        try
        {
            h.Run(dispatcher, "working", "h1", "--icon-file", firstPath, "--goal", "g");
            h.Run(dispatcher, "working", "h1", "--icon-file", secondPath, "--goal", "g2");

            var view = h.Store.FindAll().Single();
            byte[] expected = RasterNormalizer.Normalize(secondSource, Codevoid.AgentTaskVoid.Icons.IconService.DefaultSizePx).PngBytes!;
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(view.IconUri.LocalPath), "an explicit --icon-file on an update must actually rewrite the file.");
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }
}
