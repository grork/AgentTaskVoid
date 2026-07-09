using Windows.Management.Deployment;

// Tiny CLI: register / unregister / sweep. See Atv.TestIdentityTool.csproj remarks
// for why this exists as a separate process rather than in-proc code somewhere else.
// Never imports Windows.UI.Shell.Tasks -- plan/README.md standing invariant #7 scopes
// that to AppTaskStore.cs alone; this tool only touches package DEPLOYMENT, not the
// AppTaskInfo API itself.
if (args.Length == 0)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  register <manifestPath> <identityName>");
    Console.Error.WriteLine("  unregister <identityName>");
    Console.Error.WriteLine("  sweep <brandTestPrefix> <aliveInstallLocation>");
    return 2;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "register" => await RegisterAsync(args[1], args[2]),
        "unregister" => await UnregisterAsync(args[1]),
        "sweep" => await SweepAsync(args[1], args[2]),
        _ => Unknown(args[0]),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Atv.TestIdentityTool: unhandled failure: {ex}");
    return 1;
}

static int Unknown(string verb)
{
    Console.Error.WriteLine($"Atv.TestIdentityTool: unknown command '{verb}'.");
    return 2;
}

// Registers (or re-registers -- idempotent, see remarks) the loose-layout package at
// manifestPath. DeveloperMode+AllowUnsigned mirrors the same trust model the working
// dev-pool registration already uses (winapp run, phase 01) -- unsigned, dev-mode
// loose layout, no external location (INFRA-16: sparse/ExternalLocationUri is
// eliminated; full-package loose layout only).
//
// Deliberately unconditional: re-registering an already-registered identity+version
// with unchanged content is a cheap, safe no-op on this platform (same principle DIST-7
// leans on for the manifest stamp itself). This is what "register-or-assert" reduces to
// operationally -- there is no cheaper "is it already right" check worth doing first.
static async Task<int> RegisterAsync(string manifestPath, string identityName)
{
    if (!File.Exists(manifestPath))
    {
        Console.Error.WriteLine($"Atv.TestIdentityTool: manifest not found: {manifestPath}");
        return 1;
    }

    var options = new RegisterPackageOptions
    {
        DeveloperMode = true,
        AllowUnsigned = true,
    };

    var manifestUri = new Uri(manifestPath);
    DeploymentResult result = await new PackageManager()
        .RegisterPackageByUriAsync(manifestUri, options)
        .AsTask();

    if (!result.IsRegistered)
    {
        Console.Error.WriteLine(
            $"Atv.TestIdentityTool: register failed for '{identityName}': " +
            $"{result.ErrorText} (0x{result.ExtendedErrorCode?.HResult:X8})");
        return 1;
    }

    Console.WriteLine($"Atv.TestIdentityTool: registered '{identityName}'.");
    return 0;
}

// Removes every currently-registered package whose Identity Name exactly equals
// identityName (normally zero or one, for this worktree's own test pool). Run from a
// process that is NOT the identity holder (INFRA-16) -- the explicit AtvTestUnregister
// MSBuild target, never suite teardown.
static async Task<int> UnregisterAsync(string identityName)
{
    var manager = new PackageManager();
    var matches = manager.FindPackagesForUser(string.Empty)
        .Where(p => string.Equals(p.Id.Name, identityName, StringComparison.Ordinal))
        .ToList();

    if (matches.Count == 0)
    {
        Console.WriteLine($"Atv.TestIdentityTool: nothing registered for '{identityName}'; nothing to do.");
        return 0;
    }

    int failures = 0;
    foreach (var pkg in matches)
    {
        string fullName = pkg.Id.FullName;
        DeploymentResult result = await manager.RemovePackageAsync(fullName).AsTask();
        if (result.IsRegistered)
        {
            // IsRegistered still true after a remove attempt means removal did not
            // take -- DeploymentResult otherwise has no dedicated "succeeded" flag for
            // removal, so ErrorText/ExtendedErrorCode is the signal to check.
        }
        if (!string.IsNullOrEmpty(result.ErrorText))
        {
            Console.Error.WriteLine($"Atv.TestIdentityTool: unregister failed for '{fullName}': {result.ErrorText}");
            failures++;
            continue;
        }
        Console.WriteLine($"Atv.TestIdentityTool: unregistered '{fullName}'.");
    }

    return failures == 0 ? 0 : 1;
}

// Reaps orphaned <brand>.Test.* registrations: any registered package whose Identity
// Name starts with brandTestPrefix AND whose InstalledLocation no longer exists on
// disk (crashed run, deleted worktree). aliveInstallLocation is THIS worktree's own
// install location -- always spared even though it matches the prefix, so sweeping
// from within a live worktree never removes that worktree's own current registration.
static async Task<int> SweepAsync(string brandTestPrefix, string aliveInstallLocation)
{
    var manager = new PackageManager();
    string aliveNormalized = NormalizeForCompare(aliveInstallLocation);

    var orphans = manager.FindPackagesForUser(string.Empty)
        .Where(p => p.Id.Name.StartsWith(brandTestPrefix, StringComparison.Ordinal))
        .Where(p => !string.Equals(NormalizeForCompare(SafeInstalledLocationPath(p)), aliveNormalized, StringComparison.OrdinalIgnoreCase))
        .Where(p => !Directory.Exists(SafeInstalledLocationPath(p)))
        .ToList();

    if (orphans.Count == 0)
    {
        Console.WriteLine($"Atv.TestIdentityTool: no orphaned '{brandTestPrefix}*' registrations found.");
        return 0;
    }

    int failures = 0;
    foreach (var pkg in orphans)
    {
        string fullName = pkg.Id.FullName;
        DeploymentResult result = await manager.RemovePackageAsync(fullName).AsTask();
        if (!string.IsNullOrEmpty(result.ErrorText))
        {
            Console.Error.WriteLine($"Atv.TestIdentityTool: sweep failed to remove '{fullName}': {result.ErrorText}");
            failures++;
            continue;
        }
        Console.WriteLine($"Atv.TestIdentityTool: swept orphan '{fullName}' (install location gone).");
    }

    return failures == 0 ? 0 : 1;
}

static string SafeInstalledLocationPath(Windows.ApplicationModel.Package pkg)
{
    try { return pkg.InstalledPath; }
    catch { return string.Empty; }
}

static string NormalizeForCompare(string path)
    => string.IsNullOrEmpty(path) ? string.Empty : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
