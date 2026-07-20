using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.LogicTests.Persistence;

namespace Codevoid.AgentTaskVoid.LogicTests.Config;

/// <summary>
/// ERGO-30 (phase 17) AC5's precedence matrix for
/// <see cref="SettingsLoader.ResolvePresentationKey"/> -- the repo-scoped
/// presentation-key resolver (<c>flag &gt; env &gt; repo-file &gt;
/// user-file</c>), including the two orderings that are easy to get
/// backwards: repo-beats-user and env-beats-repo. Also covers
/// <see cref="SettingsLoader.ExtractEnvFor"/>/<see cref="SettingsLoader.ReadFileFor"/>,
/// the reusable layer-extraction helpers <c>Codevoid.AgentTaskVoid.Config.RepoSettings</c>
/// composes this precedence resolver against.
/// </summary>
[TestClass]
public sealed class SettingsLoaderPresentationTests
{
    private const string Key = "subtitle"; // the representative key for this matrix.

    [TestMethod]
    public void ResolvePresentationKey_AllLayersAbsent_ReturnsNull()
    {
        Assert.IsNull(SettingsLoader.ResolvePresentationKey(Key, null, null, null, null));
    }

    [TestMethod]
    public void ResolvePresentationKey_UserFileOnly_UsesUserFile()
    {
        var userFile = new Dictionary<string, string> { [Key] = "from-user" };
        Assert.AreEqual("from-user", SettingsLoader.ResolvePresentationKey(Key, null, null, null, userFile));
    }

    [TestMethod]
    public void ResolvePresentationKey_RepoFileBeatsUserFile()
    {
        var repo = new Dictionary<string, string> { [Key] = "from-repo" };
        var userFile = new Dictionary<string, string> { [Key] = "from-user" };
        Assert.AreEqual("from-repo", SettingsLoader.ResolvePresentationKey(Key, null, null, repo, userFile));
    }

    [TestMethod]
    public void ResolvePresentationKey_EnvBeatsRepoFile()
    {
        var env = new Dictionary<string, string> { [Key] = "from-env" };
        var repo = new Dictionary<string, string> { [Key] = "from-repo" };
        Assert.AreEqual("from-env", SettingsLoader.ResolvePresentationKey(Key, null, env, repo, null));
    }

    [TestMethod]
    public void ResolvePresentationKey_FlagBeatsEverything()
    {
        var env = new Dictionary<string, string> { [Key] = "from-env" };
        var repo = new Dictionary<string, string> { [Key] = "from-repo" };
        var userFile = new Dictionary<string, string> { [Key] = "from-user" };
        Assert.AreEqual("from-flag", SettingsLoader.ResolvePresentationKey(Key, "from-flag", env, repo, userFile));
    }

    [TestMethod]
    public void ResolvePresentationKey_AllFiveLayers_FlagWinsOverAllFour()
    {
        // All four lower layers disagree with the flag AND each other --
        // proves the FULL flag > env > repo > user > default chain in one shot.
        var env = new Dictionary<string, string> { [Key] = "from-env" };
        var repo = new Dictionary<string, string> { [Key] = "from-repo" };
        var userFile = new Dictionary<string, string> { [Key] = "from-user" };
        Assert.AreEqual("from-flag", SettingsLoader.ResolvePresentationKey(Key, "from-flag", env, repo, userFile));

        // Remove the flag: env must now win over repo and user.
        Assert.AreEqual("from-env", SettingsLoader.ResolvePresentationKey(Key, null, env, repo, userFile));

        // Remove env too: repo must now win over user (the easy-to-get-backwards case).
        Assert.AreEqual("from-repo", SettingsLoader.ResolvePresentationKey(Key, null, null, repo, userFile));

        // Remove repo too: user-file is the last resort before absence.
        Assert.AreEqual("from-user", SettingsLoader.ResolvePresentationKey(Key, null, null, null, userFile));

        // Remove user-file too: nothing left -- absence is the "built-in default" signal.
        Assert.IsNull(SettingsLoader.ResolvePresentationKey(Key, null, null, null, null));
    }

    // ---- ExtractEnvFor: arbitrary key set, brand-derived env names ----------

    [TestMethod]
    public void ExtractEnvFor_ReadsOnlyRequestedKeys_BrandDerivedNames()
    {
        var processEnv = new Dictionary<string, string>
        {
            [SettingsLoader.CurrentEnvVarName("subtitle")] = "env-subtitle",
            [SettingsLoader.CurrentEnvVarName("icon")] = "env-icon",
            ["UNRELATED_VAR"] = "ignored",
        };

        var result = SettingsLoader.ExtractEnvFor(processEnv, ["subtitle"]);

        Assert.AreEqual("env-subtitle", result["subtitle"]);
        Assert.IsFalse(result.ContainsKey("icon"), "must only extract the requested key set.");
    }

    [TestMethod]
    public void ExtractEnvFor_CaseInsensitive_MatchesRealWindowsEnvBehavior()
    {
        var processEnv = new Dictionary<string, string> { [SettingsLoader.CurrentEnvVarName("subtitle").ToLowerInvariant()] = "env-subtitle" };
        var result = SettingsLoader.ExtractEnvFor(processEnv, ["subtitle"]);
        Assert.AreEqual("env-subtitle", result["subtitle"]);
    }

    // ---- ReadFileFor: filtered slice of the SAME user config file -----------

    [TestMethod]
    public void ReadFileFor_FiltersToRequestedKeys_IgnoresOthers()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Path);
        string path = Path.Combine(dir.Path, "atv-config.json");
        File.WriteAllText(path, """{"subtitle":"S","watchdog-mode":"off"}""");

        var result = SettingsLoader.ReadFileFor(path, ["subtitle", "icon"]);

        Assert.AreEqual("S", result["subtitle"]);
        Assert.IsFalse(result.ContainsKey("watchdog-mode"));
        Assert.IsFalse(result.ContainsKey("icon"));
    }

    [TestMethod]
    public void ReadFileFor_AbsentFile_ReturnsEmpty_NoThrow()
    {
        var result = SettingsLoader.ReadFileFor(@"C:\definitely\does\not\exist.json", ["subtitle"]);
        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void ReadFileFor_MalformedFile_DegradesToEmpty_NoThrow()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Path);
        string path = Path.Combine(dir.Path, "atv-config.json");
        File.WriteAllText(path, "{not valid");

        var result = SettingsLoader.ReadFileFor(path, ["subtitle"]);
        Assert.IsEmpty(result);
    }
}
