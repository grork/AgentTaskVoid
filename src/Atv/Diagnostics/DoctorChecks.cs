using System.Text.Json.Serialization;
using Codevoid.AgentTaskVoid.Config;

namespace Codevoid.AgentTaskVoid.Diagnostics;

/// <summary>
/// Injected probes for every real-OS/platform signal <c>doctor</c> reports
/// (INFRA-8-style seam, matching the rest of this codebase): identity
/// presence, API availability, Developer Mode, and watchdog liveness. Each
/// is a plain delegate so <see cref="DoctorChecks.Run"/> is unit-testable
/// with zero package identity, zero real WinRT API, and zero registry/mutex
/// access -- exactly the "identity absent/present, API absent/present,
/// dev-mode on/off" matrix plan/phase-10-utility-verbs.md calls for.
/// </summary>
public sealed record DoctorProbes(
    /// <summary>Mirrors <c>Package.Current.Id.FullName</c> (the PFN) -- <see langword="null"/> when this process has no package identity.</summary>
    Func<string?> PackageFullName,
    /// <summary>Mirrors <see cref="Codevoid.AgentTaskVoid.Store.IAppTaskStore.IsSupported"/> (already wrapped for <c>CLASS_E_CLASSNOTAVAILABLE</c>, INFRA-13) -- this seam takes the RESULT as a delegate, never importing <c>Windows.UI.Shell.Tasks</c> itself (plan/README.md standing invariant #7).</summary>
    Func<bool> ApiSupported,
    /// <summary>Windows Developer Mode (dev-facing only: loose-layout dev/test loop registration, INFRA-17 -- irrelevant to a properly-installed release package).</summary>
    Func<bool> DeveloperModeEnabled,
    /// <summary>LIFE-18 watchdog liveness (informational only) -- see <see cref="Codevoid.AgentTaskVoid.Watchdog.EnsureWatchdog.IsRunning"/>.</summary>
    Func<bool> WatchdogRunning,
    /// <summary>
    /// DIST-3's 2026-07-10 amendment: mirrors <c>Package.Current.Id.Name</c> (the
    /// declared Identity Name -- distinct from <see cref="PackageFullName"/>/the PFN,
    /// which also folds in the publisher hash), fed to <see cref="BuildKindResolver"/>
    /// to produce <see cref="DoctorReport.BuildKindMarker"/>. Optional/trailing with a
    /// <see langword="null"/> default so existing callers that predate this probe keep
    /// compiling unchanged; a <see langword="null"/> probe (or one returning
    /// <see langword="null"/>) resolves to <see cref="BuildKind.NoIdentity"/> --
    /// no marker, documented, matches "no identity -> no build-kind info to show."
    /// </summary>
    Func<string?>? PackageName = null);

/// <summary>Everything <c>doctor</c> needs beyond the four probes above: the ERGO-26 paths to surface, plus (phase 17) the ERGO-30 repo-defaults discovery delegate. Bundled so <see cref="Codevoid.AgentTaskVoid.Cli.Verbs.DoctorVerb"/> takes one parameter instead of several.</summary>
public sealed record DoctorContext(
    DoctorProbes Probes,
    string ConfigPath,
    string AppDataFolder,
    string SidecarDir,
    string LogPath,
    /// <summary>
    /// ERGO-30's anti-"silent sea of robots" observability (AC7): the SAME
    /// discovery delegate <see cref="Codevoid.AgentTaskVoid.Semantics.SemanticEngine"/> uses on
    /// its create branch, invoked HERE unconditionally -- `doctor` is a pure
    /// diagnostic verb, never gated by AC3's create-only rule (that rule is
    /// about upserting verbs' hot path, not about diagnosing why a hook's
    /// repo config isn't taking effect). <see langword="null"/> for a caller
    /// that never wired repo support (existing pre-phase-17 tests) -- doctor
    /// then simply omits the repo-config section.
    /// </summary>
    Func<RepoDiscoveryResult>? DiscoverRepo = null);

/// <summary>
/// <c>doctor</c>'s structured report (ERGO-27 C5's stable `--json` shape).
/// <see cref="Remedy"/> is non-null exactly when nothing is
/// installed/registered (<see cref="IdentityPresent"/> is
/// <see langword="false"/>) -- DIST-4's one-line
/// <c>winget install &lt;package-id&gt;</c> line, never a silent self-install.
/// </summary>
public sealed record DoctorReport(
    bool IdentityPresent,
    string? PackageFullName,
    bool ApiSupported,
    bool DeveloperModeEnabled,
    bool WatchdogRunning,
    string ConfigPath,
    string AppDataFolder,
    string SidecarDir,
    string LogPath,
    string? Remedy,
    /// <summary>
    /// DIST-3's 2026-07-10 amendment: the unambiguous <c>(dev)</c>/<c>(test)</c>
    /// console/log marker (<see cref="BuildKindResolver"/>) -- <see langword="null"/>
    /// for a Release build (deliberately unmarked ship output) or when no package
    /// identity Name was available to classify.
    /// </summary>
    string? BuildKindMarker = null,
    /// <summary>ERGO-30 AC7: the resolved anchor directory (<c>--cwd</c> or the process's own cwd), <see langword="null"/> only when <see cref="DoctorContext.DiscoverRepo"/> itself was never wired.</summary>
    string? RepoAnchorPath = null,
    /// <summary><c>"--cwd"</c> or <c>"process cwd"</c> -- which of the two supplied <see cref="RepoAnchorPath"/>.</summary>
    string? RepoAnchorSource = null,
    /// <summary>The <c>.atv.json</c> path found by the anchor-rooted walk, or <see langword="null"/> when none was found (see <see cref="RepoSearchedUpTo"/> for how far the search went).</summary>
    string? RepoConfigPath = null,
    /// <summary><c>"not-found"</c> / <c>"ok"</c> / <c>"malformed"</c> / <c>"too-large"</c> -- a deliberately malformed repo file is a one-look diagnosis here (AC7).</summary>
    string? RepoConfigParseStatus = null,
    /// <summary>The last directory the discovery walk actually checked (a <c>.git</c> boundary or the filesystem root) -- what "none, searched up to &lt;root&gt;" refers to.</summary>
    string? RepoSearchedUpTo = null);

