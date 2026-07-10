using Atv.Config;

namespace Atv.Cli;

/// <summary>
/// LIFE-17/INFRA-19's mode-gated watchdog-liveness check, invoked by every
/// write-path lifecycle verb before it executes its operation. THIS PHASE:
/// the gate and the cheap <see cref="Mutex.OpenExisting(string)"/> liveness
/// probe against the LIFE-18 named watchdog mutex are real; the actual
/// spawn/inproc HOSTS are stubbed/no-op -- phase 09 supplies
/// <c>ProcessHost</c>/<c>InProcThreadHost</c> behind this same seam. Never
/// throws out to a caller (FAIL-1): every branch is either a clean return or
/// a logged no-op.
/// </summary>
public static class WatchdogGate
{
    public static void Ensure(WatchdogMode mode, string mutexName, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrEmpty(mutexName);
        ArgumentNullException.ThrowIfNull(log);

        if (mode == WatchdogMode.Off) return;

        try
        {
            using var existing = Mutex.OpenExisting(mutexName);
            return; // A watchdog already holds the LIFE-18 mutex -- nothing to do.
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // No watchdog currently holds it -- fall through to the (stubbed) spawn path.
        }
        catch (UnauthorizedAccessException ex)
        {
            // The mutex exists but this process can't open it -- treat as live
            // rather than risk a redundant/conflicting spawn attempt.
            log($"watchdog: mutex '{mutexName}' exists but is inaccessible ({ex.GetType().Name}) -- treating as live, non-disruptive.");
            return;
        }

        // Phase 09 supplies the real ProcessHost (spawn) / InProcThreadHost
        // (inproc) behind this gate; this phase only proves the gate + the
        // liveness probe are wired in and inert.
        log($"watchdog: no live watchdog detected for mode {mode} -- host not implemented until phase 09, no-op.");
    }
}
