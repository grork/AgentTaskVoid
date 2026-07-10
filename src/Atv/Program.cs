using Atv;
using Atv.Cli;
using Atv.Cli.Verbs;
using Atv.Watchdog;

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

// Hidden `watchdog` verb (LIFE-17/INFRA-21): a long-running blocking loop,
// never routed through Dispatcher/Posture (not a single request/outcome
// verb), and deliberately absent from PrintUsage() below.
if (parsed.Verb == "watchdog")
{
    return WatchdogVerb.Run(parsed.Global);
}

if (parsed.ShowHelp || (parsed.Verb is null && parsed.Error is null))
{
    // LIFE-20 boot recovery: a truly bare invocation (no verb, no parse
    // error, --help not requested -- StartupTask launches carry no CLI args)
    // recognized by ACTIVATION KIND is an OS-triggered boot/crash-recovery
    // run, not a user asking for usage text.
    if (!parsed.ShowHelp && parsed.Verb is null && parsed.Error is null && BootRecovery.IsStartupTaskActivation())
    {
        BootRecovery.FlatClear(CompositionRoot.BuildWatchdogDeps(parsed.Global));
        StartupTaskControl.DisableSync();
        return 0;
    }

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
    Console.WriteLine("Data / utility verbs:");
    Console.WriteLine($"  {Branding.Command} list [--json]");
    Console.WriteLine($"  {Branding.Command} run [--title T] [--icon TOKEN] -- <command...>");
    Console.WriteLine($"  {Branding.Command} clear [--include-recycle-bin]");
    Console.WriteLine($"  {Branding.Command} doctor [--json] [--verbose]");
    Console.WriteLine();
    Console.WriteLine("Global options (accepted anywhere): --json --strict --verbose --watchdog-mode spawn|inproc|off --unsafe --wait-for-debugger");
    Console.WriteLine();
    Console.WriteLine($"{Branding.Command} --version    Print the tool's version.");
    Console.WriteLine($"{Branding.Command} --help       Print this usage text.");
    Console.WriteLine();
    Console.WriteLine($"{Branding.Command} run's exit code is always the wrapped command's exit code (--strict never overrides it).");
}
