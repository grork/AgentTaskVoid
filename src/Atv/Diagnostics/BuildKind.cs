namespace Atv.Diagnostics;

/// <summary>
/// DIST-3's 2026-07-10 amendment (phase 12): classifies which of the
/// build-kind-aware identity pools (<c>build/Atv.Package.targets</c>'
/// <c>AtvStampAppxManifest</c>) the CURRENT process is running under, from
/// the declared Identity Name string alone (<c>Package.Current.Id.Name</c>
/// -- NOT <c>Package.Current.Id.FamilyName</c>/the PFN, which also folds in
/// the publisher hash and is irrelevant to this classification).
/// </summary>
public enum BuildKind
{
    /// <summary>No package identity at all (unpackaged process) -- documented as unmarked, same as Release.</summary>
    NoIdentity,

    /// <summary>Name == <see cref="Branding.Name"/> exactly -- the clean, pathhash-free shipped release identity.</summary>
    Release,

    /// <summary>Name starts with <c>&lt;brand&gt;.Test.</c> -- the per-worktree real-API adapter-test pool (INFRA-16).</summary>
    Test,

    /// <summary>
    /// Everything else -- the default <c>&lt;brand&gt;-&lt;pathhash&gt;</c> dev-interactive
    /// identity, or the phase-12 throwaway <c>&lt;brand&gt;-reltest</c> smoke variant.
    /// Both are developer-machine-local, never a real end-user install, so both mark
    /// themselves <c>(dev)</c>.
    /// </summary>
    Dev,
}

/// <summary>
/// Resolves <see cref="BuildKind"/> from a raw Identity Name and renders the
/// operator-requested "so it's not ambiguous looking at lots [of logs],
/// traces etc." console/log/failure-log marker (DIST-3). Pure functions over
/// injected data (INFRA-8-style seam, plan/README.md standing invariant #2:
/// derived from <see cref="Branding"/>, never a re-literaled brand string) --
/// unit-testable with zero package identity; every caller (doctor, --version,
/// the durable failure log) passes whatever <c>Package.Current.Id.Name</c>
/// resolved to (or <see langword="null"/> when identity is absent), never
/// hard-binding to the live package itself.
/// </summary>
public static class BuildKindResolver
{
    private const string TestInfix = ".Test.";

    public static BuildKind Resolve(string? packageIdentityName)
    {
        if (string.IsNullOrEmpty(packageIdentityName))
            return BuildKind.NoIdentity;

        if (string.Equals(packageIdentityName, Branding.Name, StringComparison.Ordinal))
            return BuildKind.Release;

        if (packageIdentityName.StartsWith(Branding.Name + TestInfix, StringComparison.Ordinal))
            return BuildKind.Test;

        return BuildKind.Dev;
    }

    /// <summary>
    /// The unambiguous marker text for a build kind -- <see langword="null"/>
    /// for <see cref="BuildKind.Release"/> (ship output stays clean/unmarked,
    /// per the operator's "do NOT mark release output" instruction) and for
    /// <see cref="BuildKind.NoIdentity"/> (documented: nothing to mark --
    /// there is no build-kind information to show at all).
    /// </summary>
    public static string? Marker(BuildKind kind) => kind switch
    {
        BuildKind.Dev => "(dev)",
        BuildKind.Test => "(test)",
        _ => null,
    };

    /// <summary>Convenience: resolve + mark in one call.</summary>
    public static string? Marker(string? packageIdentityName) => Marker(Resolve(packageIdentityName));

    /// <summary>
    /// `--version`'s exact output line, factored out here so it is
    /// unit-testable -- Program.cs's thin wrapper around the real
    /// <c>Package.Current.Id.Name</c> WinRT call (which needs live package
    /// identity to mean anything) stays the only untestable line.
    /// </summary>
    public static string FormatVersionLine(string version, string? packageIdentityName)
    {
        ArgumentNullException.ThrowIfNull(version);
        string? marker = Marker(packageIdentityName);
        return marker is null ? version : $"{version} {marker}";
    }
}
