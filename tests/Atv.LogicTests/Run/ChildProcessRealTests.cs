using System.Diagnostics;
using System.Text;
using Atv.Cli.Verbs;
using Atv.Config;
using Atv.Run;
using Atv.Store;

namespace Atv.LogicTests.Run;

/// <summary>
/// AC3 + AC4: the two acceptance criteria that explicitly require a REAL
/// spawned OS child process (no <see cref="FakeChildProcess"/>) --
/// <see cref="ChildProcess"/> itself, exercised the same way
/// <see cref="RunOrchestrator"/> drives it. A plain <c>Process.Start</c>
/// needs NO package identity, so this lives in the identity-free LogicTests
/// suite (matching phase 09's precedent of a real-process test living
/// outside AdapterTests when identity plays no part) rather than
/// <c>Atv.AdapterTests</c>.
/// </summary>
[TestClass]
public sealed class ChildProcessRealTests
{
    // ---- AC3: transparency with a REAL child -------------------------------

    /// <summary>
    /// A real <c>cmd.exe</c> child writes 4 known lines to stdout and 2 known
    /// lines to stderr (interleaved via per-command <c>1&gt;&amp;2</c>
    /// redirection so a naive implementation that merges the streams would
    /// scramble the order). Proves <see cref="OutputPump.Pump"/>, run on
    /// real OS pipes (not <see cref="FakeChildProcess"/>'s in-memory queue),
    /// mirrors bytes byte-for-byte AND preserves per-stream line order.
    /// Cross-stream interleaving is deliberately NOT asserted (the phase
    /// file only promises "unreordered PER STREAM" -- true OS pipe
    /// scheduling across two independent streams isn't ordered relative to
    /// each other).
    /// </summary>
    [TestMethod]
    public void Pump_RealChild_StdoutAndStderrBytesArriveUnmodifiedAndInOrderPerStream()
    {
        using var child = ChildProcess.Start(
        [
            "cmd.exe", "/c",
            "echo out-1&echo out-2&echo err-1 1>&2&echo out-3&echo err-2 1>&2&echo out-4",
        ]);

        var stdoutMirror = new MemoryStream();
        var stderrMirror = new MemoryStream();
        var stdoutLines = new List<string>();
        var stderrLines = new List<string>();

        var stdoutThread = new Thread(() => OutputPump.Pump(child.StandardOutput, stdoutMirror, l => { lock (stdoutLines) stdoutLines.Add(l); }))
        { IsBackground = true };
        var stderrThread = new Thread(() => OutputPump.Pump(child.StandardError, stderrMirror, l => { lock (stderrLines) stderrLines.Add(l); }))
        { IsBackground = true };
        stdoutThread.Start();
        stderrThread.Start();

        int exitCode = child.WaitForExit();
        Assert.IsTrue(stdoutThread.Join(TimeSpan.FromSeconds(10)));
        Assert.IsTrue(stderrThread.Join(TimeSpan.FromSeconds(10)));

        Assert.AreEqual(0, exitCode);

        // Per-stream order preserved, exactly as cmd's `echo` wrote it (CRLF
        // terminated -- OutputPump splits on \n only, so the decoded line
        // still carries its trailing \r; a bare space survives before the
        // stderr lines' \r because `1>&2` consumes only the token itself,
        // leaving the preceding space attached to the echoed text --
        // verified empirically, not guessed).
        CollectionAssert.AreEqual(new[] { "out-1\r", "out-2\r", "out-3\r", "out-4\r" }, stdoutLines);
        CollectionAssert.AreEqual(new[] { "err-1 \r", "err-2 \r" }, stderrLines);

        // The byte-for-byte mirror is untouched -- re-decoding it independently
        // of the pump's own line-splitting must reproduce the exact same bytes.
        Assert.AreEqual("out-1\r\nout-2\r\nout-3\r\nout-4\r\n", Encoding.UTF8.GetString(stdoutMirror.ToArray()));
        Assert.AreEqual("err-1 \r\nerr-2 \r\n", Encoding.UTF8.GetString(stderrMirror.ToArray()));
    }

    // ---- AC4: Ctrl+C with a REAL, long-running child -----------------------

