namespace HostEventRecorder.Tests;

[TestClass]
public sealed class ArgvParserTests
{
    [TestMethod]
    public void RequiredFlagsOnly_ParsesHostAndEvent_OptionalFieldsNull()
    {
        var options = ArgvParser.Parse(["--host", "claude-code", "--event", "PostToolUse"]);

        Assert.AreEqual("claude-code", options.Host);
        Assert.AreEqual("PostToolUse", options.Event);
        Assert.IsNull(options.Session);
        Assert.IsNull(options.CaptureDir);
    }

    [TestMethod]
    public void AllFlags_ParsesEachIndependentOfOrder()
    {
        var options = ArgvParser.Parse(["--session", "s1", "--capture-dir", @"C:\caps", "--event", "Notification", "--host", "claude-code"]);

        Assert.AreEqual("claude-code", options.Host);
        Assert.AreEqual("Notification", options.Event);
        Assert.AreEqual("s1", options.Session);
        Assert.AreEqual(@"C:\caps", options.CaptureDir);
    }

    [TestMethod]
    public void MissingHost_Throws()
    {
        var ex = Assert.ThrowsExactly<ArgvException>(() => ArgvParser.Parse(["--event", "PostToolUse"]));
        StringAssert.Contains(ex.Message, "--host");
    }

    [TestMethod]
    public void MissingEvent_Throws()
    {
        var ex = Assert.ThrowsExactly<ArgvException>(() => ArgvParser.Parse(["--host", "claude-code"]));
        StringAssert.Contains(ex.Message, "--event");
    }

    [TestMethod]
    public void UnrecognizedFlag_Throws()
    {
        var ex = Assert.ThrowsExactly<ArgvException>(() =>
            ArgvParser.Parse(["--host", "claude-code", "--event", "e", "--bogus", "x"]));
        StringAssert.Contains(ex.Message, "--bogus");
    }

    [TestMethod]
    public void FlagMissingValue_Throws()
    {
        var ex = Assert.ThrowsExactly<ArgvException>(() => ArgvParser.Parse(["--host"]));
        StringAssert.Contains(ex.Message, "--host");
    }
}
