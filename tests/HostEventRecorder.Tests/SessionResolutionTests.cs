namespace HostEventRecorder.Tests;

[TestClass]
public sealed class SessionResolutionTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 12, 18, 3, 44, TimeSpan.Zero);

    [TestMethod]
    public void NoArgvNoEnv_FallsBackToDatedAdhocId()
    {
        string id = SessionResolution.ResolveSessionId(null, null, FixedNow);
        Assert.AreEqual("adhoc-2026-07-12", id);
    }

    [TestMethod]
    public void EnvOnly_EnvWins()
    {
        string id = SessionResolution.ResolveSessionId(null, "driver-minted-session", FixedNow);
        Assert.AreEqual("driver-minted-session", id);
    }

    [TestMethod]
    public void ArgvOnly_ArgvWins()
    {
        string id = SessionResolution.ResolveSessionId("manual-session", null, FixedNow);
        Assert.AreEqual("manual-session", id);
    }

    [TestMethod]
    public void ArgvAndEnvBothSet_ArgvTakesPrecedence()
    {
        string id = SessionResolution.ResolveSessionId("manual-session", "driver-minted-session", FixedNow);
        Assert.AreEqual("manual-session", id);
    }

    [TestMethod]
    public void AdhocFallback_SameUtcDay_IsDeterministic_AcrossDifferentTimesOfDay()
    {
        string morning = SessionResolution.ResolveSessionId(null, null, new DateTimeOffset(2026, 7, 12, 1, 0, 0, TimeSpan.Zero));
        string evening = SessionResolution.ResolveSessionId(null, null, new DateTimeOffset(2026, 7, 12, 23, 59, 0, TimeSpan.Zero));

        Assert.AreEqual(morning, evening, "manual captures on the same UTC day must land in the same session file.");
    }

    [TestMethod]
    public void AdhocFallback_DifferentUtcDay_Differs()
    {
        string day1 = SessionResolution.ResolveSessionId(null, null, new DateTimeOffset(2026, 7, 12, 23, 59, 0, TimeSpan.Zero));
        string day2 = SessionResolution.ResolveSessionId(null, null, new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero));

        Assert.AreNotEqual(day1, day2);
    }

    [TestMethod]
    public void EmptyStringOverrides_TreatedAsAbsent()
    {
        string id = SessionResolution.ResolveSessionId("", "", FixedNow);
        Assert.AreEqual("adhoc-2026-07-12", id);
    }
}
