using System.Reflection;

namespace HostEventRecorder.Tests;

/// <summary>
/// Structural proof of INFRA-24's separation requirement, verified BOTH
/// ways: this test asserts the recorder assembly references nothing
/// atv-flavored; a repo-wide source grep (run manually as part of Gate A,
/// not re-implemented here as a test) proves nothing under src/ references
/// the recorder back. The primary proof is the absence of any such
/// reference in the csproj/source to begin with -- this test exists as a
/// regression guard against that ever silently changing.
/// </summary>
[TestClass]
public sealed class SeparationTests
{
    [TestMethod]
    public void RecorderAssembly_ReferencesNoAtvOrWinRTAssembly()
    {
        Assembly recorderAssembly = typeof(Constants).Assembly;
        AssemblyName[] referenced = recorderAssembly.GetReferencedAssemblies();

        var offenders = referenced
            .Where(a => (a.Name ?? "").Contains("Atv", StringComparison.OrdinalIgnoreCase)
                     || (a.Name ?? "").Contains("CsWinRT", StringComparison.OrdinalIgnoreCase)
                     || (a.Name ?? "").Contains("Windows.UI", StringComparison.OrdinalIgnoreCase)
                     || (a.Name ?? "").Contains("WinRT", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.FullName)
            .ToArray();

        Assert.IsEmpty(offenders, $"the recorder assembly must reference no Atv/CsWinRT/WinRT/Windows.UI assembly: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void RecorderAssembly_TargetsPlainNet10_NoWindowsTfmSuffix()
    {
        // The SDK only emits [SupportedOSPlatform]/[TargetPlatform] assembly
        // attributes for an OS-versioned TFM (e.g. Atv.LogicTests's
        // net10.0-windows10.0.26100.0 -- confirmed by inspecting its
        // generated AssemblyInfo.cs). Plain net10.0 emits neither -- their
        // absence here is the structural proof this project has no Windows
        // TFM suffix (INFRA-24 separation).
        Assert.IsNull(typeof(Constants).Assembly.GetCustomAttribute<System.Runtime.Versioning.SupportedOSPlatformAttribute>());
        Assert.IsNull(typeof(Constants).Assembly.GetCustomAttribute<System.Runtime.Versioning.TargetPlatformAttribute>());
    }
}
