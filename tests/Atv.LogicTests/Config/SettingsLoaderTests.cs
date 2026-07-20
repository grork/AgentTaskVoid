using Codevoid.AgentTaskVoid;
using Codevoid.AgentTaskVoid.Config;

namespace Codevoid.AgentTaskVoid.LogicTests.Config;

/// <summary>
/// ERGO-17's precedence chain (flag &gt; env &gt; file &gt; default) and
/// ERGO-18's brand-derived env var naming (phase-06 AC1), plus ERGO-26/AC4's
/// non-disruptive degradation for an absent/malformed config file.
/// </summary>
[TestClass]
public sealed class SettingsLoaderTests
{
    [TestMethod]
    public void Load_NoOverrides_ReturnsBuiltInDefaults_NoWarnings()
    {
        var result = SettingsLoader.Load();

        Assert.AreEqual(Settings.Default, result.Settings);
        Assert.IsEmpty(result.Warnings);
    }

    // ---- precedence: WatchdogMode (representative enum/string tunable) -----

    [TestMethod]
    public void Load_WatchdogMode_FlagOnly_UsesFlag()
    {
        var flags = new Dictionary<string, string> { ["watchdog-mode"] = "inproc" };
        var result = SettingsLoader.Load(flags: flags);
        Assert.AreEqual(WatchdogMode.InProc, result.Settings.WatchdogMode);
    }

    [TestMethod]
    public void Load_WatchdogMode_EnvOnly_UsesEnv()
    {
        var env = new Dictionary<string, string> { [SettingsLoader.CurrentEnvVarName("watchdog-mode")] = "off" };
        var result = SettingsLoader.Load(processEnvironment: env);
        Assert.AreEqual(WatchdogMode.Off, result.Settings.WatchdogMode);
    }

    [TestMethod]
    public void Load_WatchdogMode_FileOnly_UsesFile()
    {
        using var dir = new Persistence.TempDirectory();
        string path = Path.Combine(dir.Path, "config.json");
        Directory.CreateDirectory(dir.Path);
        File.WriteAllText(path, """{"watchdog-mode":"inproc"}""");

        var result = SettingsLoader.Load(configFilePath: path);
        Assert.AreEqual(WatchdogMode.InProc, result.Settings.WatchdogMode);
    }

    [TestMethod]
    public void Load_WatchdogMode_FlagBeatsEnvBeatsFileBeatsDefault()
    {
        using var dir = new Persistence.TempDirectory();
        string path = Path.Combine(dir.Path, "config.json");
        Directory.CreateDirectory(dir.Path);
        File.WriteAllText(path, """{"watchdog-mode":"off"}""");

        var flags = new Dictionary<string, string> { ["watchdog-mode"] = "spawn" };
        var env = new Dictionary<string, string> { [SettingsLoader.CurrentEnvVarName("watchdog-mode")] = "inproc" };

        // All four layers disagree -- flag must win.
        var result = SettingsLoader.Load(flags, env, path);
        Assert.AreEqual(WatchdogMode.Spawn, result.Settings.WatchdogMode);
    }

    [TestMethod]
    public void Load_WatchdogMode_NoFlag_EnvBeatsFile()
    {
        using var dir = new Persistence.TempDirectory();
        string path = Path.Combine(dir.Path, "config.json");
        Directory.CreateDirectory(dir.Path);
        File.WriteAllText(path, """{"watchdog-mode":"off"}""");

        var env = new Dictionary<string, string> { [SettingsLoader.CurrentEnvVarName("watchdog-mode")] = "inproc" };

        var result = SettingsLoader.Load(flags: null, processEnvironment: env, configFilePath: path);
        Assert.AreEqual(WatchdogMode.InProc, result.Settings.WatchdogMode);
    }

    [TestMethod]
    public void Load_WatchdogMode_NoFlagNoEnv_FileBeatsDefault()
    {
        using var dir = new Persistence.TempDirectory();
        string path = Path.Combine(dir.Path, "config.json");
        Directory.CreateDirectory(dir.Path);
        File.WriteAllText(path, """{"watchdog-mode":"off"}""");

        var result = SettingsLoader.Load(configFilePath: path);
        Assert.AreEqual(WatchdogMode.Off, result.Settings.WatchdogMode);
        Assert.AreNotEqual(Settings.Default.WatchdogMode, result.Settings.WatchdogMode);
    }

    // ---- precedence: IdleRunning (representative TimeSpan tunable) --------

    [TestMethod]
    public void Load_IdleRunning_FlagBeatsEnvBeatsFileBeatsDefault()
    {
        using var dir = new Persistence.TempDirectory();
        string path = Path.Combine(dir.Path, "config.json");
        Directory.CreateDirectory(dir.Path);
        File.WriteAllText(path, """{"idle-running":"00:05:00"}""");

        var flags = new Dictionary<string, string> { ["idle-running"] = "00:45:00" };
        var env = new Dictionary<string, string> { [SettingsLoader.CurrentEnvVarName("idle-running")] = "00:20:00" };

        var result = SettingsLoader.Load(flags, env, path);
        Assert.AreEqual(TimeSpan.FromMinutes(45), result.Settings.IdleRunning);
    }

