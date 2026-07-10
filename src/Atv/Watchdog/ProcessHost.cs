using System.Diagnostics;

namespace Atv.Watchdog;

/// <summary>
/// LIFE-17's production spawn mechanics: the SAME <c>atv</c> exe re-invoked
/// in the hidden <c>watchdog</c> verb, spawned windowless
/// (<see cref="ProcessStartInfo.CreateNoWindow"/>) and detached -- a plain
/// Windows child process already survives its parent's exit with no extra
/// job-object plumbing, which the REQUIRED adapter-suite integration test
/// (<c>tests/Atv.AdapterTests/WatchdogProcessHostTests.cs</c>) proves
/// empirically.
///
/// <paramref name="exePath"/> is caller-supplied (never hardcoded here) --
/// production wiring (<see cref="Atv.Cli.CompositionRoot"/>) passes
/// <see cref="Environment.ProcessPath"/> (literally "this same exe", per
/// LIFE-17 -- no brand/PFN re-derivation needed), which doubles as the test
/// hook: when a REAL-adapter test constructs its own <see cref="ProcessHost"/>
/// from ITS OWN <see cref="Environment.ProcessPath"/>, it spawns a copy of
/// the identity-carrying test exe itself, letting the required integration
/// test exercise a real detached spawn without needing a second, separately
/// identity-provisioned binary.
/// </summary>
public sealed class ProcessHost : IWatchdogHost
{
    private readonly string _exePath;
    private readonly IReadOnlyList<string> _args;
    private readonly IReadOnlyDictionary<string, string>? _extraEnvironment;
    private readonly Action<string> _log;

    public ProcessHost(string exePath, IReadOnlyList<string> args, Action<string>? log = null, IReadOnlyDictionary<string, string>? extraEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(exePath);
        ArgumentNullException.ThrowIfNull(args);
        _exePath = exePath;
        _args = args;
        _extraEnvironment = extraEnvironment;
        _log = log ?? (_ => { });
    }

    public void Start()
    {
        if (string.IsNullOrEmpty(_exePath))
        {
            _log("watchdog: no exe path available to spawn (Environment.ProcessPath was null/empty) -- non-disruptive, skipping.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (string arg in _args)
            psi.ArgumentList.Add(arg);
        if (_extraEnvironment is not null)
        {
            foreach (var kv in _extraEnvironment)
                psi.EnvironmentVariables[kv.Key] = kv.Value;
        }

        using Process? process = Process.Start(psi);
        if (process is null)
            _log($"watchdog: Process.Start returned null for '{_exePath}'.");
    }
}
