using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Diagnostics;

namespace Codevoid.AgentTaskVoid.LogicTests.Diagnostics;

/// <summary>
/// AC3's per-check coverage for <see cref="DoctorChecks"/>, driven entirely
/// by injected <see cref="DoctorProbes"/> -- no Dispatcher, no Posture, no
/// real OS/registry/mutex access. Every combination of identity
/// absent/present, API absent/present, and Developer Mode on/off the phase
/// file calls for, plus the not-installed winget-remedy path, `ToVerbResult`'s
/// worst-finding mapping, and that every check always runs (never
/// short-circuits on an earlier failure).
/// </summary>
[TestClass]
public sealed class DoctorChecksTests
{
    private static DoctorContext Context(
        string? packageFullName = "Codevoid.AgentTaskVoid-x_1.0.0.0_neutral",
        bool apiSupported = true,
        bool devMode = true,
        bool watchdogRunning = false,
        string configPath = @"C:\fake\atv-config.json",
        string appDataFolder = @"C:\fake",
        string sidecarDir = @"C:\fake\sidecar",
        string logPath = @"C:\fake\atv.log",
        string? packageName = null,
        Func<RepoDiscoveryResult>? discoverRepo = null)
    {
        var probes = new DoctorProbes(
            PackageFullName: () => packageFullName,
            ApiSupported: () => apiSupported,
            DeveloperModeEnabled: () => devMode,
            WatchdogRunning: () => watchdogRunning,
            PackageName: () => packageName);
        return new DoctorContext(probes, configPath, appDataFolder, sidecarDir, logPath, DiscoverRepo: discoverRepo);
    }

    [TestMethod]
    public void Run_IdentityPresent_ReportsPresentWithPfn()
    {
        var report = DoctorChecks.Run(Context(packageFullName: "Codevoid.AgentTaskVoid-abc123_1.2.3.4_x64"));

        Assert.IsTrue(report.IdentityPresent);
        Assert.AreEqual("Codevoid.AgentTaskVoid-abc123_1.2.3.4_x64", report.PackageFullName);
        Assert.IsNull(report.Remedy, "identity present -- no winget remedy needed.");
    }

