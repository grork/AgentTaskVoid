namespace Atv.Watchdog;

/// <summary>
/// INFRA-19's <c>inproc</c> dev/debug hosting mode: runs the SAME
/// <see cref="WatchdogLoop.Run"/> logic on a background thread bound to the
/// invoking process's lifetime (<see cref="ThreadPriority"/> default,
/// <see cref="Thread.IsBackground"/> = true so it dies with the process --
/// shift-F5 takes it too, matching INFRA-18's dev-loop expectations). NOT a
/// production-supervision equivalent (a 50ms `atv step` gives 50ms of
/// supervision) -- its point is "no detached child to lock the exe or
/// sprawl" during inner-loop development.
///
/// <paramref name="buildContext"/> is lazy (a factory, not an eagerly-built
/// <see cref="RunContext"/>) so constructing this host never forces a real
/// composition-root build unless <see cref="Start"/> is actually called.
/// </summary>
public sealed class InProcThreadHost : IWatchdogHost
{
    private readonly Func<RunContext> _buildContext;
    private readonly Action<string> _log;

    public InProcThreadHost(Func<RunContext> buildContext, Action<string>? log = null)
    {
        _buildContext = buildContext;
        _log = log ?? (_ => { });
    }

    public void Start()
    {
        var thread = new Thread(RunOnThread)
        {
            IsBackground = true,
            Name = "atv-watchdog-inproc",
        };
        thread.Start();
    }

    private void RunOnThread()
    {
        try
        {
            RunContext ctx = _buildContext();
            WatchdogLoop.Run(ctx);
        }
        catch (Exception ex)
        {
            _log($"watchdog (inproc): loop crashed ({ex.GetType().Name}: {ex.Message}) -- non-disruptive, this thread simply ends.");
        }
    }
}