    [TestMethod]
    public void Load_IdleRunning_NoFlag_EnvBeatsFile()
    {
        using var dir = new Persistence.TempDirectory();
        string path = Path.Combine(dir.Path, "config.json");
        Directory.CreateDirectory(dir.Path);
        File.WriteAllText(path, """{"idle-running":"00:05:00"}""");

        var env = new Dictionary<string, string> { [SettingsLoader.CurrentEnvVarName("idle-running")] = "00:20:00" };
        var result = SettingsLoader.Load(processEnvironment: env, configFilePath: path);
        Assert.AreEqual(TimeSpan.FromMinutes(20), result.Settings.IdleRunning);
    }

    [TestMethod]
    public void Load_IdleRunning_NothingSet_UsesDefault()
    {
        var result = SettingsLoader.Load();
        Assert.AreEqual(Settings.Default.IdleRunning, result.Settings.IdleRunning);
    }

    // ---- precedence: representative numeric tunables (long / int) ---------

    [TestMethod]
    public void Load_LogMaxBytes_FlagWins()
    {
        var flags = new Dictionary<string, string> { ["log-max-bytes"] = "2097152" };
        var result = SettingsLoader.Load(flags: flags);
        Assert.AreEqual(2097152L, result.Settings.LogMaxBytes);
    }

    [TestMethod]
    public void Load_RunStepMaxLength_FlagWins()
    {
        var flags = new Dictionary<string, string> { ["run-step-max-length"] = "80" };
        var result = SettingsLoader.Load(flags: flags);
        Assert.AreEqual(80, result.Settings.RunStepMaxLength);
    }

    // ---- independent per-field resolution ----------------------------------

    [TestMethod]
    public void Load_MixedSources_EachFieldResolvesIndependently()
    {
        using var dir = new Persistence.TempDirectory();
        string path = Path.Combine(dir.Path, "config.json");
        Directory.CreateDirectory(dir.Path);
        File.WriteAllText(path, """{"log-max-bytes":"500000","run-step-max-length":"64"}""");

        var flags = new Dictionary<string, string> { ["watchdog-mode"] = "inproc" };
        var env = new Dictionary<string, string> { [SettingsLoader.CurrentEnvVarName("idle-running")] = "00:15:00" };

        var result = SettingsLoader.Load(flags, env, path);

        Assert.AreEqual(WatchdogMode.InProc, result.Settings.WatchdogMode); // from flag
        Assert.AreEqual(TimeSpan.FromMinutes(15), result.Settings.IdleRunning); // from env
        Assert.AreEqual(500000L, result.Settings.LogMaxBytes); // from file
        Assert.AreEqual(64, result.Settings.RunStepMaxLength); // from file
        Assert.AreEqual(Settings.Default.IdlePaused, result.Settings.IdlePaused); // untouched -- default
        Assert.IsEmpty(result.Warnings);
    }

    // ---- brand-derived env var naming (ERGO-18) ----------------------------

    [TestMethod]
    public void BuildEnvVarName_UsesTheGivenCommandName_NotAHardcodedOne()
    {
        Assert.AreEqual("XYZ_WATCHDOG_MODE", SettingsLoader.BuildEnvVarName("xyz", "watchdog-mode"));
    }

