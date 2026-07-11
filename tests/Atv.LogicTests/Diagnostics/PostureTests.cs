using System.Text.Json;
using Atv.Diagnostics;
using Atv.LogicTests.Persistence;

namespace Atv.LogicTests.Diagnostics;

/// <summary>
/// FAIL-1/FAIL-2's non-disruptive wrapper (phase-06 AC2): default mode always
/// exits 0 with one durable log entry on failure; `--strict` maps failure
/// kinds onto the stable exit vocabulary and writes stderr; `--json` reports
/// `{"ok":...}` while still exiting 0; `--verbose` adds live stderr detail
/// and minimal success logging.
/// </summary>
[TestClass]
public sealed class PostureTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private static (Posture Posture, StringWriter Stdout, StringWriter Stderr, FailureLog Log, TempDirectory Dir) Build(
        bool strict = false, bool json = false, bool verbose = false)
    {
        var dir = new TempDirectory();
        var log = new FailureLog(Path.Combine(dir.Path, "atv.log"), maxBytes: 1_000_000, maxAge: TimeSpan.FromDays(14));
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var output = new Output(stdout, stderr, json);
        var posture = new Posture(log, output, strict, verbose);
        return (posture, stdout, stderr, log, dir);
    }

    [TestMethod]
    public void Run_DefaultMode_FailingOperation_ExitsZero_NothingOnStdout_OneLogEntry()
    {
        var (posture, stdout, stderr, log, dir) = Build();
        using (dir)
        {
            int exit = posture.Run("step", "h1", () => VerbResult.Failure(FailureKind.Generic, "boom"), Now);

            Assert.AreEqual(0, exit);
            Assert.AreEqual("", stdout.ToString());
            Assert.AreEqual("", stderr.ToString());
            Assert.HasCount(1, log.ReadAll());
            Assert.AreEqual("boom", log.ReadAll()[0].Error);
            Assert.AreEqual("h1", log.ReadAll()[0].Handle);
            Assert.AreEqual("step", log.ReadAll()[0].Verb);
        }
    }

    [TestMethod]
    [DataRow(FailureKind.Generic, 1)]
    [DataRow(FailureKind.ApiUnavailable, 2)]
    [DataRow(FailureKind.IdentityNotRegistered, 3)]
    [DataRow(FailureKind.InvalidArguments, 4)]
    public void Run_Strict_FailingOperation_ReturnsMappedExitCode_AndWritesStderr(FailureKind kind, int expectedExitCode)
    {
        var (posture, stdout, stderr, log, dir) = Build(strict: true);
        using (dir)
        {
            int exit = posture.Run("start", "h1", () => VerbResult.Failure(kind, "reason text"), Now);

            Assert.AreEqual(expectedExitCode, exit);
            StringAssert.Contains(stderr.ToString(), "reason text");
            Assert.AreEqual("", stdout.ToString());
            Assert.HasCount(1, log.ReadAll());
        }
    }

    [TestMethod]
    public void Run_Strict_SuccessfulOperation_ExitsZero_NoStderr()
    {
        var (posture, stdout, stderr, log, dir) = Build(strict: true);
        using (dir)
        {
            int exit = posture.Run("done", "h1", () => VerbResult.Success("completed"), Now);

            Assert.AreEqual(0, exit);
            Assert.AreEqual("", stderr.ToString());
            Assert.IsEmpty(log.ReadAll());
        }
    }

    [TestMethod]
    public void Run_Json_FailingOperation_ExitsZero_WritesOkFalseReason()
    {
        var (posture, stdout, stderr, log, dir) = Build(json: true);
        using (dir)
        {
            int exit = posture.Run("step", "h1", () => VerbResult.Failure(FailureKind.InvalidArguments, "bad args"), Now);

            Assert.AreEqual(0, exit);
            var doc = JsonDocument.Parse(stdout.ToString());
            Assert.IsFalse(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.AreEqual("bad args", doc.RootElement.GetProperty("reason").GetString());
            Assert.HasCount(1, log.ReadAll());
        }
    }

    [TestMethod]
    public void Run_JsonAndStrict_FailingOperation_WritesOkFalseShape_AndReturnsMappedExitCode()
    {
        // The parked phase-06 combination test: --json and --strict are orthogonal
        // (Output.Json governs the stdout shape; Posture's strict flag governs the
        // exit code) -- both must apply simultaneously, not just individually.
        var (posture, stdout, stderr, log, dir) = Build(strict: true, json: true);
        using (dir)
        {
            int exit = posture.Run("start", "h1", () => VerbResult.Failure(FailureKind.ApiUnavailable, "no API"), Now);

            Assert.AreEqual((int)FailureKind.ApiUnavailable, exit);
            var doc = JsonDocument.Parse(stdout.ToString());
            Assert.IsFalse(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.AreEqual("no API", doc.RootElement.GetProperty("reason").GetString());
            StringAssert.Contains(stderr.ToString(), "no API");
            Assert.HasCount(1, log.ReadAll());
        }
    }

    [TestMethod]
    public void Run_JsonAndStrict_SuccessfulOperation_WritesOkTrueShape_AndExitsZero()
    {
        var (posture, stdout, stderr, log, dir) = Build(strict: true, json: true);
        using (dir)
        {
            int exit = posture.Run("done", "h1", () => VerbResult.Success("completed"), Now);

            Assert.AreEqual(0, exit);
            var doc = JsonDocument.Parse(stdout.ToString());
            Assert.IsTrue(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.AreEqual("", stderr.ToString());
        }
    }

    [TestMethod]
    public void Run_Json_SuccessfulOperation_WritesOkTrue()
    {
        var (posture, stdout, stderr, log, dir) = Build(json: true);
        using (dir)
        {
            int exit = posture.Run("start", "h1", () => VerbResult.Success("created"), Now);

            Assert.AreEqual(0, exit);
            var doc = JsonDocument.Parse(stdout.ToString());
            Assert.IsTrue(doc.RootElement.GetProperty("ok").GetBoolean());
        }
    }

    [TestMethod]
    public void Run_SuccessfulOperation_DefaultMode_ExitsZero_NoLogEntry_NoStdout()
    {
        var (posture, stdout, stderr, log, dir) = Build();
        using (dir)
        {
            int exit = posture.Run("remove", "h1", () => VerbResult.Success(), Now);

            Assert.AreEqual(0, exit);
            Assert.AreEqual("", stdout.ToString());
            Assert.IsEmpty(log.ReadAll());
        }
    }

    [TestMethod]
    public void Run_UnhandledException_TreatedAsGenericFailure_NonDisruptiveByDefault()
    {
        var (posture, stdout, stderr, log, dir) = Build();
        using (dir)
        {
            int exit = posture.Run("step", "h1", () => throw new InvalidOperationException("kaboom"), Now);

            Assert.AreEqual(0, exit);
            var entries = log.ReadAll();
            Assert.HasCount(1, entries);
            StringAssert.Contains(entries[0].Error, "kaboom");
        }
    }

    [TestMethod]
    public void Run_UnhandledException_Strict_MapsToGenericExitCodeOne()
    {
        var (posture, stdout, stderr, log, dir) = Build(strict: true);
        using (dir)
        {
            int exit = posture.Run("step", "h1", () => throw new InvalidOperationException("kaboom"), Now);
            Assert.AreEqual(1, exit);
        }
    }

    [TestMethod]
    public void Run_Verbose_SuccessIsLogged_Minimal()
    {
        var (posture, stdout, stderr, log, dir) = Build(verbose: true);
        using (dir)
        {
            posture.Run("step", "h1", () => VerbResult.Success("advanced to step 2"), Now);

            var entries = log.ReadAll();
            Assert.HasCount(1, entries);
            Assert.AreEqual("advanced to step 2", entries[0].Error);
            Assert.IsNotEmpty(stderr.ToString());
        }
    }

    [TestMethod]
    public void Run_NonVerbose_SuccessIsNeverLogged()
    {
        var (posture, stdout, stderr, log, dir) = Build(verbose: false);
        using (dir)
        {
            posture.Run("step", "h1", () => VerbResult.Success("advanced"), Now);
            Assert.IsEmpty(log.ReadAll());
        }
    }

    [TestMethod]
    public void Run_Verbose_FailureAlsoWritesStderr_EvenWithoutStrict()
    {
        var (posture, stdout, stderr, log, dir) = Build(verbose: true, strict: false);
        using (dir)
        {
            int exit = posture.Run("step", "h1", () => VerbResult.Failure(FailureKind.Generic, "verbose-visible failure"), Now);

            Assert.AreEqual(0, exit, "non-strict must still exit 0 even with --verbose");
            StringAssert.Contains(stderr.ToString(), "verbose-visible failure");
        }
    }

    [TestMethod]
    public void RunQuery_Failure_NeverEmitsTheMutatingResultShape_EvenUnderJson()
    {
        // phase 10: list/doctor each print their OWN --json shape inside the
        // body itself -- RunQuery must never fall back to the generic
        // {"ok":..,"reason":..} mutating-verb stamp Run() emits.
        var (posture, stdout, stderr, log, dir) = Build(json: true);
        using (dir)
        {
            int exit = posture.RunQuery("list", null, () => VerbResult.Failure(FailureKind.IdentityNotRegistered, "no identity"), Now);

            Assert.AreEqual(0, exit);
            Assert.AreEqual("", stdout.ToString(), "RunQuery must not write the body's stdout itself -- and must never add the generic ok/reason stamp.");
            Assert.HasCount(1, log.ReadAll());
        }
    }

    [TestMethod]
    public void RunQuery_Success_NeverEmitsTheMutatingResultShape_EvenUnderJson()
    {
        var (posture, stdout, stderr, log, dir) = Build(json: true);
        using (dir)
        {
            int exit = posture.RunQuery("list", null, () =>
            {
                stdout.Write("[]"); // the body writes its own shape
                return VerbResult.Success("0 task(s).");
            }, Now);

            Assert.AreEqual(0, exit);
            Assert.AreEqual("[]", stdout.ToString(), "only the body's own write should appear -- no additional {\"ok\":..} stamp.");
            Assert.IsEmpty(log.ReadAll());
        }
    }

    [TestMethod]
    public void RunQuery_Strict_StillMapsFailureKindToExitCode_AndWritesStderr()
    {
        var (posture, stdout, stderr, log, dir) = Build(strict: true);
        using (dir)
        {
            int exit = posture.RunQuery("doctor", null, () => VerbResult.Failure(FailureKind.ApiUnavailable, "api down"), Now);

            Assert.AreEqual((int)FailureKind.ApiUnavailable, exit);
            StringAssert.Contains(stderr.ToString(), "api down");
        }
    }

    [TestMethod]
    public void VerbResult_Success_DefaultReasonIsEmpty()
    {
        Assert.AreEqual("", VerbResult.Success().Reason);
    }
}
