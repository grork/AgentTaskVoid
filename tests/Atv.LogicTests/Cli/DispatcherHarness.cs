using Atv.Cli;
using Atv.Config;
using Atv.Diagnostics;
using Atv.Icons;
using Atv.LogicTests.Persistence;
using Atv.LogicTests.Store;
using Atv.Operations;
using Atv.Persistence;

namespace Atv.LogicTests.Cli;

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
    public FailureLog Log { get; }
    public StringWriter Stdout { get; } = new();
    public StringWriter Stderr { get; } = new();
    public List<string> WatchdogLogs { get; } = [];
    public string AppDataRoot => _appDataDir.Path;

    /// <summary>Backing field for the <see cref="Dispatcher"/>'s injected identity check -- flip to simulate AC3's "missing platform" scenarios.</summary>
    public bool HasIdentity { get; set; } = true;

    /// <summary>A per-instance, never-created mutex name -- <see cref="WatchdogGate.Ensure"/> always takes the "not running" branch unless a test explicitly creates it.</summary>
    public string WatchdogMutexName { get; } = $@"Local\atv-tests-{Guid.NewGuid():N}-watchdog";

    public DispatcherHarness()
    {
        Sidecar = new SidecarStore(_sidecarDir.Path);
        RecycleBin = new RecycleBin(_recycleDir.Path);
        Icons = new IconService(_iconsDir.Path, _recycleDir.Path);
        var gate = new WriteGate(_mutex);
        Ops = new TaskOperations(Store, Sidecar, RecycleBin, gate, TimeSpan.FromDays(1), icons: Icons);
        Log = new FailureLog(Path.Combine(_appDataDir.Path, "atv.log"), maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));
    }

    public Dispatcher BuildDispatcher(bool json = false, bool strict = false, bool verbose = false, WatchdogMode watchdogMode = WatchdogMode.Off)
    {
        var output = new Output(Stdout, Stderr, json);
        var posture = new Posture(Log, output, strict, verbose);
        return new Dispatcher(
            Ops,
            posture,
            Icons,
            defaultDeepLink: new Uri(_appDataDir.Path),
            hasIdentity: () => HasIdentity,
            isSupported: () => Store.IsSupported(),
            watchdogMode: watchdogMode,
            watchdogMutexName: WatchdogMutexName,
            watchdogLog: WatchdogLogs.Add);
    }

    public int Run(Dispatcher dispatcher, params string[] args) => dispatcher.Run(CommandLine.Parse(args), Now);

    public void Dispose()
    {
        _mutex.Dispose();
        _sidecarDir.Dispose();
        _recycleDir.Dispose();
        _iconsDir.Dispose();
        _appDataDir.Dispose();
    }
}
