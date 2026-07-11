using Atv.Diagnostics;

namespace Atv.LogicTests.Diagnostics;

/// <summary>
/// INFRA-13's outcome mapping onto the FAIL-2 exit vocabulary: identity
/// absence maps to <see cref="FailureKind.IdentityNotRegistered"/> (3), API
/// unsupported (with identity present) maps to
/// <see cref="FailureKind.ApiUnavailable"/> (2), both present is success --
/// all via injected delegates, no real package identity or WinRT API needed.
/// </summary>
[TestClass]
public sealed class CapabilityTests
{
    [TestMethod]
    public void Check_NoIdentity_ReturnsIdentityNotRegistered()
    {
        var result = Capability.Check(hasIdentity: () => false, isSupported: () => true);

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(FailureKind.IdentityNotRegistered, result.Kind);
    }

    [TestMethod]
    public void Check_IdentityPresent_ApiUnsupported_ReturnsApiUnavailable()
    {
        var result = Capability.Check(hasIdentity: () => true, isSupported: () => false);

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(FailureKind.ApiUnavailable, result.Kind);
    }

    [TestMethod]
    public void Check_IdentityPresentAndApiSupported_ReturnsSuccess()
    {
        var result = Capability.Check(hasIdentity: () => true, isSupported: () => true);

        Assert.IsTrue(result.Ok);
    }

    [TestMethod]
    public void Check_NoIdentity_DoesNotEvenCallIsSupported()
    {
        // Identity-first ordering: asking IsSupported() is meaningless
        // without identity, so it should never be evaluated in that case.
        bool isSupportedCalled = false;
        Capability.Check(hasIdentity: () => false, isSupported: () => { isSupportedCalled = true; return true; });

        Assert.IsFalse(isSupportedCalled);
    }

}
