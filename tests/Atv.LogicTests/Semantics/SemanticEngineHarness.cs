using Atv.Icons;
using Atv.LogicTests.Persistence;
using Atv.LogicTests.Store;
using Atv.Operations;
using Atv.Persistence;
using Atv.Semantics;

namespace Atv.LogicTests.Semantics;

/// <summary>
/// Shared fake-backed test rig for the phase-15 <see cref="SemanticEngine"/>
/// suite -- mirrors <c>Atv.LogicTests.Operations.OperationsHarness</c>'s
/// shape (temp-dir sidecar/recycle-bin, unnamed per-instance mutex), plus an
/// optional real <see cref="IconService"/> for the icon-forced-recreate
/// tests (parallel to phase-07's <c>TaskOperationsIconTests</c> rig).
/// </summary>
internal sealed class SemanticEngineHarness : IDisposable
{
    public static readonly Uri DeepLink = new("https://example.invalid/deep-link");
    public static readonly Uri IconUri = new("ms-appx:///Assets/Square44x44Logo.png");
    public static readonly Uri OtherIconUri = new("ms-appx:///Assets/OtherLogo.png");
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(1);
    public static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _sidecarDir = new();
    private readonly TempDirectory _recycleDir = new();
    private readonly TempDirectory? _iconsDir;
    private readonly Mutex _mutex = new(initiallyOwned: false);

    public FakeAppTaskStore Store { get; } = new();
    public SidecarStore Sidecar { get; }
    public RecycleBin RecycleBin { get; }
    public string RecycleDirPath => _recycleDir.Path;
    public WriteGate Gate { get; }
    public IconService? Icons { get; }
    public List<string> Logs { get; } = [];
    public TaskOperations Ops { get; }
    public SemanticEngine Engine { get; }

    public SemanticEngineHarness(bool withIcons = false, TimeSpan? ttl = null)
    {
        Sidecar = new SidecarStore(_sidecarDir.Path);
        RecycleBin = new RecycleBin(_recycleDir.Path);
        Gate = new WriteGate(_mutex, log: Logs.Add);
        if (withIcons)
        {
            _iconsDir = new TempDirectory();
            Icons = new IconService(_iconsDir.Path, _recycleDir.Path);
        }
        Ops = new TaskOperations(Store, Sidecar, RecycleBin, Gate, ttl ?? Ttl, Logs.Add, Icons);
        Engine = new SemanticEngine(Store, Sidecar, RecycleBin, Gate, ttl ?? Ttl, Ops, Icons, Logs.Add);
    }

    /// <summary>A second <see cref="SemanticEngine"/> sharing this harness's store/sidecar/recycle-bin but wrapping a NEW <see cref="WriteGate"/> around the SAME underlying mutex -- mirrors AC7's cross-verb concurrency test.</summary>
    public SemanticEngine NewEngineOnSameMutex()
    {
        var ops = new TaskOperations(Store, Sidecar, RecycleBin, new WriteGate(_mutex, log: Logs.Add), Ttl, Logs.Add, Icons);
        return new SemanticEngine(Store, Sidecar, RecycleBin, new WriteGate(_mutex, log: Logs.Add), Ttl, ops, Icons, Logs.Add);
    }

    /// <summary>Convenience: `working` a fresh handle with a goal, landing it in Working.</summary>
    public OperationOutcome WorkingNew(string handle, string goal = "goal", string title = "Title", string subtitle = "Subtitle", Uri? iconUri = null, DateTimeOffset? now = null)
        => Engine.Working(handle, title, subtitle, iconUri ?? IconUri, DeepLink, goal, now ?? Now);

    public void Dispose()
    {
        _mutex.Dispose();
        _sidecarDir.Dispose();
        _recycleDir.Dispose();
        _iconsDir?.Dispose();
    }
}
