using Atv.Diagnostics;

namespace Atv.Cli.Verbs;

/// <summary>
/// `doctor [--json] [--verbose]` (FAIL-3; INFRA-13; INFRA-17; DIST-4;
/// ERGO-26): the self-diagnosis verb answering "why is nothing on my
/// taskbar?". Deliberately bypasses <see cref="Capability.Check"/> --
/// diagnosing an absent identity/API IS the point, so every check in
/// <see cref="DoctorChecks.Run"/> always runs to completion regardless of
/// platform state. Not a write-path verb -- no <c>ensureWatchdog</c> call.
/// Uses <see cref="Posture.RunQuery"/>, not <see cref="Posture.Run"/>:
/// `doctor`'s `--json` shape is its own structured <see cref="DoctorReport"/>
/// (ERGO-27 C5), not the generic mutating-verb {"ok":..,"reason":..} shape,
/// so the blanket <see cref="Output.MutatingResult"/> stamp must be
/// suppressed.
/// </summary>
public static class DoctorVerb
{
    public static int Run(Output output, Posture posture, DoctorContext context, DateTimeOffset now)
    {
        return posture.RunQuery("doctor", null, () =>
        {
            DoctorReport report = DoctorChecks.Run(context);

            if (output.Json)
                output.WriteJson(report, DoctorJsonContext.Default.DoctorReport);
            else
                foreach (string line in FormatHuman(report))
                    output.Data(line);

            return DoctorChecks.ToVerbResult(report);
        }, now);
    }

    private static IEnumerable<string> FormatHuman(DoctorReport r)
    {
        string markerSuffix = r.BuildKindMarker is null ? "" : $" {r.BuildKindMarker}";
        yield return r.IdentityPresent
            ? $"identity: present (PFN: {r.PackageFullName}){markerSuffix}"
            : "identity: NOT present -- this process is unpackaged/unregistered.";

        yield return r.ApiSupported
            ? "api: AppTaskInfo.IsSupported() -> true"
            : "api: AppTaskInfo.IsSupported() -> false (or unavailable on this build)";

        yield return r.DeveloperModeEnabled
            ? "developer mode (dev-facing): enabled"
            : "developer mode (dev-facing): disabled -- only relevant for the loose-layout dev/test loop (dotnet run/F5), not a real end-user machine.";

        yield return r.WatchdogRunning
            ? "watchdog: a supervisor currently holds the liveness mutex"
            : "watchdog: no supervisor currently running (informational -- normal if no task has been started/updated recently).";

        yield return $"config file: {r.ConfigPath}";
        yield return $"app-data folder: {r.AppDataFolder} (the durable log, sidecar index, and the platform's own tasks.json all live under here)";
        yield return $"sidecar dir: {r.SidecarDir}";
        yield return $"log file: {r.LogPath}";

        if (r.RepoAnchorPath is not null)
        {
            yield return $"repo config anchor: {r.RepoAnchorPath} (source: {r.RepoAnchorSource})";
            yield return r.RepoConfigPath is not null
                ? $"repo config: {r.RepoConfigPath} ({r.RepoConfigParseStatus})"
                : $"repo config: none, searched up to '{r.RepoSearchedUpTo}'";
        }

        if (r.Remedy is not null)
            yield return r.Remedy;
    }
}
