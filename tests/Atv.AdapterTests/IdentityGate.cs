using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.AdapterTests;

/// <summary>
/// Assert-or-skip fixture (INFRA-16, "Test-time identity provisioning and deep
/// isolation"; INFRA-11, "Test strategy for machines where the API is unavailable").
///
/// This type NEVER registers or relaunches anything -- by the time any test method
/// runs, package identity has already been granted or not, entirely as a side effect
/// of HOW this process got launched (see build/Atv.TestIdentity.targets' file header
/// for the hard-link/AppExecutionAlias mechanism that makes `dotnet test` carry
/// identity). All this type does is read that already-decided state and either let
/// the test proceed or mark it Inconclusive (MSTest's runtime-conditional-skip
/// mechanism; MTP/`dotnet test` treats Inconclusive as a non-failing outcome, distinct
/// from Failed, so a skip here never turns the suite red).
/// </summary>
internal static class IdentityGate
{
    /// <summary>
    /// Set to "1" to force <see cref="AssertApiSupportedOrSkip"/> to behave as if
    /// <c>AppTaskInfo.IsSupported()</c> returned <see langword="false"/>, regardless
    /// of this machine's real answer -- INFRA-11 acceptance criterion 6 ("on an
    /// API-absent machine (or simulated), the suite reports skipped, exit success")
    /// needs to be exercisable on a machine where the API genuinely IS present.
    /// </summary>
    internal const string SimulateApiAbsentEnvVar = "ATV_TEST_SIMULATE_API_ABSENT";

    /// <summary>
    /// Call from <c>[TestInitialize]</c>, before touching any real
    /// <see cref="IAppTaskStore"/> member. Marks the current test Inconclusive (never
    /// Failed) when this process has no package identity -- the documented outcome for
    /// VS Test Explorer launches and any direct exe launch that bypassed
    /// build/Atv.TestIdentity.targets' `_TestRunStart` hook (e.g. a fresh worktree's
    /// very first run, before `dotnet test` has ever registered it).
    /// </summary>
    public static void AssertIdentityOrSkip()
    {
        if (TryGetCurrentPackageFullName() is not null)
            return;

        Assert.Inconclusive(
            "No package identity -- register first: run `dotnet test` once (the " +
            "_TestRunStart hook in build/Atv.TestIdentity.targets registers this " +
            "worktree's test identity automatically), or run the explicit " +
            "AtvTestRegister MSBuild target, then re-run.");
    }

    /// <summary>
    /// Call from <c>[TestInitialize]</c>, after <see cref="AssertIdentityOrSkip"/> and
    /// after constructing the real <see cref="IAppTaskStore"/>, before any other
    /// member. Marks the current test Inconclusive when the platform itself reports
    /// the API unsupported (INFRA-13: <c>IsSupported()</c> can throw/return false on
    /// otherwise-correct builds during the API's gradual rollout) -- or when
    /// simulated absent via <see cref="SimulateApiAbsentEnvVar"/>.
    /// </summary>
    public static void AssertApiSupportedOrSkip(IAppTaskStore store)
    {
        if (SimulatingApiAbsent)
        {
            Assert.Inconclusive(
                $"AppTaskInfo treated as unsupported: {SimulateApiAbsentEnvVar}=1 is set " +
                "(INFRA-11 acceptance criterion 6 simulation knob).");
        }

        if (!store.IsSupported())
        {
            Assert.Inconclusive(
                "AppTaskInfo.IsSupported() returned false on this machine/build " +
                "(INFRA-13: the API's activation registration rolls out gradually) -- " +
                "real-adapter suite skipped, not failed (INFRA-11).");
        }
    }

    internal static bool SimulatingApiAbsent
        => Environment.GetEnvironmentVariable(SimulateApiAbsentEnvVar) == "1";

    private static string? TryGetCurrentPackageFullName()
    {
        try { return Windows.ApplicationModel.Package.Current.Id.FullName; }
        catch { return null; }
    }
}
