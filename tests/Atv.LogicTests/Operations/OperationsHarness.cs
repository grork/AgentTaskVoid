using Atv.LogicTests.Persistence;
using Atv.LogicTests.Store;
using Atv.Operations;
using Atv.Persistence;

namespace Atv.LogicTests.Operations;

/// <summary>
/// Shared fake-backed test rig for the phase-05 operations suite: a temp-dir
/// sidecar + recycle bin (the same <see cref="TempDirectory"/> seam phase 04's
/// own tests use) and an unnamed per-instance <see cref="Mutex"/> wrapped in a
/// <see cref="WriteGate"/>, wired into one <see cref="TaskOperations"/>. One
/// instance per test method -- never shared across tests.
/// </summary>
internal sealed class OperationsHarness : IDisposable
{
    public static readonly Uri DeepLink = new("https://example.invalid/deep-link");
    public static readonly Uri IconUri = new("ms-appx:///Assets/Square44x44Logo.png");
    public static readonly Uri OtherIconUri = new("ms-appx:///Assets/OtherLogo.png");
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(1);
    public static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _sidecarDir = new();
    private readonly TempDirectory _recycleDir = new();
    private readonly Mutex _mutex = new(initiallyOwned: false);

    public FakeAppTaskStore Store { get; } = new();
    public SidecarStore Sidecar { get; }
    public RecycleBin RecycleBin { get; }
    public WriteGate Gate { get; }
    public List<string> Logs { get; } = [];
    public TaskOperations Ops { get; }

    public OperationsHarness(TimeSpan? ttl = null)
    {
        Sidecar = new SidecarStore(_sidecarDir.Path);
        RecycleBin = new RecycleBin(_recycleDir.Path);
        Gate = new WriteGate(_mutex, log: Logs.Add);
        Ops = new TaskOperations(Store, Sidecar, RecycleBin, Gate, ttl ?? Ttl, Logs.Add);
    }

    /// <summary>A second <see cref="TaskOperations"/> sharing this harness's store/sidecar/recycle-bin but wrapping a NEW <see cref="WriteGate"/> around the SAME underlying mutex -- for AC7's concurrency test, mirroring two independent CLI invocations racing on the one named production mutex.</summary>
    public TaskOperations NewOpsOnSameMutex() => new(Store, Sidecar, RecycleBin, new WriteGate(_mutex, log: Logs.Add), Ttl, Logs.Add);

    /// <summary>Convenience: starts a fresh handle and returns its outcome, using the harness's default fields.</summary>
    public OperationOutcome StartNew(string handle, string title = "Title", string subtitle = "Subtitle", Uri? iconUri = null, DateTimeOffset? now = null)
        => Ops.Start(handle, title, subtitle, iconUri ?? IconUri, DeepLink, now ?? Now);

    public void Dispose()
    {
        _mutex.Dispose();
        _sidecarDir.Dispose();
        _recycleDir.Dispose();
    }
}
