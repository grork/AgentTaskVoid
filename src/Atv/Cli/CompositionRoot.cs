using System.Text;
using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Diagnostics;
using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.Operations;
using Codevoid.AgentTaskVoid.Persistence;
using Codevoid.AgentTaskVoid.Presence;
using Codevoid.AgentTaskVoid.Run;
using Codevoid.AgentTaskVoid.Semantics;
using Codevoid.AgentTaskVoid.Store;
using Codevoid.AgentTaskVoid.Watchdog;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Codevoid.AgentTaskVoid.Cli;

/// <summary>Everything <see cref="CompositionRoot.Build"/> assembled for one CLI invocation.</summary>
public sealed record RootContext(Dispatcher Dispatcher, Settings Settings, IReadOnlyList<string> SettingsWarnings);

/// <summary>
/// The ONLY place production instances get created (plan/phase-08's explicit
/// requirement): the real <see cref="AppTaskStore"/>, the named LIFE-18/
/// INFRA-6 mutexes, <see cref="AppPaths"/> itself, resolved
/// <see cref="Settings"/>, the durable <see cref="FailureLog"/>, the real
/// <see cref="IconService"/>, and (phase 09) the real
/// <see cref="ProcessHost"/>/<see cref="InProcThreadHost"/> watchdog hosts.
/// Every other type either takes these as constructor parameters
/// (<see cref="TaskOperations"/>, <see cref="WriteGate"/>, ...) or is itself
/// fake-testable (<see cref="Dispatcher"/>) -- this type is deliberately NOT
/// exercised by the fake-backed logic suite (matches
/// <see cref="AppPaths.ForCurrentPackage"/>'s own untested-here status);
/// production wiring is proven by the phase-03 real-adapter suite and the
/// AC4 manual dogfood instead).
///
/// Wires the two items phase 06 deliberately parked here: (a) feeds
/// <see cref="SettingsLoader"/>'s non-fatal <see cref="SettingsLoadResult.Warnings"/>
/// into the durable log immediately after resolving settings -- the loader
/// itself has no <c>Codevoid.AgentTaskVoid.Diagnostics</c> dependency by design (config
/// resolution stays a pure function of its three input layers); (b) the
/// `--json`+`--strict` combination itself is a pure <see cref="Posture"/>
/// behavior with no composition-root-specific wiring, covered instead by
/// <c>tests/Atv.LogicTests/Diagnostics/PostureTests.cs</c>.
///
/// Phase 09: <see cref="BuildWatchdogDeps"/>/<see cref="BuildWatchdogRunContext"/>
/// share the same <see cref="BuildBootstrap"/> the lifecycle-verb path
/// (<see cref="Build"/>) uses -- one bootstrap routine, three entry points
/// (<see cref="Build"/> for <see cref="Dispatcher"/>, <see cref="BuildWatchdogDeps"/>
/// for <c>Program.cs</c>'s LIFE-20 boot-recovery flat clear,
/// <see cref="BuildWatchdogRunContext"/> for the hidden <c>watchdog</c> verb
/// AND the lazy <see cref="InProcThreadHost"/> factory).
/// </summary>
public static class CompositionRoot
{
    public static RootContext Build(GlobalOptions global, TextWriter stdout, TextWriter stderr)
    {
        Bootstrap b = BuildBootstrap(global);

        var output = new Output(stdout, stderr, global.Json);
        var posture = new Posture(b.Log, output, global.Strict, global.Verbose);

        var ops = new TaskOperations(b.Store, b.Sidecar, b.RecycleBin, b.Gate, b.Settings.RecycleBinTtl, msg => b.Log.Append("ops", null, msg, DateTimeOffset.Now), b.Icons);

        // ERGO-30 (phase 17): the repo-scoped-defaults discovery delegate is
        // built ONCE here (closing over `global.Cwd` + the real process cwd)
        // but does NOTHING until invoked -- merely constructing this closure
        // performs no filesystem walk. SemanticEngine only ever calls it from
        // its upsert CREATE branch (AC3); DoctorContext below shares the exact
        // same delegate for `doctor`'s own unconditional diagnostic call.
        Func<RepoDiscoveryResult> discoverRepo = () => RepoSettings.Discover(global.Cwd, Environment.CurrentDirectory);
        IReadOnlyList<string> presentationKeys = RepoSettings.AllowlistKeys;
        var presentationEnv = SettingsLoader.ExtractEnvFor(ReadProcessEnvironment(), presentationKeys);
        var presentationUserFile = SettingsLoader.ReadFileFor(b.Paths.ConfigPath, presentationKeys);
        var groupRegistry = new IconGroupRegistry(Path.Combine(b.Paths.IconsDir, "groups"));

        // Part 1 item 7: the app-data deep-link URI is ADDED to the engine as
        // ERGO-35's floor (the dispatcher below keeps its own copy -- "re-
        // plumbed" means added, not moved; run's deps still consume the
        // dispatcher's copy via defaultDeepLink).
        Uri appDataDeepLinkFloor = new(b.Paths.Root);

        var engine = new SemanticEngine(
            b.Store, b.Sidecar, b.RecycleBin, b.Gate, b.Settings.RecycleBinTtl, ops, b.Icons,
            msg => b.Log.Append("engine", null, msg, DateTimeOffset.Now),
            discoverRepo: discoverRepo,
            presentationEnv: presentationEnv,
            presentationUserFile: presentationUserFile,
            groupRegistry: groupRegistry,
            deepLinkFloor: appDataDeepLinkFloor);

        string watchdogMutexName = ResolveWatchdogMutexName();
        Action<string> watchdogLog = msg => b.Log.Append("watchdog", null, msg, DateTimeOffset.Now);
        IWatchdogHost processHost = new ProcessHost(Environment.ProcessPath ?? "", BuildWatchdogSpawnArgs(global), watchdogLog);
        IWatchdogHost inProcHost = new InProcThreadHost(() => BuildWatchdogRunContext(global), watchdogLog);
        Action ensureWatchdog = () => EnsureWatchdog.Run(b.Settings.WatchdogMode, watchdogMutexName, processHost, inProcHost, watchdogLog);

        var doctorProbes = new DoctorProbes(
            PackageFullName: TryGetPackageFullName,
            ApiSupported: b.Store.IsSupported,
            DeveloperModeEnabled: IsDeveloperModeEnabled,
            WatchdogRunning: () => EnsureWatchdog.IsRunning(watchdogMutexName),
            PackageName: TryGetPackageName);
        var doctorContext = new DoctorContext(doctorProbes, b.Paths.ConfigPath, b.Paths.Root, b.Paths.SidecarDir, b.Paths.LogPath, DiscoverRepo: discoverRepo);

        var dispatcher = new Dispatcher(
            ops,
            engine,
            posture,
            output,
            b.Icons,
            defaultDeepLink: appDataDeepLinkFloor,
            hasIdentity: HasPackageIdentity,
            isSupported: b.Store.IsSupported,
            ensureWatchdog: ensureWatchdog,
            doctorContext: doctorContext,
            settings: b.Settings,
            clock: () => DateTimeOffset.Now,
            sleep: Thread.Sleep,
            spawnChild: ChildProcess.Start,
            stdoutMirror: Console.OpenStandardOutput(),
            stderrMirror: Console.OpenStandardError(),
            // ERGO-31's "-" stdin convention (LIFE-24 S2-walk item 1): explicit UTF-8,
            // never the console's ambient code-page encoding -- Console.In can silently
            // mojibake non-ASCII text on a non-UTF-8 console code page.
            stdin: new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)),
            // AC11: diagnostic instrumentation for the concurrent-fan-out latency investigation --
            // "trace-in" (raw call received) / "trace-out" (what was applied), wired the same way
            // as every other FailureLog category below ("ops"/"engine"/"watchdog"/"writegate"/"icon").
            traceIn: msg => b.Log.Append("trace-in", null, msg, DateTimeOffset.Now),
            traceOut: msg => b.Log.Append("trace-out", null, msg, DateTimeOffset.Now));

        return new RootContext(dispatcher, b.Settings, b.Warnings);
    }

    /// <summary>Everything the LIFE-20 boot-recovery flat clear needs -- one bootstrap, no mutex/host wiring.</summary>
    public static WatchdogDeps BuildWatchdogDeps(GlobalOptions global) => BuildWatchdogDepsCore(global).Deps;

    /// <summary>Everything <see cref="Verbs.WatchdogVerb.Run"/> (and the lazy <see cref="InProcThreadHost"/> factory) needs to actually run <see cref="WatchdogLoop.Run"/>: the deps, the real LIFE-18 single-instance mutex, real <see cref="Thread.Sleep(TimeSpan)"/>, and the real <see cref="StartupTaskControl"/> enable/disable hooks.</summary>
    public static RunContext BuildWatchdogRunContext(GlobalOptions global)
    {
        (WatchdogDeps deps, AppPaths _) = BuildWatchdogDepsCore(global);
        string mutexName = ResolveWatchdogMutexName();
        Mutex instanceMutex = ResolveWatchdogInstanceMutex(mutexName);
        return new RunContext(deps, instanceMutex, Thread.Sleep, StartupTaskControl.EnableSync, StartupTaskControl.DisableSync);
    }

    private static (WatchdogDeps Deps, AppPaths Paths) BuildWatchdogDepsCore(GlobalOptions global)
    {
        Bootstrap b = BuildBootstrap(global);
        var deps = new WatchdogDeps(
            b.Store, b.Sidecar, b.RecycleBin, b.Gate, b.Icons,
            msg => b.Log.Append("watchdog", null, msg, DateTimeOffset.Now),
            () => DateTimeOffset.Now,
            b.Settings,
            Presence: new Win32PresenceSource());
        return (deps, b.Paths);
    }

    private static IReadOnlyList<string> BuildWatchdogSpawnArgs(GlobalOptions global)
        => global.WaitForDebugger ? ["watchdog", "--wait-for-debugger"] : ["watchdog"];

    // ---- shared bootstrap --------------------------------------------------------

    /// <summary>Everything every composition entry point below needs before it diverges -- paths, settings, the durable log (with settings warnings already flushed into it), the real store/sidecar/recycle-bin/icons, and the shared write mutex/gate.</summary>
    private sealed record Bootstrap(
        AppPaths Paths, Settings Settings, IReadOnlyList<string> Warnings, FailureLog Log,
        IAppTaskStore Store, SidecarStore Sidecar, RecycleBin RecycleBin, IconService Icons,
        WriteGate Gate);

    private static Bootstrap BuildBootstrap(GlobalOptions global)
    {
        AppPaths paths = ResolvePaths();
        DateTimeOffset bootTime = DateTimeOffset.Now;

        var flagOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (global.WatchdogModeRaw is { Length: > 0 } raw)
            flagOverrides["watchdog-mode"] = raw;

        SettingsLoadResult loadResult = SettingsLoader.Load(flagOverrides, ReadProcessEnvironment(), paths.ConfigPath);
        Settings settings = loadResult.Settings;

        // DIST-3 (2026-07-10 amendment): computed once here, not per-Append-call --
        // the (dev)/(test) marker stamped onto every durable failure-log entry so
        // traces are self-identifying (operator's explicit ask).
        string? buildKindMarker = BuildKindResolver.Marker(TryGetPackageName());
        var log = new FailureLog(paths.LogPath, settings.LogMaxBytes, settings.LogMaxAge, buildKindMarker);
        foreach (string warning in loadResult.Warnings)
            log.Append("settings", null, warning, bootTime);

        IAppTaskStore store = new AppTaskStore();
        var sidecar = new SidecarStore(paths.SidecarDir);
        var recycleBin = new RecycleBin(paths.RecycleBinDir);
        var icons = new IconService(paths.IconsDir, paths.RecycleBinDir, log: msg => log.Append("icon", null, msg, DateTimeOffset.Now));

        Mutex writeMutex = ResolveWriteMutex();
        var gate = new WriteGate(writeMutex, settings.MutexWaitBudget, strict: false, log: msg => log.Append("writegate", null, msg, DateTimeOffset.Now));

        return new Bootstrap(paths, settings, loadResult.Warnings, log, store, sidecar, recycleBin, icons, gate);
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
        catch (Exception) { return $@"Local\{Branding.IdentityName}-no-identity-watchdog"; }
    }

    private static Mutex ResolveWatchdogInstanceMutex(string name)
    {
        try { return new Mutex(initiallyOwned: false, name); }
        catch (Exception) { return new Mutex(initiallyOwned: false); }
    }

    private static bool HasPackageIdentity()
    {
        try { _ = Package.Current.Id.FullName; return true; }
        catch (Exception) { return false; }
    }

    /// <summary>`doctor`'s identity probe (<see cref="DoctorProbes.PackageFullName"/>): the PFN when present, <see langword="null"/> otherwise -- the richer sibling of <see cref="HasPackageIdentity"/>, which only needs a bool.</summary>
    private static string? TryGetPackageFullName()
    {
        try { return Package.Current.Id.FullName; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// DIST-3's 2026-07-10 amendment: the declared Identity Name
    /// (<c>Package.Current.Id.Name</c> -- distinct from
    /// <see cref="TryGetPackageFullName"/>'s PFN, which also folds in the
    /// publisher hash), fed to <see cref="BuildKindResolver"/> to classify
    /// this process as Release/Dev/Test for the <c>(dev)</c>/<c>(test)</c>
    /// console/log marker. Shared by <see cref="DoctorProbes.PackageName"/>
    /// and the durable <see cref="FailureLog"/>'s marker, so there is exactly
    /// one place this WinRT call is made.
    /// </summary>
    private static string? TryGetPackageName()
    {
        try { return Package.Current.Id.Name; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// `doctor`'s dev-facing Developer Mode probe (INFRA-17): reads the same
    /// registry value Windows Settings' "Developer Mode" toggle writes
    /// (<c>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock\AllowDevelopmentWithoutDevLicense</c>,
    /// DWORD 1 = on). Never throws out (FAIL-1): any registry-access failure
    /// (permissions, key absent on a locked-down machine, ...) degrades to
    /// "disabled" -- this is informational only, never gates a write.
    /// </summary>
    private static bool IsDeveloperModeEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
            return key?.GetValue("AllowDevelopmentWithoutDevLicense") is int v && v == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
