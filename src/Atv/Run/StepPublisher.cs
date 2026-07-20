using Codevoid.AgentTaskVoid.Operations;

namespace Codevoid.AgentTaskVoid.Run;

/// <summary>
/// ERGO-5's "decoupled updater" half: owns a 10-line in-memory rolling
/// buffer (<see cref="Ingest"/>, called from the stdout/stderr reader
/// threads -- cheap, lock-protected, no I/O) and a debounced publish tick
/// (<see cref="Tick"/>, called from its own loop thread) that, each time it
/// fires, writes the WHOLE buffer as the step list via
/// <see cref="TaskOperations.ReplaceSteps"/> if it changed since the last
/// tick (coalescing any burst -- 100 <see cref="Ingest"/> calls between two
/// ticks still produce exactly ONE write), or -- if nothing changed and the
/// silent stretch has passed the keepalive interval -- refreshes
/// <c>lastUpdate</c> with NO content write via
/// <see cref="TaskOperations.TouchKeepAlive"/> (the LIFE-22 silent-child
/// keepalive). <see cref="Tick"/> is pure w.r.t. its own state (mirrors
/// <see cref="Codevoid.AgentTaskVoid.Watchdog.WatchdogLoop.RunTick"/>'s split from its own
/// <c>Run</c> loop) -- directly unit-testable with a caller-driven clock, no
/// thread/timer required.
/// </summary>
public sealed class StepPublisher
{
    /// <summary>ERGO-5's "10-line in-memory rolling buffer" -- the wrapper's own cap, independent of (but numerically matching) <see cref="AdvanceModel.MaxCompletedSteps"/>.</summary>
    public const int Capacity = 10;

    private readonly object _gate = new();
    private readonly List<string> _buffer = [];
    private readonly int _capacity;
    private readonly TaskOperations _ops;
    private readonly string _handle;
    private readonly TimeSpan _keepAliveInterval;

    private long _revision;
    private long _publishedRevision = -1;
    private DateTimeOffset _lastWriteTime;

    public StepPublisher(TaskOperations ops, string handle, TimeSpan keepAliveInterval, DateTimeOffset startNow, int capacity = Capacity)
    {
        _ops = ops;
        _handle = handle;
        _keepAliveInterval = keepAliveInterval;
        _capacity = capacity;
        _lastWriteTime = startNow;
    }

    /// <summary>Appends one already-<see cref="LineHygiene"/>-cleaned, non-blank line to the rolling buffer. O(1), touches no I/O -- safe to call concurrently from both the stdout and stderr reader threads.</summary>
    public void Ingest(string line)
    {
        lock (_gate)
        {
            _buffer.Add(line);
            if (_buffer.Count > _capacity)
                _buffer.RemoveAt(0);
            _revision++;
        }
    }

    /// <summary>One debounce tick: a changed buffer since the last publish gets ONE whole-buffer <see cref="TaskOperations.ReplaceSteps"/> write; an unchanged buffer gets a keepalive touch only once <paramref name="now"/> has passed the keepalive interval since the last write of either kind.</summary>
    public void Tick(DateTimeOffset now)
    {
        string[] snapshot;
        bool changed;
        lock (_gate)
        {
            changed = _revision != _publishedRevision;
            snapshot = [.. _buffer];
            if (changed) _publishedRevision = _revision;
        }

        if (changed)
        {
            _ops.ReplaceSteps(_handle, snapshot, now);
            _lastWriteTime = now;
            return;
        }

        if (now - _lastWriteTime >= _keepAliveInterval)
        {
            _ops.TouchKeepAlive(_handle, now);
            _lastWriteTime = now;
        }
    }

    /// <summary>Unconditional final flush (bypasses the "changed since last tick" gate) -- called once after the child has fully exited and both reader threads have drained, so the last few lines that arrived inside an in-flight debounce window are never lost before <c>done</c>/<c>fail</c>.</summary>
    public void FlushFinal(DateTimeOffset now)
    {
        string[] snapshot;
        lock (_gate) { snapshot = [.. _buffer]; }
        if (snapshot.Length > 0)
            _ops.ReplaceSteps(_handle, snapshot, now);
    }

    /// <summary>
    /// Production continuous loop: sleeps <paramref name="interval"/>, then
    /// ticks, until <paramref name="shouldStop"/> reports true -- same
    /// test-controllable shape as <see cref="Codevoid.AgentTaskVoid.Watchdog.WatchdogLoop.Run"/>
    /// (<paramref name="sleep"/>/<paramref name="shouldStop"/> injected so a
    /// test can bound and fast-forward an otherwise-infinite loop
    /// deterministically).
    /// </summary>
    public void RunLoop(Func<DateTimeOffset> clock, Action<TimeSpan> sleep, TimeSpan interval, Func<bool> shouldStop)
    {
        while (!shouldStop())
        {
            sleep(interval);
            if (shouldStop()) break;
            Tick(clock());
        }
    }
}
