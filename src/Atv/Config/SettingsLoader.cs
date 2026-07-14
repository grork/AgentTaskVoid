using System.Globalization;
using System.Text.Json;
using Atv.Persistence;

namespace Atv.Config;

/// <summary>Everything <see cref="SettingsLoader.Load"/> resolved, plus any non-fatal problems it degraded past (ERGO-26/AC4: absent/malformed input never crashes -- it falls back and reports here instead of throwing).</summary>
public sealed record SettingsLoadResult(Settings Settings, IReadOnlyList<string> Warnings);

/// <summary>
/// ERGO-17's precedence resolver: <c>flags &gt; env var &gt; config file &gt;
/// built-in default</c>, applied INDEPENDENTLY PER TUNABLE. A value that
/// fails to parse at one layer is treated as absent at that layer and the
/// next lower layer is tried -- never a hard failure, matching FAIL-1's
/// non-disruptive posture down at config-resolution granularity (one bad
/// setting never poisons the rest of the file, or the whole run).
///
/// Every tunable flows through the same raw-string pipeline at every layer:
/// flags/env are always strings by nature (argv / process environment); the
/// config file is deliberately a flat JSON object of string-to-string
/// (<see cref="SettingsJsonContext"/>) so file/env/flag values share one
/// parser per field -- no per-source special-casing, no per-source bugs.
/// </summary>
public static class SettingsLoader
{
    // Single source for each tunable's key string -- feeds both the config
    // file's JSON property name and, via BuildEnvVarName, the brand-derived
    // env var name (ERGO-18: derive, never hardcode a second literal).
    private static class Keys
    {
        public const string WatchdogMode = "watchdog-mode";
        public const string IdleRunning = "idle-running";
        public const string IdlePaused = "idle-paused";
        public const string IdleNeedsAttention = "idle-needs-attention";
        public const string IdleCompleted = "idle-completed";
        public const string RecycleBinTtl = "recycle-bin-ttl";
        public const string MutexWaitBudget = "mutex-wait-budget";
        public const string WatchdogPollInterval = "watchdog-poll-interval";
        public const string LogMaxBytes = "log-max-bytes";
        public const string LogMaxAge = "log-max-age";
        public const string RunUpdateDebounce = "run-update-debounce";
        public const string RunStepMaxLength = "run-step-max-length";
        public const string RunKeepAliveInterval = "run-keepalive-interval";
        public const string ReadyDecayThreshold = "ready-decay-threshold";

        public static readonly IReadOnlyList<string> All =
        [
            WatchdogMode, IdleRunning, IdlePaused, IdleNeedsAttention, IdleCompleted,
            RecycleBinTtl, MutexWaitBudget, WatchdogPollInterval, LogMaxBytes, LogMaxAge,
            RunUpdateDebounce, RunStepMaxLength, RunKeepAliveInterval, ReadyDecayThreshold,
        ];
    }

    /// <summary>
    /// ERGO-26's config-path helper: the path is non-obvious (package
    /// app-data), so <c>doctor</c> (phase 10) needs a way to print it. Thin
    /// wrapper over <see cref="AppPaths.ForCurrentPackage"/> -- requires the
    /// current process's package identity, so this is intentionally NOT
    /// exercised by the identity-free logic suite (matching
    /// <c>AppPaths.ForCurrentPackage</c>'s own untested-here status).
    /// </summary>
    public static string CurrentConfigPath() => AppPaths.ForCurrentPackage().ConfigPath;

    /// <summary>
    /// Resolves final <see cref="Settings"/> from the three override layers,
    /// all optional -- a caller with no flags/env/file gets pure
    /// <see cref="Settings.Default"/>. <paramref name="flags"/> is keyed by
    /// the same bare tunable name as the config file (e.g.
    /// <c>"watchdog-mode"</c>, matching the <c>--watchdog-mode</c> flag 1:1
    /// once phase 08 wires argv parsing); <paramref name="processEnvironment"/>
    /// is keyed by real env var names (e.g. <c>ATV_WATCHDOG_MODE</c>) so a
    /// caller can pass <see cref="Environment.GetEnvironmentVariables"/>
    /// straight through (case-insensitively -- Windows env vars are).
    /// </summary>
    public static SettingsLoadResult Load(
        IReadOnlyDictionary<string, string>? flags = null,
        IReadOnlyDictionary<string, string>? processEnvironment = null,
        string? configFilePath = null)
    {
        flags ??= EmptyMap;
        processEnvironment ??= EmptyMap;

        var warnings = new List<string>();
        var envByKey = ExtractEnv(processEnvironment, warnings);
        var fileByKey = ReadFile(configFilePath, warnings);

        Settings d = Settings.Default;
        var settings = new Settings(
            WatchdogMode: Resolve(Keys.WatchdogMode, d.WatchdogMode, ParseWatchdogMode, flags, envByKey, fileByKey, warnings),
            IdleRunning: Resolve(Keys.IdleRunning, d.IdleRunning, ParseTimeSpan, flags, envByKey, fileByKey, warnings),
            IdlePaused: Resolve(Keys.IdlePaused, d.IdlePaused, ParseTimeSpan, flags, envByKey, fileByKey, warnings),
            IdleNeedsAttention: Resolve(Keys.IdleNeedsAttention, d.IdleNeedsAttention, ParseTimeSpan, flags, envByKey, fileByKey, warnings),
            IdleCompleted: Resolve(Keys.IdleCompleted, d.IdleCompleted, ParseTimeSpan, flags, envByKey, fileByKey, warnings),
            RecycleBinTtl: Resolve(Keys.RecycleBinTtl, d.RecycleBinTtl, ParseTimeSpan, flags, envByKey, fileByKey, warnings),
            MutexWaitBudget: Resolve(Keys.MutexWaitBudget, d.MutexWaitBudget, ParseTimeSpan, flags, envByKey, fileByKey, warnings),
            WatchdogPollInterval: Resolve(Keys.WatchdogPollInterval, d.WatchdogPollInterval, ParseTimeSpan, flags, envByKey, fileByKey, warnings),
            LogMaxBytes: Resolve(Keys.LogMaxBytes, d.LogMaxBytes, ParseLong, flags, envByKey, fileByKey, warnings),
            LogMaxAge: Resolve(Keys.LogMaxAge, d.LogMaxAge, ParseTimeSpan, flags, envByKey, fileByKey, warnings),
            RunUpdateDebounce: Resolve(Keys.RunUpdateDebounce, d.RunUpdateDebounce, ParseTimeSpan, flags, envByKey, fileByKey, warnings),
            RunStepMaxLength: Resolve(Keys.RunStepMaxLength, d.RunStepMaxLength, ParseInt, flags, envByKey, fileByKey, warnings),
            RunKeepAliveInterval: Resolve(Keys.RunKeepAliveInterval, d.RunKeepAliveInterval, ParseTimeSpan, flags, envByKey, fileByKey, warnings),
            ReadyDecayThreshold: Resolve(Keys.ReadyDecayThreshold, d.ReadyDecayThreshold, ParseTimeSpan, flags, envByKey, fileByKey, warnings));

        return new SettingsLoadResult(settings, warnings);
    }

