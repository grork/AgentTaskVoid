namespace Atv.Watchdog;

/// <summary>
/// LIFE-20's boot/crash-recovery half: no task is valid across a reboot, so
/// recovery is an UNCONDITIONAL flat clear, never per-task reasoning.
/// </summary>
public static class BootRecovery
{
    /// <summary>
    /// True when THIS process launch is recognized as a StartupTask
    /// activation (LIFE-20, ratified 2026-07-07: <c>AppInstance.GetActivatedEventArgs()</c>
    /// -&gt; <see cref="Windows.ApplicationModel.Activation.ActivationKind.StartupTask"/>)
    /// -- StartupTask launches carry no CLI args, so this is what
    /// distinguishes an OS-triggered boot-recovery run from an ordinary bare
    /// <c>atv</c> invocation (which prints usage/help). Wrapped defensively:
    /// any failure (no package identity, API absent, called a second time in
    /// the same process -- the platform only returns real args on the FIRST
    /// call) degrades to <see langword="false"/> -- a normal bare invocation
    /// -- never throws (FAIL-1). Untested here (a real WinRT activation
    /// boundary call, matching <c>AppPaths.ForCurrentPackage</c>'s own
    /// untested-here status); <see cref="FlatClear"/>'s own logic is what
    /// gets real unit coverage.
    /// </summary>
    public static bool IsStartupTaskActivation()
    {
        try
        {
            var args = Windows.ApplicationModel.AppInstance.GetActivatedEventArgs();
            return args?.Kind == Windows.ApplicationModel.Activation.ActivationKind.StartupTask;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The unconditional flat clear itself (LIFE-20): Remove every live task
    /// (sidecar-mapped AND entryless), delete every sidecar entry, reap every
    /// live per-handle icon, sweep any icon orphans, and wipe the WHOLE
    /// recycle bin -- tombstone records AND their co-located icon files (the
    /// one internal, non-interactive exception to `clear`'s default
    /// recycle-bin exclusion, ERGO-16/ERGO-27). Runs entirely inside one
    /// <see cref="Atv.Persistence.WriteGate.TryRun{T}"/> critical section,
    /// like every other read-modify-write in this codebase (invariant #5).
    /// Returns the count of live tasks cleared, for the caller's log line;
    /// on a mutex-unavailable tick this is non-disruptively skipped (FAIL-1)
    /// and returns 0 -- the next boot/invocation gets another chance.
    /// </summary>
    public static int FlatClear(WatchdogDeps deps)
    {
        ArgumentNullException.ThrowIfNull(deps);

        int? cleared = null;
        bool ran = deps.WriteGate.TryRun(() => cleared = FlatClearCore(deps));

        if (!ran)
        {
            deps.Log("boot-recovery: could not acquire the write mutex within the bounded wait -- flat clear skipped this pass.");
            return 0;
        }

        deps.Log($"boot-recovery: flat clear removed {cleared} task(s); wiped every icon and the whole recycle bin.");
        return cleared!.Value;
    }

    private static int FlatClearCore(WatchdogDeps deps)
    {
        int cleared = 0;

        foreach (var (handle, entry) in deps.Sidecar.ReadAll())
        {
            deps.Store.Remove(entry.Id);
            deps.Sidecar.Delete(handle);
            deps.Icons?.ReapLiveIcon(handle);
            cleared++;
        }

        // Entryless leftovers too -- a boot clear is unconditional, not sidecar-scoped
        // (unlike LIFE-23's per-tick reap, there is no "safe against the create race"
        // concern here: this whole pass already holds the write mutex).
        foreach (var task in deps.Store.FindAll())
        {
            deps.Store.Remove(task.Id);
            cleared++;
        }

        deps.Icons?.SweepOrphans(liveHandles: [], recycleHandles: []);
        deps.RecycleBin.WipeAll();

        return cleared;
    }
}
