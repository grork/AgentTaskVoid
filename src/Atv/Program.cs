using Atv;
using Atv.Store;
using Windows.ApplicationModel;

// AppTaskStore wraps IsSupported() for the CLASS_E_CLASSNOTAVAILABLE
// COMException some Windows 11 builds throw even with valid package identity
// (INFRA-13) -- Program.cs no longer needs its own try/catch for that.
IAppTaskStore store = new AppTaskStore();

if (!store.IsSupported())
{
    Console.Error.WriteLine("AppTaskInfo is not supported on this system.");
    Console.Error.WriteLine("Make sure:");
    Console.Error.WriteLine("  1. You are on Windows 11 26100+");
    Console.Error.WriteLine("  2. The tool is running with package identity (see CLAUDE.md: winapp dev loop)");
    return 1;
}

if (args.Length == 0)
{
    PrintUsage();
    return 0;
}

switch (args[0].ToLowerInvariant())
{
    case "create":
        return Create(args[1..]);

    case "list":
        return List();

    case "clear":
        return Clear();

    default:
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        PrintUsage();
        return 1;
}

// Not `static`: these close over `store` (a plain field-capture closure, not a
// static local function) -- the only file allowed to talk to
// Windows.UI.Shell.Tasks directly is AppTaskStore.cs (plan/README.md standing
// invariant #7); everything here, including this POC scaffolding, goes
// through IAppTaskStore.
int Create(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Usage: create <title> [subtitle]");
        return 1;
    }

    string title = args[0];
    string subtitle = args.Length > 1 ? args[1] : "";

    // Both deepLink and iconUri cannot be null — the native implementation dereferences them
    // unconditionally in ResolveIconPath and CreateJsonObject respectively.
    var iconUri = new Uri("ms-appx:///Assets/Square44x44Logo.png");
    var deepLink = new Uri("https://example.com");
    var content = new AppTaskContentDto.SequenceOfSteps([], subtitle.Length > 0 ? subtitle : title);

    var task = store.Create(title, subtitle, deepLink, iconUri, content);
    Console.WriteLine($"Created task: {task.Id}");
    return 0;
}

int List()
{
    Console.WriteLine($"Package: {PackageFullName()}");

    var tasks = store.FindAll();
    if (tasks.Count == 0)
    {
        Console.WriteLine("No tasks.");
        return 0;
    }

    foreach (var task in tasks)
    {
        Console.WriteLine($"Id:       {task.Id}");
        Console.WriteLine($"Title:    {task.Title}");
        Console.WriteLine($"Subtitle: {task.Subtitle}");
        Console.WriteLine($"State:    {task.State}");
        Console.WriteLine();
    }
    return 0;
}

int Clear()
{
    var tasks = store.FindAll();
    foreach (var task in tasks)
        store.Remove(task.Id);
    Console.WriteLine($"Cleared {tasks.Count} task(s).");
    return 0;
}

// Diagnostic proof of package identity (phase-01 AC2): the PFN returned here is the
// same value GetCurrentPackageFullName would return, sourced from the same in-process
// package graph AppTaskInfo itself depends on.
static string PackageFullName()
{
    try { return Package.Current.Id.FullName; }
    catch (Exception ex) { return $"(no package identity: {ex.Message})"; }
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine($"  {Branding.Command} create <title> [subtitle]");
    Console.WriteLine($"  {Branding.Command} list");
    Console.WriteLine($"  {Branding.Command} clear");
}
