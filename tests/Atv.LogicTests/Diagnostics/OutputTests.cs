using System.Text.Json;
using System.Text.Json.Serialization;
using Atv.Diagnostics;

namespace Atv.LogicTests.Diagnostics;

/// <summary>FAIL-2's stdout/stderr discipline: stdout = data, stderr = diagnostics, and the `--json` mutating-verb shape.</summary>
[TestClass]
public sealed class OutputTests
{
    [TestMethod]
    public void Data_NonJsonMode_WritesLineToStdout()
    {
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: false);

        output.Data("hello");

        StringAssert.Contains(stdout.ToString(), "hello");
    }

    [TestMethod]
    public void Data_JsonMode_WritesNothing()
    {
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);

        output.Data("hello");

        Assert.AreEqual("", stdout.ToString());
    }

    [TestMethod]
    public void Diagnostic_WritesToStderr_RegardlessOfJsonMode()
    {
        var stderrNonJson = new StringWriter();
        new Output(new StringWriter(), stderrNonJson, json: false).Diagnostic("oops");
        StringAssert.Contains(stderrNonJson.ToString(), "oops");

        var stderrJson = new StringWriter();
        new Output(new StringWriter(), stderrJson, json: true).Diagnostic("oops");
        StringAssert.Contains(stderrJson.ToString(), "oops");
    }

    [TestMethod]
    public void Diagnostic_NeverWritesToStdout()
    {
        var stdout = new StringWriter();
        new Output(stdout, new StringWriter(), json: false).Diagnostic("oops");
        Assert.AreEqual("", stdout.ToString());
    }

    [TestMethod]
    public void MutatingResult_NonJsonMode_WritesNothing()
    {
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: false);

        output.MutatingResult(true, "created");

        Assert.AreEqual("", stdout.ToString());
    }

    [TestMethod]
    public void MutatingResult_JsonMode_Success_WritesOkTrueAndReason()
    {
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);

        output.MutatingResult(true, "created h1");

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.IsTrue(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual("created h1", doc.RootElement.GetProperty("reason").GetString());
    }

    [TestMethod]
    public void MutatingResult_JsonMode_Failure_WritesOkFalseAndReason()
    {
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);

        output.MutatingResult(false, "refused: unsafe combo");

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.IsFalse(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual("refused: unsafe combo", doc.RootElement.GetProperty("reason").GetString());
    }

    [TestMethod]
    public void WriteJson_NonJsonMode_WritesNothing()
    {
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: false);

        output.WriteJson(new Dummy("a", 1), DummyJsonContext.Default.Dummy);

        Assert.AreEqual("", stdout.ToString());
    }

    [TestMethod]
    public void WriteJson_JsonMode_WritesSerializedValue_UsingCallerSuppliedTypeInfo()
    {
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);

        output.WriteJson(new Dummy("list-row", 42), DummyJsonContext.Default.Dummy);

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.AreEqual("list-row", doc.RootElement.GetProperty("A").GetString());
        Assert.AreEqual(42, doc.RootElement.GetProperty("B").GetInt32());
    }

}

// A tiny self-contained test-only DTO + source-gen context, standing in for a
// future verb's own `--json` shape (list's task array / doctor's report),
// proving Output.WriteJson<T> works with ANY caller-supplied JsonTypeInfo<T>
// without Output needing to know about it. Top-level (not nested in
// OutputTests) so the source generator doesn't need OutputTests itself to be
// declared `partial`.
public sealed record Dummy(string A, int B);

[JsonSerializable(typeof(Dummy))]
public partial class DummyJsonContext : JsonSerializerContext
{
}
