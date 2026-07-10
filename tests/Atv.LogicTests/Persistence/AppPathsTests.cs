using Atv.Persistence;

namespace Atv.LogicTests.Persistence;

/// <summary>
/// AppPaths supports every other phase-04 mechanism's testing seam (temp-dir
/// injection, AC5) and centralizes PFN derivation for the INFRA-6 write
/// mutex name so no downstream phase hardcodes one (plan/README.md standing
/// invariant #3).
/// </summary>
[TestClass]
public sealed class AppPathsTests
{
    [TestMethod]
    public void ForRoot_DerivesEveryPath_UnderTheInjectedRoot()
    {
        var paths = AppPaths.ForRoot(@"C:\fake-root");

        Assert.AreEqual(@"C:\fake-root", paths.Root);
        Assert.AreEqual(Path.Combine(@"C:\fake-root", "sidecar"), paths.SidecarDir);
        Assert.AreEqual(Path.Combine(@"C:\fake-root", "recycle-bin"), paths.RecycleBinDir);
        Assert.AreEqual(Path.Combine(@"C:\fake-root", "icons"), paths.IconsDir);
        Assert.AreEqual(Path.Combine(@"C:\fake-root", $"{Branding.Command}.log"), paths.LogPath);
        Assert.AreEqual(Path.Combine(@"C:\fake-root", $"{Branding.Command}-config.json"), paths.ConfigPath);
    }

    [TestMethod]
    public void ForRoot_NullRoot_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AppPaths.ForRoot(null!));
    }

    [TestMethod]
    public void BuildWriteMutexName_IncludesBrandAndPfn_FormatIsLocalScoped()
    {
        string name = AppPaths.BuildWriteMutexName("Agentaskvoid_abc123xyz");

        Assert.AreEqual(@"Local\Agentaskvoid-Agentaskvoid_abc123xyz-tasks-write", name);
        StringAssert.Contains(name, Branding.Name);
        StringAssert.Contains(name, "Agentaskvoid_abc123xyz");
        StringAssert.StartsWith(name, @"Local\");
        StringAssert.EndsWith(name, "-tasks-write");
    }

    [TestMethod]
    public void BuildWriteMutexName_DifferentPfns_ProduceDifferentNames()
    {
        // The DIST-3 isolation guarantee at the naming level: distinct PFNs
        // (release / dev-interactive / per-worktree test pools) must never
        // collide onto the same mutex name.
        string a = AppPaths.BuildWriteMutexName("pfn-pool-a");
        string b = AppPaths.BuildWriteMutexName("pfn-pool-b");
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void BuildWatchdogMutexName_IncludesBrandAndPfn_FormatIsLocalScoped()
    {
        string name = AppPaths.BuildWatchdogMutexName("Agentaskvoid_abc123xyz");

        Assert.AreEqual(@"Local\Agentaskvoid-Agentaskvoid_abc123xyz-watchdog", name);
        StringAssert.Contains(name, Branding.Name);
        StringAssert.StartsWith(name, @"Local\");
        StringAssert.EndsWith(name, "-watchdog");
    }

    [TestMethod]
    public void BuildWatchdogMutexName_DiffersFromWriteMutexName_SamePfn()
    {
        // Two distinct named objects for the same identity (LIFE-18 vs INFRA-6) --
        // must never collide onto the same OS mutex name.
        string write = AppPaths.BuildWriteMutexName("pfn-pool-a");
        string watchdog = AppPaths.BuildWatchdogMutexName("pfn-pool-a");
        Assert.AreNotEqual(write, watchdog);
    }
}
