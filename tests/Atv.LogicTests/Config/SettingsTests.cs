using Codevoid.AgentTaskVoid.Config;

namespace Codevoid.AgentTaskVoid.LogicTests.Config;

/// <summary>Sanity check on the concrete LIFE-22/INFRA-6/FAIL-3 numbers baked into <see cref="Settings.Default"/> -- guards against an accidental edit silently drifting a default (e.g. minutes vs. hours) unnoticed.</summary>
[TestClass]
public sealed class SettingsTests
{
    [TestMethod]
    public void Default_MatchesTheDocumentedLife22IdlePeriods()
    {
        var d = Settings.Default;

        Assert.AreEqual(TimeSpan.FromMinutes(30), d.IdleRunning);
        Assert.AreEqual(TimeSpan.FromHours(4), d.IdlePaused);
        Assert.AreEqual(TimeSpan.FromHours(4), d.IdleNeedsAttention);
        Assert.AreEqual(TimeSpan.FromMinutes(10), d.IdleCompleted);
    }

    [TestMethod]
    public void Default_WatchdogModeIsSpawn_Infra19CodeDefault()
    {
        Assert.AreEqual(WatchdogMode.Spawn, Settings.Default.WatchdogMode);
    }

    [TestMethod]
    public void Default_RecycleBinTtlIsOneDay()
    {
        Assert.AreEqual(TimeSpan.FromDays(1), Settings.Default.RecycleBinTtl);
    }

    [TestMethod]
    public void Default_LogRotationIsSaneSizeAndAge()
    {
        Assert.AreEqual(1L * 1024 * 1024, Settings.Default.LogMaxBytes);
        Assert.AreEqual(TimeSpan.FromDays(14), Settings.Default.LogMaxAge);
    }

    [TestMethod]
    public void Default_IsAStableSingleton_NotRebuiltPerAccess()
    {
        // Settings.Default is a `static Settings Default { get; }` -- confirm it's
        // computed once, not a factory that could drift between two reads.
        Assert.AreSame(Settings.Default, Settings.Default);
    }
}