    // ---- brand-derived env var naming (ERGO-18) --------------------------

    /// <summary>The env var name for <paramref name="key"/> under the CURRENT brand (<see cref="Branding.Command"/>), e.g. <c>ATV_WATCHDOG_MODE</c>.</summary>
    public static string CurrentEnvVarName(string key) => BuildEnvVarName(Branding.Command, key);

    /// <summary>
    /// Pure, testable half of <see cref="CurrentEnvVarName"/> -- takes the
    /// command name as plain data, mirroring
    /// <see cref="AppPaths.BuildWriteMutexName"/>'s pattern: proving
    /// brand-derivation means passing a DIFFERENT command-name string here,
    /// not editing <see cref="Branding"/> itself.
    /// </summary>
    public static string BuildEnvVarName(string commandName, string key)
        => $"{commandName.ToUpperInvariant()}_{key.Replace('-', '_').ToUpperInvariant()}";

    // ---- layer extraction ---------------------------------------------------

    private static Dictionary<string, string> ExtractEnv(IReadOnlyDictionary<string, string> processEnvironment, List<string> warnings)
    {
        // Defensive case-insensitive shadow copy: real Windows env vars are
        // case-insensitive, and a caller might hand us a case-sensitive map.
        var caseInsensitive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in processEnvironment)
            caseInsensitive[kv.Key] = kv.Value;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in Keys.All)
        {
            string varName = CurrentEnvVarName(key);
            if (caseInsensitive.TryGetValue(varName, out string? raw) && raw is not null)
                result[key] = raw;
        }
        return result;
    }

    private static Dictionary<string, string> ReadFile(string? path, List<string> warnings)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return EmptyMap; // Absent config is normal (ERGO-26: optional, vanishes on uninstall) -- not a failure, no warning.

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            var dict = JsonSerializer.Deserialize(bytes, SettingsJsonContext.Default.DictionaryStringString);
            return dict is not null
                ? new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase)
                : EmptyMap;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Config file '{path}' could not be read/parsed ({ex.GetType().Name}: {ex.Message}) -- using built-in defaults for every setting it would have supplied.");
            return EmptyMap;
        }
    }

    // ---- per-field resolution ------------------------------------------------

    private static T Resolve<T>(
        string key,
        T fallback,
        Func<string, (bool Ok, T Value)> parse,
        IReadOnlyDictionary<string, string> flags,
        IReadOnlyDictionary<string, string> env,
        IReadOnlyDictionary<string, string> file,
        List<string> warnings)
    {
        foreach (var (layerName, source) in Layers(flags, env, file))
        {
            if (!source.TryGetValue(key, out string? raw) || raw is null)
                continue;

            var (ok, value) = parse(raw);
            if (ok)
                return value;

            warnings.Add($"Ignoring invalid {layerName} value for '{key}': '{raw}' -- trying the next-lower-precedence source.");
        }
        return fallback;
    }

    private static IEnumerable<(string Name, IReadOnlyDictionary<string, string> Source)> Layers(
        IReadOnlyDictionary<string, string> flags, IReadOnlyDictionary<string, string> env, IReadOnlyDictionary<string, string> file)
    {
        yield return ("flag", flags);
        yield return ("env", env);
        yield return ("file", file);
    }

    // ---- per-type parsers -------------------------------------------------

    private static (bool, WatchdogMode) ParseWatchdogMode(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "spawn" => (true, WatchdogMode.Spawn),
        "inproc" => (true, WatchdogMode.InProc),
        "off" => (true, WatchdogMode.Off),
        _ => (false, default),
    };

    private static (bool, TimeSpan) ParseTimeSpan(string raw)
        => TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var value) ? (true, value) : (false, default);

    private static (bool, long) ParseLong(string raw)
        => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? (true, value) : (false, default);

    private static (bool, int) ParseInt(string raw)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? (true, value) : (false, default);

    private static readonly Dictionary<string, string> EmptyMap = new(StringComparer.OrdinalIgnoreCase);
}
