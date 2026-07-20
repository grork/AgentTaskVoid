namespace Codevoid.AgentTaskVoid.Watchdog;

/// <summary>
/// LIFE-20's real WinRT half of the boot-recovery startup-item toggle: thin,
/// synchronous wrappers over <see cref="Windows.ApplicationModel.StartupTask"/>
/// (bridging its <c>IAsyncOperation</c> surface with
/// <c>.AsTask().GetAwaiter().GetResult()</c>, since <see cref="WatchdogLoop.Run"/>
/// is a synchronous blocking loop). <see cref="TaskId"/> must match the
/// <c>desktop:StartupTask TaskId</c> attribute declared in
/// <c>src/Atv/Package/AppxManifest.template.xml</c> -- a hand-authored parallel
/// literal, same DIST-7 precedent as that template's other static fields
/// (not brand/PFN-derived, so plan/README.md standing invariant #2/#3 do not
/// apply to this particular internal identifier).
///
/// Both methods are non-disruptive by design (FAIL-1): the user may have
/// disabled the startup task in Task Manager (LIFE-20: "degrades to 'accept
/// the gap'"), or the API may be absent -- either way this swallows and
/// returns silently, never throwing into <see cref="WatchdogLoop.Run"/>'s
/// caller. Untested here (matches <c>AppPaths.ForCurrentPackage</c>'s own
/// untested-here status) -- the STATE MACHINE that decides WHEN to call these
/// (once on loop start, once on clean exit, never on a startup-race loser)
/// is what <c>tests/Atv.LogicTests/Watchdog/WatchdogLoopRunTests.cs</c>
/// actually covers, via injected fake delegates.
/// </summary>
public static class StartupTaskControl
{
    public const string TaskId = "CodevoidAgentTaskVoidBootRecovery";

    public static void EnableSync()
    {
        try
        {
            var task = Windows.ApplicationModel.StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult();
            task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Non-disruptive (FAIL-1) -- see type-level remarks.
        }
    }

    public static void DisableSync()
    {
        try
        {
            var task = Windows.ApplicationModel.StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult();
            task.Disable();
        }
        catch (Exception)
        {
            // Non-disruptive (FAIL-1) -- see type-level remarks.
        }
    }
}
