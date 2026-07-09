using System.Runtime.CompilerServices;

namespace Atv.LogicTests.Architecture;

/// <summary>
/// Enforces plan/README.md standing invariant #7: "Exactly one type imports
/// <c>Windows.UI.Shell.Tasks</c>" -- <c>AppTaskStore</c>. Everything else must
/// be plain code testable against <c>FakeAppTaskStore</c>. This is a textual
/// grep over the checked-in source tree, not a reflection/metadata check, so
/// it also catches a bare <c>using</c> that never resolves (e.g. a typo) --
/// anything that leaves the literal namespace string in a `.cs` file other
/// than <c>AppTaskStore.cs</c> trips it.
/// </summary>
[TestClass]
public sealed class SeamPurityTests
{
    private const string SeamNamespace = "Windows.UI.Shell.Tasks";
    private const string SoleImporterFileName = "AppTaskStore.cs";

    [TestMethod]
    public void OnlyAppTaskStore_ImportsWindowsUiShellTasks()
    {
        string srcDir = Path.Combine(RepoRoot(), "src", "Atv");
        Assert.IsTrue(Directory.Exists(srcDir), $"Expected to find src/Atv under the repo root; looked at: {srcDir}");

        var offenders = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            // Only the checked-in source tree is subject to the invariant --
            // bin/obj hold build output, including the CsWinRT-generated
            // projection glue itself (obj/**/Generated Files/CsWinRT/*.cs),
            // which legitimately mentions the namespace it projects and isn't
            // hand-authored application code.
            .Where(path => !IsUnderBuildOutputDirectory(srcDir, path))
            .Where(path => !string.Equals(Path.GetFileName(path), SoleImporterFileName, StringComparison.Ordinal))
            .Where(ReferencesSeamNamespaceOutsideComments)
            .Select(path => Path.GetRelativePath(srcDir, path))
            .ToArray();

        Assert.IsEmpty(offenders,
            $"Only {SoleImporterFileName} may reference {SeamNamespace} (plan/README.md standing invariant #7). " +
            $"Offending file(s): {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void TheGrepItself_IsNotVacuous_AppTaskStoreDoesReferenceTheSeamNamespace()
    {
        // Guards against the check above silently passing because it's
        // pointed at the wrong directory, or because AppTaskStore.cs was
        // renamed/moved without updating SoleImporterFileName.
        string path = Path.Combine(RepoRoot(), "src", "Atv", "Store", SoleImporterFileName);
        Assert.IsTrue(File.Exists(path), $"Expected to find {path}");
        Assert.IsTrue(File.ReadAllText(path).Contains(SeamNamespace, StringComparison.Ordinal),
            $"{SoleImporterFileName} no longer references {SeamNamespace} -- is it still the real adapter?");
    }

    /// <summary>
    /// The invariant is about actual imports/usage (a <c>using</c> directive,
    /// a type alias, or a fully-qualified reference in code) -- not about
    /// prose that cross-references the namespace name in a comment for
    /// documentation purposes, which several files legitimately do (e.g. "DTO
    /// mirror of <c>Windows.UI.Shell.Tasks.AppTaskState</c>" in doc comments,
    /// or plain <c>//</c> remarks explaining why a file does NOT import it).
    /// A real <c>using</c>/type-alias/fully-qualified reference is code, never
    /// itself the start of a line comment -- so skipping any line whose
    /// trimmed content starts with <c>//</c> (covers both <c>//</c> and
    /// <c>///</c>) is sufficient to distinguish the two without a full C#
    /// parser.
    /// </summary>
    private static bool ReferencesSeamNamespaceOutsideComments(string path)
        => File.ReadLines(path)
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Any(line => line.Contains(SeamNamespace, StringComparison.Ordinal));

    private static bool IsUnderBuildOutputDirectory(string srcDir, string path)
    {
        string relative = Path.GetRelativePath(srcDir, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin", StringComparer.Ordinal) || segments.Contains("obj", StringComparer.Ordinal);
    }

    /// <summary>
    /// Walks up from this source file's own on-disk path (captured at compile
    /// time via <see cref="CallerFilePathAttribute"/>, so it works regardless
    /// of the test binary's output/working directory) until it finds the
    /// checked-in solution file, which marks the repo root.
    /// </summary>
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
