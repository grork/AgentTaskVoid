using Atv.Config;
using Atv.LogicTests.Persistence;

namespace Atv.LogicTests.Config;

/// <summary>
/// ERGO-30 (phase 17) AC1/AC2/AC6: the <c>.atv.json</c> discovery walk
/// (nearest-wins, <c>.git</c>-boundary/filesystem-root stop, malformed/
/// too-large degradation, allowlist filtering), the anchor resolution
/// (<c>--cwd</c> beats process cwd, structurally free of host env reads),
/// and the cheap <c>.git/HEAD</c> branch read + title-template token
/// expansion.
/// </summary>
[TestClass]
public sealed class RepoSettingsTests
{
    // ---- AC1: discovery -----------------------------------------------------

    [TestMethod]
    public void Discover_NearestAtvJson_WinsOverAncestor()
    {
        using var root = new TempDirectory();
        string child = Path.Combine(root.Path, "child");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(root.Path, RepoSettings.FileName), """{"subtitle":"root"}""");
        File.WriteAllText(Path.Combine(child, RepoSettings.FileName), """{"subtitle":"child"}""");

        var result = RepoSettings.Discover(cwdFlag: child, processCwd: "unused");

        Assert.AreEqual(Path.Combine(child, RepoSettings.FileName), result.ConfigPath);
        Assert.AreEqual("child", result.AllowedValues["subtitle"]);
    }

    [TestMethod]
    public void Discover_NoFileAnywhere_CleanNoOp()
    {
        using var root = new TempDirectory();
        string child = Path.Combine(root.Path, "child");
        Directory.CreateDirectory(child);
        Directory.CreateDirectory(Path.Combine(root.Path, ".git")); // bound the walk so it doesn't escalate to the real filesystem root.

        var result = RepoSettings.Discover(cwdFlag: child, processCwd: "unused");

        Assert.IsNull(result.ConfigPath);
        Assert.AreEqual(RepoConfigParseStatus.NotFound, result.ParseStatus);
        Assert.IsEmpty(result.AllowedValues);
    }

    [TestMethod]
    public void Discover_WalkStopsAtGitBoundary_AncestorAtvJsonNeverSeen()
    {
        using var root = new TempDirectory();
        string repo = Path.Combine(root.Path, "repo");
        string sub = Path.Combine(repo, "sub");
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        // ABOVE the .git boundary -- must never be reached by the walk.
        File.WriteAllText(Path.Combine(root.Path, RepoSettings.FileName), """{"subtitle":"above-git"}""");

        var result = RepoSettings.Discover(cwdFlag: sub, processCwd: "unused");

        Assert.IsNull(result.ConfigPath, "the walk must stop AT the .git boundary, never search above it.");
        Assert.AreEqual(repo, result.RepoRootDir);
        Assert.AreEqual(repo, result.SearchedUpTo);
    }

    [TestMethod]
    public void Discover_AtvJsonAtTheGitBoundaryItself_IsStillFound()
    {
        // The .git-containing directory is the LAST one checked, INCLUSIVE.
        using var root = new TempDirectory();
        string repo = Path.Combine(root.Path, "repo");
        Directory.CreateDirectory(repo);
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(Path.Combine(repo, RepoSettings.FileName), """{"subtitle":"at-boundary"}""");

        var result = RepoSettings.Discover(cwdFlag: repo, processCwd: "unused");

        Assert.AreEqual(Path.Combine(repo, RepoSettings.FileName), result.ConfigPath);
    }

    [TestMethod]
    public void Discover_MalformedJson_ReportsMalformed_EmptyValues()
    {
        using var root = new TempDirectory();
        Directory.CreateDirectory(root.Path);
        File.WriteAllText(Path.Combine(root.Path, RepoSettings.FileName), "{not valid json");

        var result = RepoSettings.Discover(cwdFlag: root.Path, processCwd: "unused");

        Assert.AreEqual(RepoConfigParseStatus.Malformed, result.ParseStatus);
        Assert.IsEmpty(result.AllowedValues);
    }

    [TestMethod]
    public void Discover_NestedJsonObject_IsMalformed_NotACrash()
    {
        // Depth cap "for free": a flat string->string map can't hold a nested
        // object, so this fails deserialization rather than materializing it.
        using var root = new TempDirectory();
        Directory.CreateDirectory(root.Path);
        File.WriteAllText(Path.Combine(root.Path, RepoSettings.FileName), """{"subtitle":{"nested":"oops"}}""");

        var result = RepoSettings.Discover(cwdFlag: root.Path, processCwd: "unused");

        Assert.AreEqual(RepoConfigParseStatus.Malformed, result.ParseStatus);
    }

    [TestMethod]
    public void Discover_OversizedFile_ReportsTooLarge_NeverReadIntoMemory()
    {
        using var root = new TempDirectory();
        Directory.CreateDirectory(root.Path);
        string huge = new('x', (int)RepoSettings.MaxFileBytes + 1);
        File.WriteAllText(Path.Combine(root.Path, RepoSettings.FileName), $$"""{"subtitle":"{{huge}}"}""");

        var result = RepoSettings.Discover(cwdFlag: root.Path, processCwd: "unused");

        Assert.AreEqual(RepoConfigParseStatus.TooLarge, result.ParseStatus);
        Assert.IsEmpty(result.AllowedValues);
    }

    [TestMethod]
    public void Discover_AllowlistFilter_SeparatesAllowedFromDisallowed()
    {
        using var root = new TempDirectory();
        Directory.CreateDirectory(root.Path);
        File.WriteAllText(Path.Combine(root.Path, RepoSettings.FileName),
            """{"subtitle":"S","deep-link":"file:///evil","idle-running":"00:00:01"}""");

        var result = RepoSettings.Discover(cwdFlag: root.Path, processCwd: "unused");

        Assert.AreEqual(RepoConfigParseStatus.Ok, result.ParseStatus);
        Assert.AreEqual("S", result.AllowedValues["subtitle"]);
        Assert.IsFalse(result.AllowedValues.ContainsKey("deep-link"));
        Assert.IsFalse(result.AllowedValues.ContainsKey("idle-running"));
        CollectionAssert.AreEquivalent(new[] { "deep-link", "idle-running" }, result.DisallowedKeys.ToList());
    }

    // ---- AC2: anchor ---------------------------------------------------------

    [TestMethod]
    public void Discover_CwdFlag_BeatsProcessCwd()
    {
        using var flagDir = new TempDirectory();
        using var processDir = new TempDirectory();
        Directory.CreateDirectory(flagDir.Path);
        Directory.CreateDirectory(processDir.Path);
        File.WriteAllText(Path.Combine(flagDir.Path, RepoSettings.FileName), """{"subtitle":"flag"}""");
        File.WriteAllText(Path.Combine(processDir.Path, RepoSettings.FileName), """{"subtitle":"process"}""");

        var result = RepoSettings.Discover(cwdFlag: flagDir.Path, processCwd: processDir.Path);

        Assert.AreEqual(AnchorSource.CwdFlag, result.AnchorSource);
        Assert.AreEqual("flag", result.AllowedValues["subtitle"]);
    }

    [TestMethod]
    public void Discover_NoCwdFlag_FallsBackToProcessCwd()
    {
        using var processDir = new TempDirectory();
        Directory.CreateDirectory(processDir.Path);
        File.WriteAllText(Path.Combine(processDir.Path, RepoSettings.FileName), """{"subtitle":"process"}""");

        var result = RepoSettings.Discover(cwdFlag: null, processCwd: processDir.Path);

        Assert.AreEqual(AnchorSource.ProcessCwd, result.AnchorSource);
        Assert.AreEqual("process", result.AllowedValues["subtitle"]);
    }

    [TestMethod]
    public void Discover_EmptyCwdFlag_TreatedAsAbsent_FallsBackToProcessCwd()
    {
        var result = RepoSettings.Discover(cwdFlag: "", processCwd: @"C:\some\process\dir");
        Assert.AreEqual(AnchorSource.ProcessCwd, result.AnchorSource);
    }

    [TestMethod]
    public void Discover_MalformedAnchor_NeverThrows_DegradesToNotFound()
    {
        // A path with characters GetFullPath rejects must never crash discovery.
        var result = RepoSettings.Discover(cwdFlag: "\0bad\0path", processCwd: "unused");
        Assert.AreEqual(RepoConfigParseStatus.NotFound, result.ParseStatus);
    }

    /// <summary>
    /// AC2's STRUCTURAL proof (not merely behavioral): greps this project's
    /// actual checked-in source file for <c>GetEnvironmentVariable</c> --
    /// mirrors <c>Atv.LogicTests.Architecture.SeamPurityTests</c>'s own
    /// grep-the-real-file pattern. Passing behaviorally (no env var happens to
    /// be read in the test's own environment) would never catch a REGRESSION
    /// that adds an env-var read to the anchor path; this catches it even if
    /// nobody thinks to set that env var in a future test run.
    /// </summary>
    [TestMethod]
    public void TheAnchorResolution_IsGrepProvenFreeOfEnvironmentVariableReads()
    {
        // Mirrors Atv.LogicTests.Architecture.SeamPurityTests' own "grep the
        // real checked-in file, skip comment lines" pattern -- this is about
        // actual CODE never calling the env-var API, not about whether a doc
        // comment is permitted to name it in prose (this very test's own
        // remarks do, elsewhere in this file).
        const string forbidden = "GetEnvironmentVariable";
        string path = RepoSettingsSourcePath();
        Assert.IsTrue(File.Exists(path), $"Expected to find {path}");

        var offendingLines = File.ReadLines(path)
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(line => line.Contains(forbidden, StringComparison.Ordinal))
            .ToList();

        Assert.IsEmpty(offendingLines,
            $"RepoSettings.cs must never call {forbidden}(s) in actual code for anchor resolution (ERGO-30: atv stays host-agnostic, --cwd or process cwd only). Offending line(s): {string.Join(" | ", offendingLines)}");
    }

    private static string RepoSettingsSourcePath([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AppTaskInfoCli.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root above " + here);
        return Path.Combine(dir.FullName, "src", "Atv", "Config", "RepoSettings.cs");
    }

    // ---- AC6: {repo}/{branch} templates + branch reading ---------------------

    [TestMethod]
    public void ExpandTemplate_SubstitutesRepoAndBranch()
    {
        string result = RepoSettings.ExpandTemplate("{repo} on {branch}", "my-repo", "main");
        Assert.AreEqual("my-repo on main", result);
    }

    [TestMethod]
    public void ExpandTemplate_MissingInfo_TokenIsDropped_NotLeftLiteral()
    {
        // Build-detail choice (AC6): a missing token is DROPPED (empty string),
        // never left as a raw "{branch}" placeholder in a real title.
        string result = RepoSettings.ExpandTemplate("{repo} ({branch})", "my-repo", null);
        Assert.AreEqual("my-repo ()", result);
        Assert.IsFalse(result.Contains("{branch}", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Discover_ReadsBranch_FromRefsHeads()
    {
        using var root = new TempDirectory();
        string gitDir = Path.Combine(root.Path, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/feature-x\n");

        var result = RepoSettings.Discover(cwdFlag: root.Path, processCwd: "unused");

        Assert.AreEqual("feature-x", result.Branch);
        Assert.AreEqual(Path.GetFileName(root.Path.TrimEnd(Path.DirectorySeparatorChar)), result.RepoName);
    }

    [TestMethod]
    public void Discover_DetachedHead_ReturnsShortSha()
    {
        using var root = new TempDirectory();
        string gitDir = Path.Combine(root.Path, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "abcdef1234567890abcdef1234567890abcdef12\n");

        var result = RepoSettings.Discover(cwdFlag: root.Path, processCwd: "unused");

        Assert.AreEqual("abcdef1", result.Branch);
    }

    [TestMethod]
    public void Discover_GitAsWorktreeFile_ResolvesGitDir_ReadsBranch()
    {
        using var root = new TempDirectory();
        string realGitDir = Path.Combine(root.Path, "real-git");
        Directory.CreateDirectory(realGitDir);
        File.WriteAllText(Path.Combine(realGitDir, "HEAD"), "ref: refs/heads/worktree-branch\n");

        string repo = Path.Combine(root.Path, "worktree-checkout");
        Directory.CreateDirectory(repo);
        File.WriteAllText(Path.Combine(repo, ".git"), $"gitdir: {realGitDir}\n");

        var result = RepoSettings.Discover(cwdFlag: repo, processCwd: "unused");

        Assert.AreEqual(repo, result.RepoRootDir);
        Assert.AreEqual("worktree-branch", result.Branch);
    }

    [TestMethod]
    public void Discover_NoGitAnywhere_RepoNameAndBranchAreNull()
    {
        using var root = new TempDirectory();
        Directory.CreateDirectory(root.Path);
        File.WriteAllText(Path.Combine(root.Path, RepoSettings.FileName), """{"subtitle":"S"}""");

        // NOTE: this walk WILL escalate to the real filesystem root if no
        // .git is ever found -- exercising exactly that path is the point.
        var result = RepoSettings.Discover(cwdFlag: root.Path, processCwd: "unused");

        Assert.IsNull(result.RepoRootDir);
        Assert.IsNull(result.RepoName);
        Assert.IsNull(result.Branch);
        // The .atv.json itself is still found even with no .git boundary.
        Assert.AreEqual("S", result.AllowedValues["subtitle"]);
    }
}
