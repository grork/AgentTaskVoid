using Atv.LogicTests.Persistence;
using Atv.LogicTests.Store;
using Atv.Operations;
using Atv.Persistence;
using Atv.Semantics;

namespace Atv.LogicTests.Run;

/// <summary>
/// Minimal fake-backed rig for phase-11's `Atv.Run` unit tests: a real
/// <see cref="TaskOperations"/> + <see cref="SemanticEngine"/> (phase 15's
/// re-seat -- <see cref="RunOrchestrator"/> now starts/finishes the card via
/// <see cref="Engine"/>, while <c>StepPublisher</c> still writes through
/// <see cref="Ops"/>) over a <see cref="CountingAppTaskStore"/>-wrapped
/// <see cref="FakeAppTaskStore"/> (so a test can assert exactly how many
/// whole-content writes happened) plus temp-dir sidecar/recycle-bin and an
/// unnamed per-instance mutex -- same shape as
/// <c>Atv.LogicTests.Cli.DispatcherHarness</c>, scoped down to what
/// <c>StepPublisher</c>/<c>RunOrchestrator</c> need (no icons/watchdog/doctor
/// wiring).
/// </summary>
internal sealed class RunTestHarness : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _sidecarDir = new();
    private readonly TempDirectory _recycleDir = new();
    private readonly Mutex _mutex = new(initiallyOwned: false);

    public FakeAppTaskStore Fake { get; } = new();
    public CountingAppTaskStore Store { get; }
    public SidecarStore Sidecar { get; }
    public RecycleBin RecycleBin { get; }
    public TaskOperations Ops { get; }
    public SemanticEngine Engine { get; }

    public RunTestHarness()
    {
        Store = new CountingAppTaskStore(Fake);
        Sidecar = new SidecarStore(_sidecarDir.Path);
        RecycleBin = new RecycleBin(_recycleDir.Path);
        var gate = new WriteGate(_mutex);
        Ops = new TaskOperations(Store, Sidecar, RecycleBin, gate, TimeSpan.FromDays(1));
        Engine = new SemanticEngine(Store, Sidecar, RecycleBin, gate, TimeSpan.FromDays(1), Ops);
    }

    /// <summary>Starts a card the way `run` would (bare title/subtitle/deepLink/icon -- content details don't matter to these tests).</summary>
    public void StartCard(string handle, DateTimeOffset now)
        => Engine.Working(handle, title: "Test Card", subtitle: "", new Uri("file:///icon.png"), new Uri("file:///deep-link"), goal: null, now);

    public void Dispose()
    {
        _mutex.Dispose();
        _sidecarDir.Dispose();
        _recycleDir.Dispose();
    }
}
