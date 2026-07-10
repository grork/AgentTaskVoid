using Atv.Config;
using Atv.Diagnostics;
using Atv.Icons;
using Atv.Operations;
using Atv.Persistence;
using Atv.Store;
using Windows.ApplicationModel;

namespace Atv.Cli;

/// <summary>Everything <see cref="CompositionRoot.Build"/> assembled for one CLI invocation.</summary>
public sealed record RootContext(Dispatcher Dispatcher, Settings Settings, IReadOnlyList<string> SettingsWarnings);

/// <summary>
/// The ONLY place production instances get created (plan/phase-08's explicit
/// requirement): the real <see cref="AppTaskStore"/>, the named LIFE-18/
/// INFRA-6 mutexes, <see cref="AppPaths"/> itself, resolved
/// <see cref="Settings"/>, the durable <see cref="FailureLog"/>, and the real
/// <see cref="IconService"/>. Every other type either takes these as
/// constructor parameters (<see cref="TaskOperations"/>, <see cref="WriteGate"/>,
/// ...) or is itself fake-testable (<see cref="Dispatcher"/>) -- this type is
/// deliberately NOT exercised by the fake-backed logic suite (matches
/// <see cref="AppPaths.ForCurrentPackage"/>'s own untested-here status;
/// production wiring is proven by the phase-03 real-adapter suite and the
/// AC4 manual dogfood instead).
///
/// Wires the two items phase 06 deliberately parked here: (a) feeds
/// <see cref="SettingsLoader"/>'s non-fatal <see cref="SettingsLoadResult.Warnings"/>
/// into the durable log immediately after resolving settings -- the loader
/// itself has no <c>Atv.Diagnostics</c> dependency by design (config
/// resolution stays a pure function of its three input layers); (b) the
/// `--json`+`--strict` combination itself is a pure <see cref="Posture"/>
/// behavior with no composition-root-specific wiring, covered instead by
/// <c>tests/Atv.LogicTests/Diagnostics/PostureTests.cs</c>.
/// </summary>
public static class CompositionRoot
{
    public static RootContext Build(GlobalOptions global, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        AppPaths paths = ResolvePaths();
        DateTimeOffset bootTime = DateTimeOffset.Now;

        var flagOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (global.WatchdogModeRaw is { Length: > 0 } raw)
            flagOverrides["watchdog-mode"] = raw;

        SettingsLoadResult loadResult = SettingsLoader.Load(flagOverrides, ReadProcessEnvironment(), paths.ConfigPath);
        Settings settings = loadResult.Settings;

        var log = new FailureLog(paths.LogPath, settings.LogMaxBytes, settings.LogMaxAge);
        foreach (string warning in loadResult.Warnings)
            log.Append("settings", null, warning, bootTime);

        var output = new Output(stdout, stderr, global.Json);
        var posture = new Posture(log, output, global.Strict, global.Verbose);

        IAppTaskStore store = new AppTaskStore();
        var sidecar = new SidecarStore(paths.SidecarDir);
        var recycleBin = new RecycleBin(paths.RecycleBinDir);
        var icons = new IconService(paths.IconsDir, paths.RecycleBinDir, log: msg => log.Append("icon", null, msg, DateTimeOffset.Now));

        Mutex writeMutex = ResolveWriteMutex();
        var gate = new WriteGate(writeMutex, settings.MutexWaitBudget, strict: false, log: msg => log.Append("writegate", null, msg, DateTimeOffset.Now));
        var ops = new TaskOperations(store, sidecar, recycleBin, gate, settings.RecycleBinTtl, msg => log.Append("ops", null, msg, DateTimeOffset.Now), icons);

        var dispatcher = new Dispatcher(
            ops,
            posture,
            icons,
            defaultDeepLink: new Uri(paths.Root),
            hasIdentity: HasPackageIdentity,
            isSupported: store.IsSupported,
            watchdogMode: settings.WatchdogMode,
            watchdogMutexName: ResolveWatchdogMutexName(),
            watchdogLog: msg => log.Append("watchdog", null, msg, DateTimeOffset.Now));

        return new RootContext(dispatcher, settings, loadResult.Warnings);
    }

    private static Dictionary<string, string> ReadProcessEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
                result[key] = value;
        }
        return result;
    }

    /// <summary>Falls back to a non-package-scoped app-data folder when this process has no identity, so the durable-log/config machinery still has somewhere to (best-effort) write instead of throwing out of the composition root itself. The real failure signal in that case is <see cref="HasPackageIdentity"/> returning <see langword="false"/> to <see cref="Capability.Check"/>, not an exception here.</summary>
    private static AppPaths ResolvePaths()
    {
        try { return AppPaths.ForCurrentPackage(); }
        catch (Exception)
        {
            string fallbackRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Branding.Command, "no-identity");
            return AppPaths.ForRoot(fallbackRoot);
        }
    }

    private static Mutex ResolveWriteMutex()
    {
        try { return new Mutex(initiallyOwned: false, AppPaths.CurrentWriteMutexName); }
        catch (Exception) { return new Mutex(initiallyOwned: false); }
    }

    private static string ResolveWatchdogMutexName()
    {
        try { return AppPaths.CurrentWatchdogMutexName; }
        catch (Exception) { return $@"Local\{Branding.Name}-no-identity-watchdog"; }
    }

    private static bool HasPackageIdentity()
    {
        try { _ = Package.Current.Id.FullName; return true; }
        catch (Exception) { return false; }
    }
}
