using System.Text;
using Codevoid.AgentTaskVoid.Run;

namespace Codevoid.AgentTaskVoid.LogicTests.Run;

/// <summary>
/// A tiny in-memory, blocking <see cref="Stream"/> standing in for a real
/// process's redirected pipe: <see cref="Read"/> blocks until either more
/// bytes have been queued or <see cref="Complete"/> has been called (EOF,
/// mirroring <see cref="OutputPump.Pump"/>'s <c>while (Read(...) &gt; 0)</c>
/// loop contract). No external package (avoids pulling in
/// <c>System.IO.Pipelines</c> just for test scaffolding) -- a plain
/// monitor-guarded byte queue is all <see cref="OutputPump"/>'s contract
/// needs.
/// </summary>
internal sealed class BlockingByteStream : Stream
{
    private readonly Queue<byte> _queue = new();
    private readonly object _gate = new();
    private bool _completed;

    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    public void Write(byte[] bytes)
    {
        lock (_gate)
        {
            foreach (byte b in bytes) _queue.Enqueue(b);
            Monitor.PulseAll(_gate);
        }
    }

    public void Complete()
    {
        lock (_gate) { _completed = true; Monitor.PulseAll(_gate); }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            while (_queue.Count == 0 && !_completed)
                Monitor.Wait(_gate);

            int n = 0;
            while (n < count && _queue.Count > 0)
                buffer[offset + n++] = _queue.Dequeue();
            return n;
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// The AC2 "scripted fake child" seam: a fully in-memory <see cref="IChildProcess"/>
/// double, no real process. A test scripts stdout/stderr content, then calls
/// <see cref="Exit"/> to complete both streams and unblock
/// <see cref="WaitForExit"/> with a chosen exit code.
/// </summary>
internal sealed class FakeChildProcess : IChildProcess
{
    private static int s_nextFakeId = 900_000; // Far away from any real PID, deliberately unrealistic-looking.

    private readonly BlockingByteStream _stdout = new();
    private readonly BlockingByteStream _stderr = new();
    private readonly ManualResetEventSlim _exited = new(initialState: false);
    private int _exitCode;

    public int Id { get; } = Interlocked.Increment(ref s_nextFakeId);

    public Stream StandardOutput => _stdout;
    public Stream StandardError => _stderr;

    public bool CancelRequested { get; private set; }
    public TimeSpan? LastGracePeriod { get; private set; }

    public void WriteStdout(string text) => _stdout.Write(text);
    public void WriteStderr(string text) => _stderr.Write(text);

    /// <summary>Completes both streams (EOF) and signals <see cref="WaitForExit"/> to return <paramref name="exitCode"/>.</summary>
    public void Exit(int exitCode)
    {
        _exitCode = exitCode;
        _stdout.Complete();
        _stderr.Complete();
        _exited.Set();
    }

    public int WaitForExit()
    {
        _exited.Wait();
        return _exitCode;
    }

    public void RequestCancel(TimeSpan gracePeriod)
    {
        CancelRequested = true;
        LastGracePeriod = gracePeriod;
    }

    public void Dispose() => _exited.Dispose();
}
