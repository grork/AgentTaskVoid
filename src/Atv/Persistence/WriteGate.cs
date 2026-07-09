namespace Atv.Persistence;

/// <summary>
/// INFRA-6's global write-mutex wrapper: acquire / bounded-wait / abandoned /
/// release, written exactly once (<see cref="TryAcquire"/>) -- both public
/// overloads funnel through it. Holds the mutex across the WHOLE
/// read-modify-write the caller passes in: the API write AND the sidecar
/// write run as one critical section, never two separate acquisitions.
///
/// Not abstracted (INFRA-8): takes an already-constructed
/// <see cref="System.Threading.Mutex"/> from the composition root --
/// production = a named mutex (<see cref="AppPaths.CurrentWriteMutexName"/>),
/// tests = unnamed/unique (still abandonable in-proc). The watchdog
/// (phase 09) SHARES this same mutex as a supervisor; it must never become a
/// write broker -- mutex-per-invocation stays synchronous (INFRA-6).
///
/// Failure posture (FAIL-1): on a bounded-wait TIMEOUT, non-strict mode logs
/// and returns <see langword="false"/> -- no exception ever escapes, the
/// critical section never runs, the caller skips the write. Strict mode
/// throws <see cref="TimeoutException"/> instead. On
/// <see cref="AbandonedMutexException"/> (a previous holder crashed
/// mid-write): proceed -- no corruption has ever been observed empirically
/// from an abandoned write mutex (INFRA-6) -- and log.
/// </summary>
public sealed class WriteGate
{
    /// <summary>The default bounded-wait budget (~2s, INFRA-6) until phase 06's config supplies a tunable override.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private readonly Mutex _mutex;
    private readonly TimeSpan _timeout;
    private readonly bool _strict;
    private readonly Action<string> _log;

    public WriteGate(Mutex mutex, TimeSpan? timeout = null, bool strict = false, Action<string>? log = null)
    {
        _mutex = mutex ?? throw new ArgumentNullException(nameof(mutex));
        _timeout = timeout ?? DefaultTimeout;
        _strict = strict;
        _log = log ?? (_ => { });
    }

    /// <summary>Runs <paramref name="criticalSection"/> under the mutex. Returns <see langword="false"/> (non-strict timeout) without running it, or <see langword="true"/> once it has completed.</summary>
    public bool TryRun(Action criticalSection)
    {
        ArgumentNullException.ThrowIfNull(criticalSection);
        return TryRun<object?>(() =>
        {
            criticalSection();
            return null;
        }, out _);
    }

    /// <summary>
    /// Runs <paramref name="criticalSection"/> under the mutex and captures
    /// its return value in <paramref name="result"/>. Returns
    /// <see langword="false"/> (non-strict timeout; <paramref name="result"/>
    /// left at its default) if the mutex could not be acquired within the
    /// bounded wait -- <paramref name="criticalSection"/> is never invoked in
    /// that case.
    /// </summary>
    public bool TryRun<T>(Func<T> criticalSection, out T? result)
    {
        ArgumentNullException.ThrowIfNull(criticalSection);
        result = default;

        if (!TryAcquire())
            return false;

        try
        {
            result = criticalSection();
            return true;
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    /// <summary>The one acquire/timeout/abandoned code path every overload above funnels through -- written once, per the phase-04 spec.</summary>
    private bool TryAcquire()
    {
        bool acquired;
        try
        {
            acquired = _mutex.WaitOne(_timeout);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
            _log("WriteGate: acquired a mutex abandoned by a crashed holder; proceeding (no corruption ever observed empirically, INFRA-6).");
        }

        if (!acquired)
        {
            _log($"WriteGate: timed out after {_timeout} waiting for the tasks write mutex.");
            if (_strict)
                throw new TimeoutException($"Timed out after {_timeout} waiting for the tasks write mutex.");
        }

        return acquired;
    }
}
