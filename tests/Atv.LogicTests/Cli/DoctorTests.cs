using System.Text.Json;
using Atv.Config;
using Atv.Diagnostics;

namespace Atv.LogicTests.Cli;

/// <summary>
/// AC3's `doctor` integration coverage (Dispatcher-level, via
/// <see cref="DispatcherHarness"/>'s injected probes): always runs to
/// completion regardless of platform state (never Capability-gated), the
/// not-installed path emits the winget remedy, `--json` shape is stable and
/// distinct from the generic mutating-verb shape, printed paths match what
/// the harness supplied, `--strict` exit-vocabulary mapping (identity/API
/// only -- Developer Mode/watchdog never fail it), and it is NOT a
/// write-path verb (no watchdog-ensure). Pure per-probe unit coverage of
/// <see cref="DoctorChecks"/> itself lives in
/// <c>tests/Atv.LogicTests/Diagnostics/DoctorChecksTests.cs</c>.
/// </summary>
[TestClass]
public sealed class DoctorTests
{
    [TestMethod]
    public void Doctor_AllGood_ExitsZero_HumanOutputMentionsIdentityAndApi()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "doctor");

        Assert.AreEqual(0, exit);
        string output = h.Stdout.ToString();
        StringAssert.Contains(output, "identity: present");
        StringAssert.Contains(output, h.DoctorPackageFullName!);
        StringAssert.Contains(output, "api:");
    }

    [TestMethod]
    public void Doctor_NoIdentity_StillRunsToCompletion_PrintsWingetRemedy()
    {
        using var h = new DispatcherHarness { DoctorPackageFullName = null };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "doctor");

        // Non-strict: doctor is diagnostic, always exits 0.
        Assert.AreEqual(0, exit);
        string output = h.Stdout.ToString();
        StringAssert.Contains(output, "identity: NOT present");
        StringAssert.Contains(output, "winget install");
        StringAssert.Contains(output, DoctorChecks.WingetPackageIdPlaceholder);
        // Every other check still ran and printed -- doctor never short-circuits.
        StringAssert.Contains(output, "api:");
        StringAssert.Contains(output, "developer mode");
        StringAssert.Contains(output, "watchdog:");
        StringAssert.Contains(output, "config file:");
    }

    [TestMethod]
    public void Doctor_IdentityPresent_NoRemedyPrinted()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher();

        h.Run(dispatcher, "doctor");

        Assert.IsFalse(h.Stdout.ToString().Contains("winget install", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Doctor_Json_ShapeIsStable_AndPathsMatchTheHarness()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(json: true);

        h.Run(dispatcher, "doctor");

        using var doc = JsonDocument.Parse(h.Stdout.ToString());
        var root = doc.RootElement;
        Assert.IsTrue(root.GetProperty("identityPresent").GetBoolean());
        Assert.AreEqual(h.DoctorPackageFullName, root.GetProperty("packageFullName").GetString());
        Assert.IsTrue(root.TryGetProperty("apiSupported", out _));
        Assert.IsTrue(root.TryGetProperty("developerModeEnabled", out _));
        Assert.IsTrue(root.TryGetProperty("watchdogRunning", out _));
        Assert.AreEqual(Path.Combine(h.AppDataRoot, "atv-config.json"), root.GetProperty("configPath").GetString());
        Assert.AreEqual(h.AppDataRoot, root.GetProperty("appDataFolder").GetString());
        Assert.AreEqual(Path.Combine(h.AppDataRoot, "atv.log"), root.GetProperty("logPath").GetString());
    }

    [TestMethod]
    public void Doctor_Json_DoesNotEmitTheGenericMutatingResultShape()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(json: true);

        h.Run(dispatcher, "doctor");

        // doctor --json's shape is its own report (ERGO-27 C5), never the
        // generic {"ok":..,"reason":..} mutating-verb shape.
        Assert.IsFalse(h.Stdout.ToString().Contains("\"reason\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Doctor_NoIdentity_Strict_ReturnsIdentityNotRegisteredExitCode()
    {
        using var h = new DispatcherHarness { DoctorPackageFullName = null };
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "doctor");

        Assert.AreEqual((int)FailureKind.IdentityNotRegistered, exit);
    }

    [TestMethod]
    public void Doctor_ApiUnsupported_Strict_ReturnsApiUnavailableExitCode()
    {
        using var h = new DispatcherHarness();
        h.Store.Supported = false;
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "doctor");

        Assert.AreEqual((int)FailureKind.ApiUnavailable, exit);
    }

    [TestMethod]
    public void Doctor_DeveloperModeOff_NeverFails_EvenStrict()
    {
        using var h = new DispatcherHarness { DoctorDeveloperModeEnabled = false };
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "doctor");

        Assert.AreEqual(0, exit, "Developer Mode is dev-facing/informational only -- it must never fail doctor, even under --strict.");
    }

    [TestMethod]
    public void Doctor_WatchdogNotRunning_NeverFails_EvenStrict()
    {
        using var h = new DispatcherHarness { DoctorWatchdogRunning = false };
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "doctor");

        Assert.AreEqual(0, exit, "watchdog liveness is informational only -- it must never fail doctor, even under --strict.");
    }

    [TestMethod]
    public void Doctor_DoesNotRequireIdentity_UnlikeLifecycleVerbs()
    {
        // Whereas `start` etc. are Capability-gated and refuse without
        // identity, `doctor`'s entire job is diagnosing exactly that state --
        // it must run its own checks regardless.
        using var h = new DispatcherHarness { HasIdentity = false, DoctorPackageFullName = null };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "doctor");

        Assert.AreEqual(0, exit);
        StringAssert.Contains(h.Stdout.ToString(), "identity: NOT present");
    }

    [TestMethod]
    public void Doctor_NeverEnsuresWatchdog_NotAWritePathVerb()
    {
        using var h = new DispatcherHarness();
        var dispatcher = h.BuildDispatcher(watchdogMode: WatchdogMode.Spawn);

        h.Run(dispatcher, "doctor");

        Assert.AreEqual(0, h.ProcessHost.StartCallCount, "doctor is diagnostic -- it must never trigger the LIFE-17 watchdog-ensure gate.");
    }
}
