using Atv.Config;

namespace Atv.Watchdog;

/// <summary>
/// LIFE-17/INFRA-19's decide-to-run logic, replacing phase 08's inert
/// <c>Atv.Cli.WatchdogGate</c> stub: resolve <see cref="WatchdogMode"/>, then
/// a cheap <see cref="Mutex.OpenExisting(string)"/> liveness check IN THE
/// INVOKER (so a busy session's `step` stream never spawns doomed processes),
/// then hand off to the mode-selected <see cref="IWatchdogHost"/>. The
/// spawned/inproc watchdog's own LIFE-18 acquire-or-exit
/// (<see cref="WatchdogLoop.Run"/>) is the correctness backstop for the
/// check-&gt;spawn race -- this gate is a cheap short-circuit, not the source
/// of truth.
///
/// Never throws out to a caller (FAIL-1): every branch is either a clean
/// return or a logged no-op; a host's <see cref="IWatchdogHost.Start"/>
/// throwing is caught and logged here too (LIFE-17: "spawn failure is
/// non-disruptive: log + continue").
/// </summary>
public static class EnsureWatchdog
{
    public static void Run(WatchdogMode mode, string mutexName, IWatchdogHost processHost, IWatchdogHost inProcHost, Action<string> log)
    {
        if (mode == WatchdogMode.Off) return;

        try
        {
            using var existing = Mutex.OpenExisting(mutexName);
            return; // A watchdog already holds the LIFE-18 mutex -- nothing to do.
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // No watchdog currently holds it -- fall through to the real spawn/inproc path.
        }
        catch (UnauthorizedAccessException ex)
        {
            // The mutex exists but this process can't open it -- treat as live
            // rather than risk a redundant/conflicting spawn attempt.
            log($"watchdog: mutex '{mutexName}' exists but is inaccessible ({ex.GetType().Name}) -- treating as live, non-disruptive.");
            return;
        }

        IWatchdogHost host = mode == WatchdogMode.Spawn ? processHost : inProcHost;
        try
        {
            host.Start();
        }
        catch (Exception ex)
        {
            log($"watchdog: failed to start ({mode}): {ex.GetType().Name}: {ex.Message} -- non-disruptive, continuing.");
        }
    }

    /// <summary>
    /// Cheap, standalone liveness probe: <see langword="true"/> if a
    /// watchdog currently holds the LIFE-18 mutex named
    /// <paramref name="mutexName"/>. Phase 10's `doctor` reuses this rather
    /// than re-deriving <see cref="Run"/>'s own internal
    /// <see cref="Mutex.OpenExisting(string)"/> pattern -- purely
    /// informational (doctor never spawns/starts anything), so this never
    /// falls through to a host.
    /// </summary>
    public static bool IsRunning(string mutexName)
    {
        try
        {
            using var existing = Mutex.OpenExisting(mutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Exists but inaccessible -- treat as live (matches Run's own posture).
            return true;
        }
    }
}
