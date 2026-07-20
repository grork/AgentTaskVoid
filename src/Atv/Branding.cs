namespace Codevoid.AgentTaskVoid;

/// <summary>
/// Single source of truth for the product brand (ERGO-18, "The shipped command
/// name"). Every identity, command-name, env/config, mutex, and path string in the
/// codebase MUST derive from these three constants -- never re-literal the brand or
/// command name anywhere else (plan/README.md standing invariant #2).
///
/// <c>Directory.Build.props</c> reads this file's literal text at build time (via a
/// regex over <c>public const string IdentityName/Command = "...";</c>) to compute
/// <c>$(AtvBrandName)</c> / <c>$(AtvCommandName)</c> for MSBuild -- which feeds
/// <c>AssemblyName</c> and the AppxManifest Identity Name stamp
/// (<c>build/Atv.Package.targets</c>, DIST-7). Rename a constant here and both the
/// compiled code AND the build/manifest machinery follow automatically.
/// </summary>
public static class Branding
{
    /// <summary>
    /// MSIX Identity Name base and winget package id. Seeds the stamped package
    /// identity for all three pools (release is exactly this; dev/test append
    /// their suffixes), build-kind classification, and mutex names. Must stay a
    /// legal MSIX Identity/@Name (alphanumerics, periods, dashes -- no spaces).
    /// </summary>
    public const string IdentityName = "Codevoid.AgentTaskVoid";

    /// <summary>Human-facing product name -- taskbar cards, Explorer, Task Manager, Settings.</summary>
    public const string DisplayName = "Agent Task Void";

    /// <summary>Command / binary / AppExecutionAlias name -- what the user types.</summary>
    public const string Command = "atv";
}
