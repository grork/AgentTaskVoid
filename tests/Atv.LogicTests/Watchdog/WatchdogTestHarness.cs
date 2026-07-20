using Codevoid.AgentTaskVoid.Config;
using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.LogicTests.Persistence;
using Codevoid.AgentTaskVoid.LogicTests.Store;
using Codevoid.AgentTaskVoid.Persistence;
using Codevoid.AgentTaskVoid.Watchdog;

namespace Codevoid.AgentTaskVoid.LogicTests.Watchdog;

/// <summary>
/// Shared fake-backed test rig for the phase-09 watchdog suite: a temp-dir
/// sidecar/recycle-bin/icons (same <see cref="TempDirectory"/> seam every
/// other phase's tests use), an unnamed per-instance <see cref="Mutex"/>
/// wrapped in a <see cref="WriteGate"/>, and a REAL <see cref="IconService"/>
/// (phase 07: no fake exists -- <c>Codevoid.AgentTaskVoid.IconRendering</c> has no filesystem/
/// handle/policy surface to fake against). One instance per test method --
/// never shared across tests.
/// </summary>
internal sealed class WatchdogTestHarness : IDisposable
{
    public static readonly Uri DeepLink = new("https://example.invalid/deep-link");
    public static readonly Uri IconUri = new("ms-appx:///Assets/Square44x44Logo.png");
    public static readonly DateTimeOffset Now = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _sidecarDir = new();
    private readonly TempDirectory _recycleDir = new();
    private readonly TempDirectory _iconsDir = new();
    private readonly Mutex _mutex = new(initiallyOwned: false);

    public FakeAppTaskStore Store { get; } = new();
    public SidecarStore Sidecar { get; }
    public RecycleBin RecycleBin { get; }
    public IconService Icons { get; }
    public WriteGate Gate { get; }
    public List<string> Logs { get; } = [];
    public string RecycleDirPath => _recycleDir.Path;

    /// <summary>Mutable so a test can move the clock without rebuilding the harness -- consumed by <see cref="WatchdogDeps.Clock"/> (used by <see cref="WatchdogLoop.Run"/>'s loop; single-tick tests pass <c>now</c> directly to <see cref="WatchdogLoop.RunTick"/> instead).</summary>
    public DateTimeOffset ClockValue { get; set; } = Now;

    public Settings Settings { get; set; } = Settings.Default with
    {
        IdleRunning = TimeSpan.FromMinutes(30),
        IdlePaused = TimeSpan.FromHours(4),
        IdleNeedsAttention = TimeSpan.FromHours(4),
        IdleCompleted = TimeSpan.FromMinutes(10),
        RecycleBinTtl = TimeSpan.FromDays(1),
        WatchdogPollInterval = TimeSpan.FromMilliseconds(1),
        ReadyDecayThreshold = TimeSpan.FromMinutes(20),
    };

    /// <summary>Phase 15B's presence gate (LIFE-24 §6) -- a fake by default (present) so existing hygiene-only tests are unaffected; a decay test overrides <see cref="FakePresenceSource.Present"/> directly.</summary>
    public FakePresenceSource Presence { get; } = new();

    public WatchdogTestHarness()
    {
        Sidecar = new SidecarStore(_sidecarDir.Path);
        RecycleBin = new RecycleBin(_recycleDir.Path);
        Icons = new IconService(_iconsDir.Path, _recycleDir.Path);
        Gate = new WriteGate(_mutex, log: Logs.Add);
    }

    /// <summary>A second <see cref="WriteGate"/> over the SAME underlying mutex -- for single-instance/gate-contention tests.</summary>
    public WriteGate NewGateOnSameMutex() => new(_mutex, log: Logs.Add);

    public WatchdogDeps Deps(bool withIcons = true, WriteGate? gate = null, bool withPresence = true)
        => new(Store, Sidecar, RecycleBin, gate ?? Gate, withIcons ? Icons : null, Logs.Add, () => ClockValue, Settings, withPresence ? Presence : null);

    public void Dispose()
    {
        _mutex.Dispose();
        _sidecarDir.Dispose();
        _recycleDir.Dispose();
        _iconsDir.Dispose();
    }
}
