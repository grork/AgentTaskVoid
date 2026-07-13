namespace HostEventRecorder.Tests;

/// <summary>
/// Locates the sibling HostEventRecorder project's build output from this
/// test project's own runtime location, without any MSBuild-time wiring.
/// Both projects share the exact <c>&lt;proj&gt;/bin/&lt;Config&gt;/net10.0/</c>
/// output shape (same TFM, no RID), so this walks up from
/// <see cref="AppContext.BaseDirectory"/> to the repo root and back down
/// into the recorder project's own bin folder under the SAME configuration
/// this test binary was built with.
/// </summary>
internal static class RecorderExeLocator
{
    public static string FindBuiltExePath()
    {
        // AppContext.BaseDirectory: ...\tests\HostEventRecorder.Tests\bin\<Config>\net10.0\
        DirectoryInfo net10Dir = new(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        DirectoryInfo? configDir = net10Dir.Parent;   // ...\bin\<Config>
        DirectoryInfo? binDir = configDir?.Parent;    // ...\bin
        DirectoryInfo? testProjDir = binDir?.Parent;  // ...\HostEventRecorder.Tests
        DirectoryInfo? testsDir = testProjDir?.Parent;    // ...\tests
        DirectoryInfo? repoRoot = testsDir?.Parent;       // repo root

        if (configDir is null || repoRoot is null)
            throw new InvalidOperationException($"Could not walk up from AppContext.BaseDirectory ('{AppContext.BaseDirectory}') to the repo root.");

        string config = configDir.Name; // "Debug" or "Release" -- whatever this test binary itself was built as
        string exePath = Path.Combine(repoRoot.FullName, "tools", "host-event-recorder", "bin", config, "net10.0", "host-event-recorder.exe");

        if (!File.Exists(exePath))
        {
            throw new InvalidOperationException(
                $"Expected the HostEventRecorder project to already be built at '{exePath}' (build the solution before running this test suite).");
        }

        return exePath;
    }
}
