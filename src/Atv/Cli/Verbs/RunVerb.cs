using Atv.Config;
using Atv.Diagnostics;
using Atv.Icons;
using Atv.Operations;
using Atv.Run;
using Atv.Semantics;

namespace Atv.Cli.Verbs;

/// <summary>
/// Everything <see cref="RunVerb.Run"/> needs, gathered in one place
/// (mirrors <c>WatchdogDeps</c>'s shape/purpose): the real collaborators
/// every other verb already uses (<see cref="Ops"/>/<see cref="Engine"/>/
/// <see cref="Icons"/>/<see cref="Posture"/>/<see cref="HasIdentity"/>/
/// <see cref="IsSupported"/>/<see cref="EnsureWatchdog"/>/
/// <see cref="DefaultDeepLink"/>), plus what `run` alone needs on top:
/// resolved <see cref="Settings"/> (debounce/keepalive/max-length tunables),
/// a live <see cref="Clock"/> and <see cref="Sleep"/> for the step-publisher
/// loop (test-controllable, mirrors <c>RunContext</c>), a
/// <see cref="SpawnChild"/> factory (the AC2 fake-child seam), and the two
/// raw byte sinks the child's stdout/stderr mirror to (production = the real
/// console's raw streams; tests = in-memory <see cref="Stream"/>s).
///
/// Phase 15 (re-seat onto the v2 engine): <see cref="Ops"/> is STILL needed
/// -- <see cref="Run.StepPublisher"/>'s debounced step-stream write
/// (<see cref="TaskOperations.ReplaceSteps"/>/<see cref="TaskOperations.TouchKeepAlive"/>)
/// keeps its own whole-buffer-replace content model, unrelated to the v2
/// claim semantics. Only the card's START/FINISH now go through
/// <see cref="Engine"/> (<c>working</c>/<c>ready</c>/<c>broken</c>
/// equivalents) instead of the retired <c>TaskOperations.Start/Done/Fail</c>.
/// </summary>
public sealed record RunDeps(
    TaskOperations Ops,
    SemanticEngine Engine,
    IconService Icons,
    Posture Posture,
    Func<bool> HasIdentity,
    Func<bool> IsSupported,
    Action EnsureWatchdog,
    Uri DefaultDeepLink,
    Settings Settings,
    Func<DateTimeOffset> Clock,
    Action<TimeSpan> Sleep,
    Func<IReadOnlyList<string>, IChildProcess> SpawnChild,
    Stream StdoutMirror,
    Stream StderrMirror);

/// <summary>
/// `run --title &lt;t&gt; [--icon &lt;token&gt;] -- &lt;command...&gt;` (ERGO-5,
/// "Providing a wrapper"; ERGO-27 C2). Deliberately NOT wrapped end-to-end in
/// <see cref="Posture.Run"/> like a lifecycle verb -- ONLY atv's own
/// pre-launch failures (bad args, capability down, can't spawn) go through
/// <see cref="Posture"/> (consistent logging + `--strict` exit-vocabulary
/// mapping for THOSE specific failures); once the child has actually
/// launched, this method returns the child's raw exit code directly,
/// UNCHANGED by `--strict` (ERGO-27 C2 -- "the child's exit code always
/// wins"). No `--json`/mutating-result stdout emission on the launched path
/// either -- appending anything to stdout beyond the child's own mirrored
/// bytes would break the transparency promise.
/// </summary>
public static class RunVerb
{
    /// <summary>How long a Ctrl+C-triggered cancellation waits for the child to exit on its own before <see cref="IChildProcess.RequestCancel"/>'s own escalation force-kills it. Not a phase-06 config tunable (the phase file lists only debounce/keepalive/max-length) -- a fixed, generous-enough-for-a-well-behaved-child constant.</summary>
    public static readonly TimeSpan CancelGracePeriod = TimeSpan.FromSeconds(3);

    public static int Run(RunDeps deps, ParseResult parsed, DateTimeOffset now)
    {
        if (parsed.Positionals.Count > 0)
        {
            return deps.Posture.Run("run", null, () => VerbResult.Failure(FailureKind.InvalidArguments,
                "run takes no positional <handle> -- it mints its own. Did you forget the '--' before the child command?"), now);
        }

        if (parsed.ChildArgs.Count == 0)
        {
            return deps.Posture.Run("run", null, () => VerbResult.Failure(FailureKind.InvalidArguments,
                "run requires a child command after '--' (e.g. `atv run --title T -- <command...>`)."), now);
        }

        if (!TryResolveIconToken(parsed, out IconToken token, out VerbResult? iconErr))
            return deps.Posture.Run("run", null, () => iconErr!.Value, now);

        VerbResult cap = Capability.Check(deps.HasIdentity, deps.IsSupported);
        if (!cap.Ok)
            return deps.Posture.Run("run", null, () => cap, now);

        deps.EnsureWatchdog();

        IChildProcess child;
        try
        {
            child = deps.SpawnChild(parsed.ChildArgs);
        }
        catch (Exception ex)
        {
            return deps.Posture.Run("run", null, () => VerbResult.Failure(FailureKind.Generic,
                $"Could not launch child process: {ex.Message}"), now);
        }

        string handle = MintHandle(now);
        string title = parsed.Flags.TryGetValue("title", out string? t) && !string.IsNullOrWhiteSpace(t)
            ? t
            : string.Join(' ', parsed.ChildArgs);
        Uri iconUri = deps.Icons.Place(handle, token);

        int cancelled = 0;
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            // Keep OUR process alive -- the default behavior would tear this
            // process down immediately, before we ever get to forward the
            // signal, wait for the child, or mark the card. Idempotent:
            // GenerateConsoleCtrlEvent(..., 0) below necessarily re-signals
            // THIS process too (Win32 can't target only the child).
            e.Cancel = true;
            if (Interlocked.Exchange(ref cancelled, 1) == 0)
                child.RequestCancel(CancelGracePeriod);
        };

