using Windows.ApplicationModel;
using Windows.Storage;

namespace Codevoid.AgentTaskVoid.Persistence;

/// <summary>
/// Runtime-derived app-data paths for every persistence mechanism this
/// namespace owns (sidecar index, recycle bin, icons, log, config) --
/// plan/README.md standing invariant #3: nothing here ever hardcodes a PFN.
///
/// Production paths are rooted at <c>ApplicationData.Current.LocalFolder</c>,
/// which the OS already resolves per-package-identity
/// (<c>%LOCALAPPDATA%\Packages\&lt;PFN&gt;\LocalState</c>) -- simply USING
/// that, rather than hand-building a <c>...\Packages\&lt;PFN&gt;\...</c>
/// string ourselves, satisfies "no hardcoded PFN" for free. DIST-3 ("Dev vs
/// release identity (PFN) divergence")'s three identity pools (release,
/// dev-interactive, per-worktree test) each get a different LocalFolder
/// automatically, with zero extra plumbing here. File/dir NAMES under that
/// root derive from the ERGO-18 ("The shipped command name")
/// <see cref="Branding.Command"/> constant (ERGO-26's standing requirement),
/// never a re-literal.
///
/// Testing seam (INFRA-8-style -- no interface, matching ERGO-21's sidecar
/// seam and LIFE-21's recycle-bin seam): <see cref="ForRoot"/> injects an
/// arbitrary root (prod = LocalFolder, test = a temp dir), so every consumer
/// is testable with zero package identity.
/// </summary>
public sealed class AppPaths
{
    /// <summary>Root of every path this type derives -- LocalFolder in production, an injected temp dir in tests.</summary>
    public string Root { get; }

    private AppPaths(string root) => Root = root;

    /// <summary>Injects an arbitrary root. Tests use a temp directory -- no package identity required.</summary>
    public static AppPaths ForRoot(string root) => new(root);

    /// <summary>Production root: the current package's isolated app-data folder.</summary>
    public static AppPaths ForCurrentPackage() => new(ApplicationData.Current.LocalFolder.Path);

    /// <summary>ERGO-21 ("The sidecar store design"): the sidecar index directory -- one file per handle.</summary>
    public string SidecarDir => Path.Combine(Root, "sidecar");

    /// <summary>LIFE-21 ("What expiry does"): the cold recycle-bin folder -- never enumerated on the hot path.</summary>
    public string RecycleBinDir => Path.Combine(Root, "recycle-bin");

    /// <summary>ERGO-22/ERGO-23: per-handle icon copies + the canonical render-once cache (phase 07 -- folder contract only, defined here).</summary>
    public string IconsDir => Path.Combine(Root, "icons");

    /// <summary>FAIL-3 ("Diagnosability"): the durable failure log.</summary>
    public string LogPath => Path.Combine(Root, $"{Branding.Command}.log");

    /// <summary>ERGO-26 ("Config file location and format"): the JSON config file.</summary>
    public string ConfigPath => Path.Combine(Root, $"{Branding.Command}-config.json");

    /// <summary>
    /// The INFRA-6 global write-mutex name for the CURRENT package identity:
    /// <c>Local\&lt;brand&gt;-&lt;PFN&gt;-tasks-write</c>. Not consumed by
    /// <see cref="WriteGate"/> itself -- WriteGate takes an
    /// already-constructed <see cref="System.Threading.Mutex"/> from the
    /// composition root (production named, tests unnamed/unique; INFRA-8).
    /// This property is where PFN derivation for that name is centralized so
    /// no downstream phase re-derives or hardcodes it. Uses
    /// <see cref="Branding.IdentityName"/> (not <see cref="Branding.Command"/>) for
    /// the brand segment -- the same brand string that seeds the package
    /// Identity Name itself (<c>build/Atv.Package.targets</c>'
    /// <c>$(AtvBrandName)</c>), which is what the PFN is ultimately derived
    /// from.
    /// </summary>
    public static string CurrentWriteMutexName => BuildWriteMutexName(Package.Current.Id.FamilyName);

    /// <summary>Pure, testable half of <see cref="CurrentWriteMutexName"/> -- takes the package family name (PFN) as plain data.</summary>
    public static string BuildWriteMutexName(string packageFamilyName)
        => $@"Local\{Branding.IdentityName}-{packageFamilyName}-tasks-write";

    /// <summary>
    /// LIFE-18's ("Watchdog single-instance enforcement") named mutex for the
    /// CURRENT package identity: <c>Local\&lt;brand&gt;-&lt;PFN&gt;-watchdog</c>,
    /// held for the watchdog's whole lifetime. Same derivation pattern as
    /// <see cref="CurrentWriteMutexName"/> -- single source of truth for this
    /// name too, so phase 08's <c>EnsureWatchdog</c> liveness probe and
    /// phase 09's real watchdog host never re-derive or hardcode it
    /// independently.
    /// </summary>
    public static string CurrentWatchdogMutexName => BuildWatchdogMutexName(Package.Current.Id.FamilyName);

    /// <summary>Pure, testable half of <see cref="CurrentWatchdogMutexName"/> -- takes the package family name (PFN) as plain data.</summary>
    public static string BuildWatchdogMutexName(string packageFamilyName)
        => $@"Local\{Branding.IdentityName}-{packageFamilyName}-watchdog";
}