/// <summary>
/// The individual, injected-probe-driven checks behind `doctor`
/// (FAIL-3/INFRA-13/INFRA-17/DIST-4/ERGO-26) -- pure data production, no
/// stdout/exit-code work (that is <see cref="Codevoid.AgentTaskVoid.Cli.Verbs.DoctorVerb"/>'s
/// job, matching every other verb's split between operation core and CLI
/// wiring). <see cref="Run"/> ALWAYS runs every check to completion
/// regardless of any other check's result -- diagnosing exactly why
/// identity/API/etc. are absent is the whole point, so nothing here ever
/// short-circuits.
/// </summary>
public static class DoctorChecks
{
    /// <summary>
    /// Finalized winget package id (DIST-4, phase 12): <c>Codevoid.AgentTaskVoid</c>
    /// -- the package identity name itself. Derived from <see cref="Branding"/>
    /// (plan/README.md standing invariant #2: never re-literal the brand); kept
    /// as a single, clearly-marked source so no other file needs to know it
    /// changed. MUST match <c>PackageIdentifier</c> in every file under
    /// <c>build/winget/manifests/.../</c> exactly -- doctor's remedy line is
    /// this codebase's other copy of the published package id.
    /// </summary>
    public static readonly string WingetPackageId = Branding.IdentityName;

    public static DoctorReport Run(DoctorContext context)
    {
        string? pfn = context.Probes.PackageFullName();
        bool identityPresent = pfn is not null;
        bool apiSupported = context.Probes.ApiSupported();
        bool devModeEnabled = context.Probes.DeveloperModeEnabled();
        bool watchdogRunning = context.Probes.WatchdogRunning();
        string? buildKindMarker = BuildKindResolver.Marker(context.Probes.PackageName?.Invoke());

        string? remedy = identityPresent
            ? null
            : $"Nothing installed/registered -- winget install {WingetPackageId}";

        RepoDiscoveryResult? repo = context.DiscoverRepo?.Invoke();
        string? repoAnchorPath = repo?.AnchorPath;
        string? repoAnchorSource = repo is null ? null : repo.AnchorSource == AnchorSource.CwdFlag ? "--cwd" : "process cwd";
        string? repoConfigParseStatus = repo?.ParseStatus switch
        {
            null => null,
            RepoConfigParseStatus.NotFound => "not-found",
            RepoConfigParseStatus.Ok => "ok",
            RepoConfigParseStatus.Malformed => "malformed",
            RepoConfigParseStatus.TooLarge => "too-large",
            _ => "unknown",
        };

        return new DoctorReport(
            identityPresent, pfn, apiSupported, devModeEnabled, watchdogRunning,
            context.ConfigPath, context.AppDataFolder, context.SidecarDir, context.LogPath,
            remedy, buildKindMarker,
            repoAnchorPath, repoAnchorSource, repo?.ConfigPath, repoConfigParseStatus, repo?.SearchedUpTo);
    }

    /// <summary>
    /// Maps the report onto a <see cref="VerbResult"/> for
    /// <see cref="Posture.RunQuery"/>'s `--strict` exit-vocabulary mapping
    /// (FAIL-2). Only identity/API feed the worst-finding exit code --
    /// Developer Mode and watchdog liveness are informational/dev-facing
    /// only and never fail `doctor`, matching plan/phase-10's "always runs
    /// to completion and exits 0 unless --strict" contract.
    /// </summary>
    public static VerbResult ToVerbResult(DoctorReport report)
    {
        if (!report.IdentityPresent)
            return VerbResult.Failure(FailureKind.IdentityNotRegistered, "No package identity detected -- see the winget remedy.");

        if (!report.ApiSupported)
            return VerbResult.Failure(FailureKind.ApiUnavailable, "AppTaskInfo.IsSupported() reports false on this build.");

        return VerbResult.Success("Package identity present and AppTaskInfo is supported.");
    }
}

/// <summary>Source-generated (AOT/trim-safe) JSON metadata for <see cref="DoctorReport"/> -- camelCase, matching every other `--json` shape in this codebase.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DoctorReport))]
internal partial class DoctorJsonContext : JsonSerializerContext
{
}