    /// <summary>
    /// Drives a full <see cref="RunOrchestrator.Execute"/> lifecycle over a
    /// REAL, deliberately long-running child (<c>ping -n 30</c>, ~30s), then
    /// -- from the test's own thread, exactly mirroring what
    /// <see cref="RunVerb.Run"/>'s <c>Console.CancelKeyPress</c> handler
    /// does -- calls <see cref="IChildProcess.RequestCancel"/> mid-flight.
    /// Asserts the orchestrator unblocks well before the child's natural
    /// 30s runtime (proving the grace-period-then-kill escalation fired),
    /// the card lands on <c>fail</c> (never left stuck Running), and the
    /// child's OS process id no longer exists afterward (no orphan).
    ///
    /// <b>Environmental adaptation (deliberate, not a shortcut):</b> this
    /// test does NOT raise a real synthetic console CTRL_C_EVENT via
    /// <c>SetConsoleCtrlHandler</c>/OS signal injection against the test
    /// host itself -- <see cref="ChildProcess.RequestCancel"/>'s own
    /// <c>GenerateConsoleCtrlEvent(..., 0)</c> necessarily re-signals the
    /// ENTIRE console process group, which on some hosts (an interactive
    /// terminal) includes this very test process, and an MSTest host has no
    /// handler installed to survive that the way <see cref="RunVerb.Run"/>
    /// does. So this test installs the SAME <c>Console.CancelKeyPress</c>
    /// absorption <see cref="RunVerb.Run"/> installs before calling
    /// <see cref="IChildProcess.RequestCancel"/> -- i.e. it exercises the
    /// production code path (the real Win32 call, the real escalation
    /// timer, the real child process) through the exact same safety net
    /// production uses, rather than fabricating a synthetic signal that
    /// would either do nothing (headless host, no console) or risk
    /// terminating the test run (console-attached host). The "never orphan"
    /// guarantee this proves does not depend on whether the soft forward was
    /// actually delivered -- see <see cref="ChildProcess"/>'s own remarks.
    /// </summary>
    [TestMethod]
    public void RunOrchestrator_CtrlCDuringLongRunningRealChild_ChildExits_CardMarkedFail_NoOrphan()
    {
        using var h = new RunTestHarness();
        using var child = ChildProcess.Start(["cmd.exe", "/c", "ping -n 30 127.0.0.1 >nul"]);
        int childId = child.Id;

        Settings settings = Settings.Default with
        {
            RunUpdateDebounce = TimeSpan.FromMilliseconds(20),
            RunKeepAliveInterval = TimeSpan.FromSeconds(30),
        };

        int exitCode = int.MinValue;
        var orchestratorThread = new Thread(() =>
        {
            exitCode = RunOrchestrator.Execute(
                h.Ops, h.Engine, settings, () => DateTimeOffset.UtcNow, Thread.Sleep,
                "run-ctrlc-handle", "Long Task", new Uri("file:///icon.png"), new Uri("file:///deep-link"),
                child, Stream.Null, Stream.Null, DateTimeOffset.UtcNow);
        })
        { IsBackground = true };
        orchestratorThread.Start();

        // Give the orchestrator a moment to actually start the card and spin
        // up its reader/publisher threads -- mirrors the real gap between a
        // user launching `run` and pressing Ctrl+C.
        SpinWaitUntil(() => h.Ops.List().Count == 1, TimeSpan.FromSeconds(5));
        Assert.AreEqual(AppTaskState.Running, h.Ops.List().Single().State);

        ConsoleCancelEventHandler absorbOwnSignal = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += absorbOwnSignal;
        try
        {
            child.RequestCancel(TimeSpan.FromSeconds(2));
            Assert.IsTrue(orchestratorThread.Join(TimeSpan.FromSeconds(15)),
                "RunOrchestrator.Execute must unblock once the escalation force-kills the child -- well before the child's natural ~30s runtime.");
        }
        finally
        {
            Console.CancelKeyPress -= absorbOwnSignal;
        }

        Assert.AreNotEqual(0, exitCode, "a force-killed child's exit code must be nonzero, so the wrapper maps it to fail.");
        Assert.AreEqual(AppTaskState.Error, h.Ops.List().Single().State, "the card must land on fail, never left stuck Running.");
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(childId),
            "no orphan: the child's OS process id (and, via entireProcessTree kill, ping.exe underneath cmd.exe) must no longer exist.");
    }

    private static void SpinWaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Condition did not become true within the timeout.");
            Thread.Sleep(10);
        }
    }
}
