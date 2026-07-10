using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Atv.Run;

/// <summary>
/// The `run` wrapper's testing seam over a spawned child (INFRA-8-style):
/// <see cref="ChildProcess"/> is the sole real implementation; the logic
/// suite substitutes a scripted fake (no real process) to exercise
/// <c>RunOrchestrator</c>'s lifecycle mapping (AC2). Deliberately thin --
/// exactly what the wrapper needs (the two raw output streams, a blocking
/// wait, and cancellation) and nothing more.
/// </summary>
public interface IChildProcess : IDisposable
{
    /// <summary>The child's OS process id -- diagnostic/testing use only (e.g. proving "no orphan" via <c>Process.GetProcessById</c> after exit); never used for correctness decisions in this codebase.</summary>
    int Id { get; }

    /// <summary>Raw byte stream -- no encoding applied by this type (transparency: <see cref="OutputPump"/> owns the only decode, for the step copy only).</summary>
    Stream StandardOutput { get; }

    /// <summary>Raw byte stream, same contract as <see cref="StandardOutput"/>.</summary>
    Stream StandardError { get; }

    /// <summary>Blocks until the child has exited (naturally, or via <see cref="RequestCancel"/>'s own escalation), returning its real exit code verbatim.</summary>
    int WaitForExit();

    /// <summary>
    /// Ctrl+C handling: best-effort forwards a break to the child, then
    /// arms a bounded escalation to a hard kill if the child hasn't exited
    /// within <paramref name="gracePeriod"/> -- non-blocking (safe to call
    /// from a console signal handler, which must return promptly) and safe
    /// to call concurrently with a thread blocked in <see cref="WaitForExit"/>.
    /// The "never orphan the child" guarantee comes from the escalation, NOT
    /// from the forwarded signal actually being delivered/honored.
    /// </summary>
    void RequestCancel(TimeSpan gracePeriod);
}

/// <summary>
/// Real <see cref="IChildProcess"/>: a plain, unelevated child process with
/// its stdout/stderr redirected as raw streams (no <c>ProcessStartInfo</c>
/// encoding involved -- <see cref="StandardOutput"/>/<see cref="StandardError"/>
/// return <see cref="Process.StandardOutput"/>/<see cref="Process.StandardError"/>'s
/// own <c>BaseStream</c>, sidestepping any TextReader decode/re-encode round
/// trip that could corrupt the mirror). Stdin is left NOT redirected --
/// interactive/TTY children are out of scope (phase file), but a child that
/// happens to read stdin still inherits this process's real console handle
/// rather than seeing a closed pipe.
///
/// No <c>CREATE_NEW_PROCESS_GROUP</c> flag is set, so the child stays in
/// THIS process's console process group -- Windows already delivers a real
/// Ctrl+C keypress to the whole group automatically. <see cref="RequestCancel"/>
/// additionally calls <c>GenerateConsoleCtrlEvent</c> explicitly (the
/// standard technique real process-wrapper tools use, since automatic
/// group delivery isn't guaranteed reliable across every hosting layer --
/// e.g. `dotnet run`'s own launcher indirection) -- <c>dwProcessGroupId=0</c>
/// necessarily also re-signals THIS process (Win32 has no way to target only
/// the child), so callers must make their own handler idempotent. Delivery
/// of that signal is inherently best-effort (it requires the CALLER to be
/// attached to a console at all, which e.g. a headless test host is not) --
/// the real "never orphan" guarantee is the grace-period-then-<see cref="Process.Kill(bool)"/>
/// escalation below, which holds regardless of whether the signal was
/// delivered.
/// </summary>
public sealed partial class ChildProcess : IChildProcess
{
    private readonly Process _process;

    private ChildProcess(Process process) => _process = process;

    /// <summary><paramref name="commandAndArgs"/>[0] is the executable; the rest are passed via <see cref="ProcessStartInfo.ArgumentList"/> (no manual quoting/escaping).</summary>
    public static ChildProcess Start(IReadOnlyList<string> commandAndArgs)
    {
        ArgumentNullException.ThrowIfNull(commandAndArgs);
        if (commandAndArgs.Count == 0)
            throw new ArgumentException("A child command requires at least one token (the executable).", nameof(commandAndArgs));

        var psi = new ProcessStartInfo(commandAndArgs[0])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };
        for (int i = 1; i < commandAndArgs.Count; i++)
            psi.ArgumentList.Add(commandAndArgs[i]);

        Process process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        return new ChildProcess(process);
    }

    public int Id => _process.Id;

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public Stream StandardError => _process.StandardError.BaseStream;

    public int WaitForExit()
    {
        _process.WaitForExit();
        return _process.ExitCode;
    }

    public void RequestCancel(TimeSpan gracePeriod)
    {
        TryForwardCtrlC();

        // The escalation runs on its own thread so this call itself never
        // blocks the caller (a console CancelKeyPress handler must return
        // promptly, and a test may call this from its own thread too).
        var escalate = new Thread(() =>
        {
            try
            {
                if (!_process.WaitForExit((int)Math.Max(0, gracePeriod.TotalMilliseconds)))
                    _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited between the check and the kill -- fine.
            }
            catch (Win32Exception)
            {
                // Lost the race to terminate/access the process -- fine, the
                // goal (not orphaned) is already satisfied either way.
            }
        })
        { IsBackground = true };
        escalate.Start();
    }

    private void TryForwardCtrlC()
    {
        try { GenerateConsoleCtrlEvent(CtrlCEvent, 0); }
        catch { /* Best-effort only -- see type-level remarks. */ }
    }

    public void Dispose() => _process.Dispose();

    private const uint CtrlCEvent = 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
}
