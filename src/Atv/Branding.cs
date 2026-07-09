namespace Atv;

/// <summary>
/// Single source of truth for the product brand (ERGO-18, "The shipped command
/// name"). Every identity, command-name, env/config, mutex, and path string in the
/// codebase MUST derive from these two constants -- never re-literal the brand or
/// command name anywhere else (plan/README.md standing invariant #2).
///
/// <c>Directory.Build.props</c> reads this file's literal text at build time (via a
/// regex over <c>public const string Name/Command = "...";</c>) to compute
/// <c>$(AtvBrandName)</c> / <c>$(AtvCommandName)</c> for MSBuild -- which feeds
/// <c>AssemblyName</c> and the AppxManifest Identity Name stamp
/// (<c>build/Atv.Package.targets</c>, DIST-7). Rename either constant here and both
/// the compiled code AND the build/manifest machinery follow automatically.
/// </summary>
public static class Branding
{
    /// <summary>Product/brand name, as shown in Explorer, Task Manager, and Settings.</summary>
    public const string Name = "Agentaskvoid";

    /// <summary>Command / binary / AppExecutionAlias name -- what the user types.</summary>
    public const string Command = "atv";
}
