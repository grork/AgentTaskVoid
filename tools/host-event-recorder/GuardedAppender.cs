using System.Text;

namespace HostEventRecorder;

/// <summary>
/// Appends one already-serialized JSON line to a log file under the named
/// mutex <see cref="MutexNaming.DeriveMutexName"/> derives for that file --
/// held only for the append itself, so records structurally cannot tear
/// under parallel hook fan-out (phase-14 Part A "Guarded append" spec).
/// File line order is the authoritative sequence; there is no seq field.
/// </summary>
public static class GuardedAppender
{
    /// <summary>Bounded wait for the mutex. A real timeout here means something is stuck holding the file's mutex far longer than a single small append should ever take -- surfaced as an exception rather than silently dropping the record.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static void Append(string filePath, string jsonLine, TimeSpan? timeout = null)
    {
        string fullPath = Path.GetFullPath(filePath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string mutexName = MutexNaming.DeriveMutexName(fullPath);
        using var mutex = new Mutex(initiallyOwned: false, mutexName);

        bool acquired;
        try
        {
            acquired = mutex.WaitOne(timeout ?? DefaultTimeout);
        }
        catch (AbandonedMutexException)
        {
            // A previous holder crashed mid-append. Proceed -- an abandoned
            // append mutex means at worst one prior line is missing or (if
            // it crashed mid-write, which File.AppendAllText's single OS
            // write call makes vanishingly unlikely) truncated; it never
            // implies this process's own write is unsafe to perform.
            acquired = true;
        }

        if (!acquired)
            throw new TimeoutException($"Timed out after {timeout ?? DefaultTimeout} waiting for the capture-file mutex ('{mutexName}') for '{fullPath}'.");

        try
        {
            File.AppendAllText(fullPath, jsonLine + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
