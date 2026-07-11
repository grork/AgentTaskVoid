using Atv.Diagnostics;
using Atv.Operations;

namespace Atv.Cli.Verbs;

/// <summary>
/// `clear [--include-recycle-bin]` (ERGO-16 amended by ERGO-27 C4): purges
/// EVERY active handle for this identity immediately -- no confirmation
/// prompt, no `--all` gate (explicit-intent-over-magic: "they invoked it, do
/// it"). A write-path verb (ensures a watchdog is live, LIFE-17), so unlike
/// <see cref="ListVerb"/>/<see cref="DoctorVerb"/> this reuses
/// <see cref="Posture.Run"/> (not <see cref="Posture.RunQuery"/>) -- `clear`'s
/// `--json` shape IS the standard mutating-verb {"ok":..,"reason":..}
/// (FAIL-2/ERGO-27 C5), no special shape to protect.
/// </summary>
public static class ClearVerb
{
    public static int Run(
        Posture posture,
        Func<bool> hasIdentity,
        Func<bool> isSupported,
        Action ensureWatchdog,
        TaskOperations ops,
        bool includeRecycleBin,
        DateTimeOffset now)
    {
        return posture.Run("clear", null, () =>
        {
            var cap = Capability.Check(hasIdentity, isSupported);
            if (!cap.Ok) return cap;

            ensureWatchdog();

            TaskOperations.ClearSummary summary = ops.ClearAll(includeRecycleBin);
            if (!summary.GateAcquired)
            {
                return VerbResult.Failure(FailureKind.Generic,
                    "Could not acquire the write mutex within the bounded wait; skipped non-disruptively.");
            }

            string reason = includeRecycleBin
                ? $"Cleared {summary.TasksRemoved} task(s); wiped {summary.RecycleRecordsRemoved} recycle-bin file(s)."
                : $"Cleared {summary.TasksRemoved} task(s). Recycle bin untouched (pass --include-recycle-bin to also wipe it).";
            return VerbResult.Success(reason);
        }, now);
    }
}
