using Atv;
using Atv.Cli;

// Thin main (plan/phase-08): parse -> (help/version/bare short-circuit, no
// identity/platform needed) -> CompositionRoot -> Dispatcher. The only file
// permitted to import Windows.UI.Shell.Tasks is Store/AppTaskStore.cs
// (plan/README.md standing invariant #7); this file never does.

ParseResult parsed = CommandLine.Parse(args);

if (parsed.ShowVersion)
{
    Console.WriteLine(ThisAssembly.AssemblyInformationalVersion);
    return 0;
}

if (parsed.ShowHelp || (parsed.Verb is null && parsed.Error is null))
{
    PrintUsage();
    return 0;
}

RootContext root = CompositionRoot.Build(parsed.Global, Console.Out, Console.Error);
return root.Dispatcher.Run(parsed, DateTimeOffset.Now);

static void PrintUsage()
{
    Console.WriteLine($"Usage: {Branding.Command} <verb> <handle> [options]");
    Console.WriteLine();
    Console.WriteLine("Lifecycle verbs:");
    Console.WriteLine($"  {Branding.Command} start <handle> [--title T] [--subtitle S] [--icon TOKEN] [--deep-link URI] [--reset]");
    Console.WriteLine($"  {Branding.Command} step <handle> <message>");
    Console.WriteLine($"  {Branding.Command} state <handle> running|paused");
    Console.WriteLine($"  {Branding.Command} attention <handle> <question>");
    Console.WriteLine($"  {Branding.Command} done <handle> [--summary TEXT]");
    Console.WriteLine($"  {Branding.Command} fail <handle> [--summary TEXT]");
    Console.WriteLine($"  {Branding.Command} remove <handle>");
    Console.WriteLine();
    Console.WriteLine("Global options (accepted anywhere): --json --strict --verbose --watchdog-mode spawn|inproc|off --unsafe --wait-for-debugger");
    Console.WriteLine();
    Console.WriteLine($"{Branding.Command} --version    Print the tool's version.");
    Console.WriteLine($"{Branding.Command} --help       Print this usage text.");
    Console.WriteLine();
    Console.WriteLine("list/clear/doctor/run land in later phases -- for now, remove <handle> is the way to clean up a single task.");
}
