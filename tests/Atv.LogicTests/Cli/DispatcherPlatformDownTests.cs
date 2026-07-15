using Atv.Diagnostics;

namespace Atv.LogicTests.Cli;

/// <summary>
/// AC3: "the hook can never break the host" -- a missing/failing platform
/// (fake configured unavailable, or identity absent) leaves every verb
/// exit-0-silent by default, with exactly one durable log entry, and NO
/// store write attempted. Uses <c>working</c> (v2's create-or-adopt-and-upsert
/// verb, the successor to v1 `start`) as the representative upserting verb.
/// </summary>
[TestClass]
public sealed class DispatcherPlatformDownTests
{
    [TestMethod]
    public void Working_NoIdentity_NonStrict_ExitsZero_Silent_LogsOneEntry_NoWrite()
    {
        using var h = new DispatcherHarness { HasIdentity = false };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "working", "h1", "--title", "T");

        Assert.AreEqual(0, exit);
        Assert.AreEqual("", h.Stdout.ToString());
        Assert.AreEqual("", h.Stderr.ToString());
        Assert.IsEmpty(h.Store.FindAll());
        Assert.HasCount(1, h.LogEntriesExcludingTrace());
        Assert.AreEqual("working", h.LogEntriesExcludingTrace()[0].Verb);
    }

    [TestMethod]
    public void Working_NoIdentity_Strict_ReturnsIdentityNotRegisteredExitCode()
    {
        using var h = new DispatcherHarness { HasIdentity = false };
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "working", "h1");

        Assert.AreEqual((int)FailureKind.IdentityNotRegistered, exit);
    }

    [TestMethod]
    public void Working_ApiUnsupported_NonStrict_ExitsZero_Silent_LogsOneEntry_NoWrite()
    {
        using var h = new DispatcherHarness();
        h.Store.Supported = false;
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "working", "h1");

        Assert.AreEqual(0, exit);
        Assert.IsEmpty(h.Store.FindAll());
        Assert.HasCount(1, h.LogEntriesExcludingTrace());
    }

    [TestMethod]
    public void Working_ApiUnsupported_Strict_ReturnsApiUnavailableExitCode()
    {
        using var h = new DispatcherHarness();
        h.Store.Supported = false;
        var dispatcher = h.BuildDispatcher(strict: true);

        int exit = h.Run(dispatcher, "working", "h1");

        Assert.AreEqual((int)FailureKind.ApiUnavailable, exit);
    }

    [TestMethod]
    public void Remove_NoIdentity_NonStrict_ExitsZero_Silent()
    {
        using var h = new DispatcherHarness { HasIdentity = false };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "remove", "h1");

        Assert.AreEqual(0, exit);
        Assert.HasCount(1, h.LogEntriesExcludingTrace());
    }

    [TestMethod]
    public void Activity_NoIdentity_NonStrict_ExitsZero_Silent()
    {
        using var h = new DispatcherHarness { HasIdentity = false };
        var dispatcher = h.BuildDispatcher();

        int exit = h.Run(dispatcher, "activity", "h1", "--kind", "shell", "--label", "npm test");

        Assert.AreEqual(0, exit);
        Assert.HasCount(1, h.LogEntriesExcludingTrace());
    }

    [TestMethod]
    public void Json_PlatformDown_ReportsOkFalse_StillExitsZeroByDefault()
    {
        using var h = new DispatcherHarness { HasIdentity = false };
        var dispatcher = h.BuildDispatcher(json: true);

        int exit = h.Run(dispatcher, "working", "h1");

        Assert.AreEqual(0, exit);
        StringAssert.Contains(h.Stdout.ToString(), "\"ok\":false");
    }
}