    [TestMethod]
    public void Run_IdentityAbsent_ReportsAbsent_AndEmitsWingetRemedyWithTheFinalizedId()
    {
        var report = DoctorChecks.Run(Context(packageFullName: null));

        Assert.IsFalse(report.IdentityPresent);
        Assert.IsNull(report.PackageFullName);
        Assert.IsNotNull(report.Remedy);
        StringAssert.Contains(report.Remedy!, "winget install");
        StringAssert.Contains(report.Remedy!, DoctorChecks.WingetPackageId);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Run_ApiSupportedProbe_PassesThroughVerbatim(bool apiSupported)
    {
        var report = DoctorChecks.Run(Context(apiSupported: apiSupported));
        Assert.AreEqual(apiSupported, report.ApiSupported);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Run_DeveloperModeProbe_PassesThroughVerbatim(bool devMode)
    {
        var report = DoctorChecks.Run(Context(devMode: devMode));
        Assert.AreEqual(devMode, report.DeveloperModeEnabled);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Run_WatchdogRunningProbe_PassesThroughVerbatim(bool watchdogRunning)
    {
        var report = DoctorChecks.Run(Context(watchdogRunning: watchdogRunning));
        Assert.AreEqual(watchdogRunning, report.WatchdogRunning);
    }

    [TestMethod]
    public void Run_EveryProbeInvokedExactlyOnce_NeverShortCircuits()
    {
        int identityCalls = 0, apiCalls = 0, devModeCalls = 0, watchdogCalls = 0;
        var probes = new DoctorProbes(
            PackageFullName: () => { identityCalls++; return null; }, // absent -- would short-circuit a naive implementation
            ApiSupported: () => { apiCalls++; return false; },
            DeveloperModeEnabled: () => { devModeCalls++; return false; },
            WatchdogRunning: () => { watchdogCalls++; return false; });
        var context = new DoctorContext(probes, "cfg", "data", "sidecar", "log");

        DoctorChecks.Run(context);

        Assert.AreEqual(1, identityCalls);
        Assert.AreEqual(1, apiCalls, "API probe must still run even though identity is absent -- doctor never short-circuits.");
        Assert.AreEqual(1, devModeCalls);
        Assert.AreEqual(1, watchdogCalls);
    }

    [TestMethod]
    public void Run_PathsPassThroughVerbatim()
    {
        var report = DoctorChecks.Run(Context(
            configPath: @"X:\cfg.json", appDataFolder: @"X:\data", sidecarDir: @"X:\data\sidecar", logPath: @"X:\data\atv.log"));

        Assert.AreEqual(@"X:\cfg.json", report.ConfigPath);
        Assert.AreEqual(@"X:\data", report.AppDataFolder);
        Assert.AreEqual(@"X:\data\sidecar", report.SidecarDir);
        Assert.AreEqual(@"X:\data\atv.log", report.LogPath);
    }

    // ---- DIST-3 (2026-07-10 amendment): build-kind marker -------------------------

    [TestMethod]
    public void Run_PackageNameIsBrand_BuildKindMarkerIsNull_Release()
    {
        var report = DoctorChecks.Run(Context(packageName: Codevoid.AgentTaskVoid.Branding.IdentityName));
        Assert.IsNull(report.BuildKindMarker, "Release (clean brand-only Name) must be unmarked ship output.");
    }

    [TestMethod]
    public void Run_PackageNameIsBrandPlusHash_BuildKindMarkerIsDev()
    {
        var report = DoctorChecks.Run(Context(packageName: $"{Codevoid.AgentTaskVoid.Branding.IdentityName}-bbbb1168"));
        Assert.AreEqual("(dev)", report.BuildKindMarker);
    }

    [TestMethod]
    public void Run_PackageNameIsTestPool_BuildKindMarkerIsTest()
    {
        var report = DoctorChecks.Run(Context(packageName: $"{Codevoid.AgentTaskVoid.Branding.IdentityName}.Test.abcd1234"));
        Assert.AreEqual("(test)", report.BuildKindMarker);
    }

    [TestMethod]
    public void Run_NoPackageNameSupplied_BuildKindMarkerIsNull_NoIdentityDocumented()
    {
        var report = DoctorChecks.Run(Context(packageName: null));
        Assert.IsNull(report.BuildKindMarker);
    }

    [TestMethod]
    public void Run_PackageNameProbeOmittedEntirely_DoesNotThrow_DefaultsToNull()
    {
        // A caller predating the DIST-3 amendment supplies no PackageName probe at
        // all (the record default) -- Run must not NullReferenceException on it.
        var probes = new DoctorProbes(
            PackageFullName: () => "Codevoid.AgentTaskVoid-x_1.0.0.0_neutral",
            ApiSupported: () => true,
            DeveloperModeEnabled: () => true,
            WatchdogRunning: () => false);
        var context = new DoctorContext(probes, "cfg", "data", "sidecar", "log");

        var report = DoctorChecks.Run(context);

        Assert.IsNull(report.BuildKindMarker);
    }

    [TestMethod]
    public void ToVerbResult_IdentityAbsent_MapsToIdentityNotRegistered()
    {
        var report = DoctorChecks.Run(Context(packageFullName: null, apiSupported: true));
        var result = DoctorChecks.ToVerbResult(report);

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(FailureKind.IdentityNotRegistered, result.Kind);
    }

    [TestMethod]
    public void ToVerbResult_IdentityPresent_ApiAbsent_MapsToApiUnavailable()
    {
        var report = DoctorChecks.Run(Context(packageFullName: "pfn", apiSupported: false));
        var result = DoctorChecks.ToVerbResult(report);

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(FailureKind.ApiUnavailable, result.Kind);
    }

    [TestMethod]
    public void ToVerbResult_IdentityAndApiBothGood_IsSuccess_RegardlessOfDevModeOrWatchdog()
    {
        var report = DoctorChecks.Run(Context(packageFullName: "pfn", apiSupported: true, devMode: false, watchdogRunning: false));
        var result = DoctorChecks.ToVerbResult(report);

        Assert.IsTrue(result.Ok, "Developer Mode/watchdog are informational only -- they must never make doctor's overall verdict a failure.");
    }

    // ---- ERGO-30 (phase 17) AC7: repo-defaults discovery surfacing ----------------

    [TestMethod]
    public void Run_NoDiscoverRepoWired_RepoFieldsAreNull()
    {
        var report = DoctorChecks.Run(Context(discoverRepo: null));
        Assert.IsNull(report.RepoAnchorPath);
        Assert.IsNull(report.RepoAnchorSource);
        Assert.IsNull(report.RepoConfigPath);
        Assert.IsNull(report.RepoConfigParseStatus);
    }

    [TestMethod]
    public void Run_RepoConfigFound_SurfacesAnchorSourceAndOkStatus()
    {
        var discovery = new RepoDiscoveryResult(
            AnchorPath: @"C:\repo", AnchorSource: AnchorSource.CwdFlag, ConfigPath: @"C:\repo\.atv.json",
            SearchedUpTo: @"C:\repo", ParseStatus: RepoConfigParseStatus.Ok,
            AllowedValues: new Dictionary<string, string>(), DisallowedKeys: [], RepoRootDir: @"C:\repo", RepoName: "repo", Branch: "main");

        var report = DoctorChecks.Run(Context(discoverRepo: () => discovery));

        Assert.AreEqual(@"C:\repo", report.RepoAnchorPath);
        Assert.AreEqual("--cwd", report.RepoAnchorSource);
        Assert.AreEqual(@"C:\repo\.atv.json", report.RepoConfigPath);
        Assert.AreEqual("ok", report.RepoConfigParseStatus);
    }

    [TestMethod]
    public void Run_RepoConfigNotFound_SurfacesNullPathAndSearchedUpTo_ProcessCwdSource()
    {
        var discovery = new RepoDiscoveryResult(
            AnchorPath: @"C:\somewhere", AnchorSource: AnchorSource.ProcessCwd, ConfigPath: null,
            SearchedUpTo: @"C:\", ParseStatus: RepoConfigParseStatus.NotFound,
            AllowedValues: new Dictionary<string, string>(), DisallowedKeys: [], RepoRootDir: null, RepoName: null, Branch: null);

        var report = DoctorChecks.Run(Context(discoverRepo: () => discovery));

        Assert.AreEqual("process cwd", report.RepoAnchorSource);
        Assert.IsNull(report.RepoConfigPath);
        Assert.AreEqual("not-found", report.RepoConfigParseStatus);
        Assert.AreEqual(@"C:\", report.RepoSearchedUpTo);
    }

    [TestMethod]
    public void Run_RepoConfigMalformed_IsAOneLookDiagnosis()
    {
        var discovery = new RepoDiscoveryResult(
            AnchorPath: @"C:\repo", AnchorSource: AnchorSource.CwdFlag, ConfigPath: @"C:\repo\.atv.json",
            SearchedUpTo: @"C:\repo", ParseStatus: RepoConfigParseStatus.Malformed,
            AllowedValues: new Dictionary<string, string>(), DisallowedKeys: [], RepoRootDir: @"C:\repo", RepoName: "repo", Branch: null);

        var report = DoctorChecks.Run(Context(discoverRepo: () => discovery));

        Assert.AreEqual("malformed", report.RepoConfigParseStatus, "a deliberately malformed repo file must be a one-look diagnosis in doctor's report.");
    }

    [TestMethod]
    public void FormatHuman_MalformedRepoConfig_MentionsItInOneLine()
    {
        var discovery = new RepoDiscoveryResult(
            AnchorPath: @"C:\repo", AnchorSource: AnchorSource.CwdFlag, ConfigPath: @"C:\repo\.atv.json",
            SearchedUpTo: @"C:\repo", ParseStatus: RepoConfigParseStatus.Malformed,
            AllowedValues: new Dictionary<string, string>(), DisallowedKeys: [], RepoRootDir: @"C:\repo", RepoName: "repo", Branch: null);
        var report = DoctorChecks.Run(Context(discoverRepo: () => discovery));

        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: false);
        var log = new FailureLog(Path.Combine(Path.GetTempPath(), $"atv-doctor-test-{Guid.NewGuid():N}.log"), maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));
        var posture = new Posture(log, output, strict: false, verbose: false);

        Codevoid.AgentTaskVoid.Cli.Verbs.DoctorVerb.Run(output, posture, Context(discoverRepo: () => discovery), DateTimeOffset.Now);

        string text = stdout.ToString();
        StringAssert.Contains(text, "repo config:");
        StringAssert.Contains(text, "malformed");
    }
}
