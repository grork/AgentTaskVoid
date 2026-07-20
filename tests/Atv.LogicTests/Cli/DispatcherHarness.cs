using Codevoid.AgentTaskVoid;
using Codevoid.AgentTaskVoid.Cli;
using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Diagnostics;
using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.LogicTests.Persistence;
using Codevoid.AgentTaskVoid.LogicTests.Run;
using Codevoid.AgentTaskVoid.LogicTests.Store;
using Codevoid.AgentTaskVoid.LogicTests.Watchdog;
using Codevoid.AgentTaskVoid.Operations;
using Codevoid.AgentTaskVoid.Persistence;
using Codevoid.AgentTaskVoid.Run;
using Codevoid.AgentTaskVoid.Semantics;
using Codevoid.AgentTaskVoid.Watchdog;

namespace Codevoid.AgentTaskVoid.LogicTests.Cli;

/// <summary>
/// Shared fake-backed rig for the phase-08 verb-level suite (AC1-3): a
/// temp-dir sidecar/recycle-bin/icons (same <see cref="TempDirectory"/> seam
/// every other phase's tests use), an unnamed per-instance <see cref="Mutex"/>,
/// a real <see cref="FailureLog"/> pointed at a temp file, and injectable
/// identity/API-availability delegates -- so the whole CLI pipeline
/// (<see cref="Dispatcher"/>) is exercised with NO package identity and NO
/// real WinRT API, per plan/phase-08's "Verb-level logic testable fake-backed
/// with temp dirs + unnamed mutex" instruction.
/// </summary>
internal sealed class DispatcherHarness : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _sidecarDir = new();
    private readonly TempDirectory _recycleDir = new();
    private readonly TempDirectory _iconsDir = new();
    private readonly TempDirectory _appDataDir = new();
    private readonly Mutex _mutex = new(initiallyOwned: false);

    public FakeAppTaskStore Store { get; } = new();
    public SidecarStore Sidecar { get; }
    public RecycleBin RecycleBin { get; }
    public IconService Icons { get; }
    public TaskOperations Ops { get; }
    public SemanticEngine Engine { get; private set; }
    public FailureLog Log { get; }

    /// <summary>The v2 `-` stdin convention's test seam (ERGO-31, LIFE-24 S2-walk item 1) -- defaults empty; a test that needs a specific piped value sets this before dispatching.</summary>
    public TextReader Stdin { get; set; } = new StringReader("");
    public StringWriter Stdout { get; } = new();
    public StringWriter Stderr { get; } = new();
    public List<string> WatchdogLogs { get; } = [];
    public string AppDataRoot => _appDataDir.Path;

    /// <summary>The canonical icon render-once cache directory (<c>Icons/cache</c>, ERGO-23) -- for asserting `clear` never touches it.</summary>
    public string IconsCacheDir => Path.Combine(_iconsDir.Path, "cache");

    /// <summary>Backing field for the <see cref="Dispatcher"/>'s injected identity check -- flip to simulate AC3's "missing platform" scenarios.</summary>
    public bool HasIdentity { get; set; } = true;

    /// <summary>A per-instance, never-created mutex name -- <see cref="EnsureWatchdog.Run"/> always takes the "not running" branch unless a test explicitly creates it.</summary>
    public string WatchdogMutexName { get; } = $@"Local\atv-tests-{Guid.NewGuid():N}-watchdog";

    /// <summary>The fake host <see cref="EnsureWatchdog.Run"/> selects for <see cref="Config.WatchdogMode.Spawn"/> -- never a real process in this fake-backed suite.</summary>
    public FakeWatchdogHost ProcessHost { get; } = new();

    /// <summary>The fake host <see cref="EnsureWatchdog.Run"/> selects for <see cref="Config.WatchdogMode.InProc"/> -- never a real thread in this fake-backed suite.</summary>
    public FakeWatchdogHost InProcHost { get; } = new();

    /// <summary>Backing field for <c>doctor</c>'s identity probe -- independent of <see cref="HasIdentity"/> (which gates lifecycle verbs/list/clear via <see cref="Diagnostics.Capability"/>) so doctor tests can flip identity presence without touching the rest of the pipeline.</summary>
    public string? DoctorPackageFullName { get; set; } = "Codevoid.AgentTaskVoid-test_1.0.0.0_neutral";

    /// <summary>
    /// Backing field for <c>doctor</c>'s DIST-3 build-kind probe
    /// (<see cref="Diagnostics.DoctorProbes.PackageName"/>) -- the declared
    /// Identity Name (distinct from <see cref="DoctorPackageFullName"/>'s
    /// PFN). Defaults to a dev-shaped name (<c>"&lt;brand&gt;-testhash"</c>,
    /// resolving to <see cref="Diagnostics.BuildKind.Dev"/>) so the default
    /// harness exercises the <c>(dev)</c> marker path unless a test
    /// overrides it to a clean <see cref="Branding.IdentityName"/> (Release, no
    /// marker) or a <c>&lt;brand&gt;.Test.*</c> name (Test).
    /// </summary>
    public string? DoctorPackageName { get; set; } = $"{Branding.IdentityName}-testhash";

    /// <summary>Backing field for <c>doctor</c>'s dev-facing Developer Mode probe.</summary>
    public bool DoctorDeveloperModeEnabled { get; set; } = true;

    /// <summary>
    /// ERGO-30 (phase 17) repo-defaults wiring: unset (<see langword="null"/>)
    /// by default, so every pre-existing test using this harness keeps
    /// exercising zero repo-file access (matches <see cref="SemanticEngine"/>'s
    /// own degradation). A test that needs repo-defaults support sets this
    /// BEFORE calling <see cref="BuildDispatcher"/> (the engine is constructed
    /// there, not lazily).
    /// </summary>
    public Func<RepoDiscoveryResult>? DiscoverRepo { get; set; }

    /// <summary>Backing field for <c>doctor</c>'s watchdog-liveness probe.</summary>
    public bool DoctorWatchdogRunning { get; set; }

    /// <summary>Phase-11 `run` tunables, deliberately fast (not the ~2s/5min production defaults) so a Dispatcher-level `run` test doesn't sit through a real debounce/keepalive interval.</summary>
    public Settings Settings { get; set; } = Settings.Default with
    {
        RunUpdateDebounce = TimeSpan.FromMilliseconds(20),
        RunKeepAliveInterval = TimeSpan.FromMilliseconds(100),
    };

    /// <summary>Mutable fake wall clock for `run`'s step-publisher loop -- <see cref="Now"/> by default, advance explicitly for keepalive-timing tests.</summary>
    public DateTimeOffset ClockNow { get; set; } = Now;

    /// <summary>The last <see cref="FakeChildProcess"/> the harness's default <c>SpawnChild</c> factory created -- the AC2 seam a test grabs to script output/exit.</summary>
    public FakeChildProcess? LastSpawnedChild { get; private set; }

    /// <summary>
    /// Optional synchronous scripting hook invoked immediately on a freshly
    /// created <see cref="FakeChildProcess"/>, BEFORE it is handed back to
    /// <c>RunVerb.Run</c> -- lets a test call <c>Exit(...)</c> up front
    /// (Dispatcher's own thread is the one that will block on
    /// <c>WaitForExit</c>) so a full `run` dispatch through
    /// <see cref="Run(Dispatcher, string[])"/> stays single-threaded and
    /// deterministic instead of racing a background "script the child" step
    /// against the blocking wait.
    /// </summary>
    public Action<FakeChildProcess>? ScriptChild { get; set; }

    /// <summary>The exact child argv the `run` verb resolved (everything after `--`, verbatim) on the last spawn.</summary>
    public IReadOnlyList<string>? LastSpawnArgs { get; private set; }

    /// <summary>Raw byte sinks `run` mirrors the fake child's stdout/stderr to -- a test reads these back to assert byte-for-byte transparency.</summary>
    public MemoryStream StdoutMirror { get; } = new();
    public MemoryStream StderrMirror { get; } = new();

    private readonly WriteGate _gate;

    public DispatcherHarness()
    {
        Sidecar = new SidecarStore(_sidecarDir.Path);
        RecycleBin = new RecycleBin(_recycleDir.Path);
        Icons = new IconService(_iconsDir.Path, _recycleDir.Path);
        _gate = new WriteGate(_mutex);
        Ops = new TaskOperations(Store, Sidecar, RecycleBin, _gate, TimeSpan.FromDays(1), icons: Icons);
        // Built fresh in BuildDispatcher() (not here) so a test can set
        // DiscoverRepo AFTER constructing the harness but BEFORE building the
        // dispatcher -- Engine itself is stateless composition over the fields
        // above, so rebuilding it is free of any state loss.
        Engine = BuildEngine();
        Log = new FailureLog(Path.Combine(_appDataDir.Path, "atv.log"), maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));
    }

    private SemanticEngine BuildEngine() => new(Store, Sidecar, RecycleBin, _gate, TimeSpan.FromDays(1), Ops, icons: Icons, discoverRepo: DiscoverRepo);

    public Dispatcher BuildDispatcher(bool json = false, bool strict = false, bool verbose = false, WatchdogMode watchdogMode = WatchdogMode.Off)
    {
        Engine = BuildEngine();
        var output = new Output(Stdout, Stderr, json);
        var posture = new Posture(Log, output, strict, verbose);
        Action ensureWatchdog = () => EnsureWatchdog.Run(watchdogMode, WatchdogMutexName, ProcessHost, InProcHost, WatchdogLogs.Add);
        var doctorProbes = new DoctorProbes(
            PackageFullName: () => DoctorPackageFullName,
            ApiSupported: () => Store.IsSupported(),
            DeveloperModeEnabled: () => DoctorDeveloperModeEnabled,
            WatchdogRunning: () => DoctorWatchdogRunning,
            PackageName: () => DoctorPackageName);
        var doctorContext = new DoctorContext(
            doctorProbes,
            ConfigPath: Path.Combine(_appDataDir.Path, "atv-config.json"),
            AppDataFolder: _appDataDir.Path,
            SidecarDir: _sidecarDir.Path,
            LogPath: Path.Combine(_appDataDir.Path, "atv.log"),
            DiscoverRepo: DiscoverRepo);
        return new Dispatcher(
            Ops,
            Engine,
            posture,
            output,
            Icons,
            defaultDeepLink: new Uri(_appDataDir.Path),
            hasIdentity: () => HasIdentity,
            isSupported: () => Store.IsSupported(),
            ensureWatchdog: ensureWatchdog,
            doctorContext: doctorContext,
            settings: Settings,
            clock: () => ClockNow,
            sleep: _ => Thread.Sleep(1),
            spawnChild: args =>
            {
                LastSpawnArgs = args;
                var child = new FakeChildProcess();
                LastSpawnedChild = child;
                ScriptChild?.Invoke(child);
                return child;
            },
            stdoutMirror: StdoutMirror,
            stderrMirror: StderrMirror,
            stdin: Stdin,
            traceIn: msg => Log.Append("trace-in", null, msg, ClockNow),
            traceOut: msg => Log.Append("trace-out", null, msg, ClockNow));
    }

    public int Run(Dispatcher dispatcher, params string[] args) => dispatcher.Run(CommandLine.Parse(args), Now);

    /// <summary>
    /// Every durable log entry EXCLUDING AC11's always-on "trace-in"/
    /// "trace-out" diagnostic lines (src/Atv/Cli/Dispatcher.cs) -- those fire
    /// unconditionally on every <see cref="Dispatcher.Run"/> call, additive
    /// to (never a replacement for) the verb-outcome-level entries
    /// (Posture/"ops"/"engine"/etc.) the pre-existing exact-count assertions
    /// below were written against.
    /// </summary>
    public IReadOnlyList<LogEntry> LogEntriesExcludingTrace()
        => [.. Log.ReadAll().Where(e => e.Verb is not "trace-in" and not "trace-out")];

    public void Dispose()
    {
        _mutex.Dispose();
        _sidecarDir.Dispose();
        _recycleDir.Dispose();
        _iconsDir.Dispose();
        _appDataDir.Dispose();
    }
}