    [TestMethod]
    public void BuildEnvVarName_DifferentCommandNames_ProduceDifferentEnvVarNames()
    {
        string a = SettingsLoader.BuildEnvVarName("atv", "watchdog-mode");
        string b = SettingsLoader.BuildEnvVarName("otherbrand", "watchdog-mode");
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void BuildEnvVarName_ReplacesHyphensWithUnderscores_AndUppercases()
    {
        Assert.AreEqual("ATV_RUN_STEP_MAX_LENGTH", SettingsLoader.BuildEnvVarName("atv", "run-step-max-length"));
    }

    [TestMethod]
    public void CurrentEnvVarName_MatchesBuildEnvVarName_WithTheCurrentBrandCommand()
    {
        // Renaming the brand means Branding.Command changes; this proves
        // CurrentEnvVarName is wired through BuildEnvVarName rather than a
        // second hardcoded prefix -- if the brand changed, this would still
        // hold without editing this test.
        string expected = SettingsLoader.BuildEnvVarName(Branding.Command, "watchdog-mode");
        Assert.AreEqual(expected, SettingsLoader.CurrentEnvVarName("watchdog-mode"));
        Assert.AreEqual("ATV_WATCHDOG_MODE", SettingsLoader.CurrentEnvVarName("watchdog-mode"));
    }

    [TestMethod]
    public void Load_ReadsEnv_UnderTheCurrentBrandDerivedName()
    {
        // Full round trip: build the env dict with the SAME derivation Load()
        // itself uses (not a magic string), proving Load actually consults
        // the brand-derived name rather than some other hardcoded one.
        var env = new Dictionary<string, string>
        {
            [SettingsLoader.CurrentEnvVarName("idle-running")] = "00:12:00",
        };
        var result = SettingsLoader.Load(processEnvironment: env);
        Assert.AreEqual(TimeSpan.FromMinutes(12), result.Settings.IdleRunning);
    }

    [TestMethod]
    public void Load_EnvLookup_IsCaseInsensitive()
    {
        var env = new Dictionary<string, string> { ["atv_watchdog_mode"] = "off" };
        var result = SettingsLoader.Load(processEnvironment: env);
        Assert.AreEqual(WatchdogMode.Off, result.Settings.WatchdogMode);
    }

    // ---- absent / malformed config file (AC4) ------------------------------

    [TestMethod]
    public void Load_ConfigFileMissing_FallsBackToDefaults_NoWarnings()
    {
        using var dir = new Persistence.TempDirectory();
        string path = Path.Combine(dir.Path, "does-not-exist.json");

        var result = SettingsLoader.Load(configFilePath: path);

        Assert.AreEqual(Settings.Default, result.Settings);
        Assert.IsEmpty(result.Warnings);
    }

    [TestMethod]
    public void Load_ConfigFileNullPath_FallsBackToDefaults_NoWarnings()
    {
        var result = SettingsLoader.Load(configFilePath: null);
        Assert.AreEqual(Settings.Default, result.Settings);
        Assert.IsEmpty(result.Warnings);
    }

    [TestMethod]
    public void Load_ConfigFileMalformedJson_FallsBackToDefaults_WithWarning()
    {
        using var dir = new Persistence.TempDirectory();
        Directory.CreateDirectory(dir.Path);
        string path = Path.Combine(dir.Path, "config.json");
        File.WriteAllText(path, "{ not valid json !!");

        var result = SettingsLoader.Load(configFilePath: path);

        Assert.AreEqual(Settings.Default, result.Settings);
        Assert.IsNotEmpty(result.Warnings);
    }

    [TestMethod]
    public void Load_ConfigFileGarbageBytes_NeverThrows_FallsBackToDefaults()
    {
        using var dir = new Persistence.TempDirectory();
        Directory.CreateDirectory(dir.Path);
        string path = Path.Combine(dir.Path, "config.json");
        File.WriteAllBytes(path, [0x00, 0xFF, 0x13, 0x37, 0xDE, 0xAD, 0xBE, 0xEF]);

        var result = SettingsLoader.Load(configFilePath: path);

        Assert.AreEqual(Settings.Default, result.Settings);
        Assert.IsNotEmpty(result.Warnings);
    }

    [TestMethod]
    public void Load_ConfigFileBadValueForOneKey_OnlyThatKeyFallsBack_RestStillApply()
    {
        using var dir = new Persistence.TempDirectory();
        Directory.CreateDirectory(dir.Path);
        string path = Path.Combine(dir.Path, "config.json");
        File.WriteAllText(path, """{"watchdog-mode":"bogus-value","idle-running":"00:45:00"}""");

        var result = SettingsLoader.Load(configFilePath: path);

        Assert.AreEqual(Settings.Default.WatchdogMode, result.Settings.WatchdogMode); // bad value -- fell back
        Assert.AreEqual(TimeSpan.FromMinutes(45), result.Settings.IdleRunning); // good value -- still applied
        Assert.IsNotEmpty(result.Warnings);
    }

    [TestMethod]
    public void Load_ConfigFileHasNullValue_TreatedAsAbsent_NoCrash()
    {
        using var dir = new Persistence.TempDirectory();
        Directory.CreateDirectory(dir.Path);
        string path = Path.Combine(dir.Path, "config.json");
        File.WriteAllText(path, """{"watchdog-mode":null}""");

        var result = SettingsLoader.Load(configFilePath: path);

        Assert.AreEqual(Settings.Default.WatchdogMode, result.Settings.WatchdogMode);
    }

    [TestMethod]
    public void Load_EnvValueMalformed_FallsThroughToFile_WithWarning()
    {
        using var dir = new Persistence.TempDirectory();
        Directory.CreateDirectory(dir.Path);
        string path = Path.Combine(dir.Path, "config.json");
        File.WriteAllText(path, """{"idle-running":"00:25:00"}""");

        var env = new Dictionary<string, string> { [SettingsLoader.CurrentEnvVarName("idle-running")] = "not-a-timespan" };

        var result = SettingsLoader.Load(processEnvironment: env, configFilePath: path);

        Assert.AreEqual(TimeSpan.FromMinutes(25), result.Settings.IdleRunning);
        Assert.IsNotEmpty(result.Warnings);
    }

    [TestMethod]
    public void Load_FlagValueMalformed_FallsThroughToDefault_WithWarning()
    {
        var flags = new Dictionary<string, string> { ["watchdog-mode"] = "not-a-mode" };
        var result = SettingsLoader.Load(flags: flags);

        Assert.AreEqual(Settings.Default.WatchdogMode, result.Settings.WatchdogMode);
        Assert.IsNotEmpty(result.Warnings);
    }
}
