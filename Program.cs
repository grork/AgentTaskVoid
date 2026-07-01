using System.Runtime.InteropServices;
using Windows.UI.Shell.Tasks;

bool supported = false;
try { supported = AppTaskInfo.IsSupported(); }
catch (COMException) { }

if (!supported)
{
    Console.Error.WriteLine("AppTaskInfo is not supported on this system.");
    Console.Error.WriteLine("Make sure:");
    Console.Error.WriteLine("  1. You are on Windows 11 26100+");
    Console.Error.WriteLine("  2. The identity package is registered: .\\Register-Identity.ps1");
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

static int Create(string[] args)
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
    var content = AppTaskContent.CreateSequenceOfSteps([], subtitle.Length > 0 ? subtitle : title);

    var task = AppTaskInfo.Create(title, subtitle, deepLink, iconUri, content);
    Console.WriteLine($"Created task: {task.Id}");
    return 0;
}

static int List()
{
    var tasks = AppTaskInfo.FindAll() ?? [];
    if (tasks.Length == 0)
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

static int Clear()
{
    var tasks = AppTaskInfo.FindAll() ?? [];
    foreach (var task in tasks)
        task.Remove();
    Console.WriteLine($"Cleared {tasks.Length} task(s).");
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  apptaskinfocli create <title> [subtitle]");
    Console.WriteLine("  apptaskinfocli list");
    Console.WriteLine("  apptaskinfocli clear");
}
