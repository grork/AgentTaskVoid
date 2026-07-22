using System.Runtime.CompilerServices;

namespace Codevoid.AgentTaskVoid.LogicTests.Cli;

/// <summary>
/// AC9's structural proof (Part 1 item 3): none of the seven upserting verb
/// bodies in <c>Dispatcher.cs</c> call <c>IconService.Place</c> anymore --
/// icon file placement moved into <c>SemanticEngine</c>, the only layer that
/// knows create from update. A textual source check (same technique as
/// <c>Architecture.SeamPurityTests</c>) rather than a runtime spy, because
/// <c>IconService</c> is sealed with no interface -- it cannot be mocked/
/// counted the way <c>IAppTaskStore</c> can (see
/// <c>CountingAppTaskStore</c>'s own remarks on this exact limitation).
/// </summary>
[TestClass]
public sealed class DispatcherIconPlacementStructuralTests
{
    [TestMethod]
    public void DispatcherSource_NeverCallsIconServicePlace()
    {
        string path = Path.Combine(RepoRoot(), "src", "Atv", "Cli", "Dispatcher.cs");
        Assert.IsTrue(File.Exists(path), $"expected to find {path}");

        string source = File.ReadAllText(path);
        Assert.IsFalse(source.Contains("_icons.Place(", StringComparison.Ordinal),
            "Dispatcher.cs must never call IconService.Place -- placement now lives entirely in SemanticEngine (Part 1 item 3).");
    }

    [TestMethod]
    public void RunVerbSource_NeverCallsIconServicePlace()
    {
        string path = Path.Combine(RepoRoot(), "src", "Atv", "Cli", "Verbs", "RunVerb.cs");
        Assert.IsTrue(File.Exists(path), $"expected to find {path}");

        string source = File.ReadAllText(path);
        Assert.IsFalse(source.Contains("Icons.Place(", StringComparison.Ordinal),
            "RunVerb.cs must never call IconService.Place either -- run adopts the engine path (Part 1 item 6), dropping its own pre-place.");
    }

    /// <summary>The grep itself is not vacuous -- Dispatcher.cs DOES still reference <c>IconService</c> (the injected <c>_icons</c> field, still threaded into <c>RunDeps</c>), so a check pointed at the wrong string/file would silently pass for the wrong reason.</summary>
    [TestMethod]
    public void TheGrepItself_IsNotVacuous_DispatcherStillReferencesIconService()
    {
        string path = Path.Combine(RepoRoot(), "src", "Atv", "Cli", "Dispatcher.cs");
        string source = File.ReadAllText(path);
        Assert.IsTrue(source.Contains("IconService", StringComparison.Ordinal),
            "Dispatcher.cs no longer references IconService at all -- did the injected _icons field get removed?");
    }

    private static string RepoRoot([CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AppTaskInfoCli.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException($"Could not locate the repo root (AppTaskInfoCli.slnx) above {here}");

        return dir.FullName;
    }
}