        try
        {
            Console.CancelKeyPress += handler;
            return RunOrchestrator.Execute(deps.Ops, deps.Engine, deps.Settings, deps.Clock, deps.Sleep,
                handle, title, iconUri, deps.DefaultDeepLink, child, deps.StdoutMirror, deps.StderrMirror, now);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            child.Dispose();
        }
    }

    /// <summary>The wrapper MINTS ITS OWN handle (the one sanctioned exception to caller-supplied handles, ERGO-6) -- brand-derived prefix + a GUID, unique per run.</summary>
    private static string MintHandle(DateTimeOffset now)
        => $"{Branding.Command}-run-{now:yyyyMMddTHHmmss}-{Guid.NewGuid():N}";

    private static bool TryResolveIconToken(ParseResult p, out IconToken token, out VerbResult? error)
    {
        if (!p.Flags.TryGetValue("icon", out string? raw))
        {
            token = IconTokens.Default;
            error = null;
            return true;
        }

        if (!IconTokens.TryParse(raw, out token, out string? parseError))
        {
            error = VerbResult.Failure(FailureKind.InvalidArguments, parseError ?? $"--icon '{raw}' is invalid.");
            return false;
        }

        error = null;
        return true;
    }
}

/// <summary>
/// The testable core of one `run` invocation, once a child has already been
/// spawned: start the card -&gt; wire the two <see cref="OutputPump"/> reader
/// threads + one <see cref="StepPublisher"/> loop thread (three independent
/// threads, per ERGO-5's decoupled reader/updater structure) -&gt; block for
/// the child's exit -&gt; drain/flush -&gt; map exit code onto the v2
/// <c>ready</c>/<c>broken</c> claims -&gt; return the exit code verbatim.
/// Takes <see cref="IChildProcess"/> (not a concrete <see cref="ChildProcess"/>)
/// so a scripted fake child can drive this exact code path in tests (AC2)
/// with no real process involved.
///
/// Phase 15 re-seat: the card's start/finish now go through
/// <see cref="SemanticEngine"/> (a bare <c>working</c> claim at launch --
/// no goal, the wrapper has none to give; bare <c>ready</c> on exit 0,
/// preserving the last mirrored output lines exactly like v1's bare
/// <c>done</c> did; <c>broken --reason fatal</c> on a nonzero exit, the
/// vocabulary's catch-all -- exactly one <see cref="BrokenReasonToken"/> fits
/// a generic child-process failure) instead of the retired
/// <c>TaskOperations.Start/Done/Fail</c>. The step-stream itself
/// (<see cref="StepPublisher"/>) is UNCHANGED -- still <see cref="TaskOperations.ReplaceSteps"/>/
/// <see cref="TaskOperations.TouchKeepAlive"/>'s whole-buffer-replace model,
/// which has nothing to do with the v2 claim semantics. This is the "v2
/// internals underneath it" the phase-11 observable contract (exit-code
/// passthrough, debounce, lingering card) is preserved over.
/// </summary>
public static class RunOrchestrator
{
    public static int Execute(
        TaskOperations ops,
        SemanticEngine engine,
        Settings settings,
        Func<DateTimeOffset> clock,
        Action<TimeSpan> sleep,
        string handle,
        string title,
        Uri iconUri,
        Uri deepLink,
        IChildProcess child,
        Stream stdoutMirror,
        Stream stderrMirror,
        DateTimeOffset startNow)
    {
        engine.Working(handle, title, subtitle: "", iconUri, deepLink, goal: null, startNow);

        var publisher = new StepPublisher(ops, handle, settings.RunKeepAliveInterval, startNow);

        void OnLine(string raw)
        {
            string? cleaned = LineHygiene.Clean(raw, settings.RunStepMaxLength);
            if (cleaned is not null)
                publisher.Ingest(cleaned);
        }

        var stdoutThread = new Thread(() => OutputPump.Pump(child.StandardOutput, stdoutMirror, OnLine))
        { IsBackground = true, Name = "atv-run-stdout-pump" };
        var stderrThread = new Thread(() => OutputPump.Pump(child.StandardError, stderrMirror, OnLine))
        { IsBackground = true, Name = "atv-run-stderr-pump" };
        stdoutThread.Start();
        stderrThread.Start();

        int stopFlag = 0;
        var publisherThread = new Thread(() =>
            publisher.RunLoop(clock, sleep, settings.RunUpdateDebounce, () => Volatile.Read(ref stopFlag) != 0))
        { IsBackground = true, Name = "atv-run-step-publisher" };
        publisherThread.Start();

        // Decoupled reader/updater (ERGO-5): the child's own exit is governed
        // solely by the two pump threads draining to EOF, never by the
        // publisher's debounce cadence.
        int exitCode = child.WaitForExit();

        stdoutThread.Join();
        stderrThread.Join();

        Volatile.Write(ref stopFlag, 1);
        publisherThread.Join();

        DateTimeOffset endNow = clock();
        publisher.FlushFinal(endNow); // Never lose the last lines to an in-flight debounce window.

        if (exitCode == 0)
            engine.Ready(handle, title, subtitle: "", iconUri, deepLink, summary: null, endNow);
        else
            engine.Broken(handle, title, subtitle: "", iconUri, deepLink, BrokenReasonToken.Fatal, detail: null, endNow);

        return exitCode;
    }
}
