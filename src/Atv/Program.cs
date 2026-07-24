using Codevoid.AgentTaskVoid;
using Codevoid.AgentTaskVoid.Cli;
using Codevoid.AgentTaskVoid.Cli.Verbs;
using Codevoid.AgentTaskVoid.Diagnostics;
using Codevoid.AgentTaskVoid.Watchdog;
using Windows.ApplicationModel;

// Thin main (plan/phase-08): parse -> (help/version/bare short-circuit, no
// identity/platform needed) -> CompositionRoot -> Dispatcher. The only file
// permitted to import Windows.UI.Shell.Tasks is Store/AppTaskStore.cs
// (plan/README.md standing invariant #7); this file never does. Reading
// Package.Current.Id.Name below (for the DIST-3 (dev)/(test) --version
// marker) is a DIFFERENT WinRT namespace (Windows.ApplicationModel, already
// used by AppPaths.cs/CompositionRoot.cs) and does not touch that invariant.

ParseResult parsed = CommandLine.Parse(args);

if (parsed.ShowVersion)
{
    // DIST-3 (2026-07-10 amendment): the (dev)/(test) build-kind marker, so
    // `atv --version` output is never ambiguous about which pool produced it.
    // FormatVersionLine itself is unit-tested; only this try/catch wrapper
    // around the live WinRT call is not (needs real package identity to mean
    // anything).
    string? packageIdentityName;
    try { packageIdentityName = Package.Current.Id.Name; }
    catch (Exception) { packageIdentityName = null; }

    Console.WriteLine(BuildKindResolver.FormatVersionLine(ThisAssembly.AssemblyInformationalVersion, packageIdentityName));
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
    Console.WriteLine("Card verbs. The first verb called on a new <handle> creates its card. Every verb below");
    Console.WriteLine("except session-ended also accepts [--title T] [--subtitle S] [--icon TOKEN | --icon-file PATH]");
    Console.WriteLine("[--deep-link URI]. A flag value of exactly \"-\" reads that field from stdin (UTF-8, to EOF):");
    Console.WriteLine($"  {Branding.Command} working <handle> [--goal -]");
    Console.WriteLine($"  {Branding.Command} activity <handle> --kind read|edit|write|search|shell|fetch|web-search|plan|compacting|tool [--label -] [--agent ID] [--name N]");
    Console.WriteLine($"  {Branding.Command} blocked <handle> --question - [--agent ID]");
    Console.WriteLine($"  {Branding.Command} ready <handle> [--summary -]");
    Console.WriteLine($"  {Branding.Command} broken <handle> --reason rate-limit|overloaded|api-error|timeout|fatal [--detail -]");
    Console.WriteLine($"  {Branding.Command} agent-started <handle> [--agent ID] [--name N]");
    Console.WriteLine($"  {Branding.Command} agent-stopped <handle> [--agent ID]");
    Console.WriteLine($"  {Branding.Command} session-ended <handle> --reason finished|error");
    Console.WriteLine($"  {Branding.Command} remove <handle>");
    Console.WriteLine();
    Console.WriteLine("Data / utility verbs:");
    Console.WriteLine($"  {Branding.Command} list [--json]");
    Console.WriteLine($"  {Branding.Command} run [--title T] [--icon TOKEN] -- <command...>");
    Console.WriteLine($"  {Branding.Command} clear [--include-recycle-bin]");
    Console.WriteLine($"  {Branding.Command} doctor [--json] [--verbose]");
    Console.WriteLine();
    Console.WriteLine("Global options (accepted anywhere): --json --strict --verbose --watchdog-mode spawn|inproc|off --unsafe --wait-for-debugger");
    Console.WriteLine("  --cwd <path>  Anchor directory for repo-scoped .atv.json defaults; absent -> this process's own working directory.");
    Console.WriteLine();
    Console.WriteLine($"{Branding.Command} --version    Print the tool's version.");
    Console.WriteLine($"{Branding.Command} --help       Print this usage text.");
    Console.WriteLine();
    Console.WriteLine($"{Branding.Command} run's exit code is always the wrapped command's exit code (--strict never overrides it).");
    Console.WriteLine("See docs/integration-api.md for the full verb contract.");
}
