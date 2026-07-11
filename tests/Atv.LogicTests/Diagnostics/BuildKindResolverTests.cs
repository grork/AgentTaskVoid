using Atv;
using Atv.Diagnostics;

namespace Atv.LogicTests.Diagnostics;

/// <summary>
/// DIST-3's 2026-07-10 amendment: <see cref="BuildKindResolver"/> classifies
/// which of the build-kind-aware identity pools
/// (<c>build/Atv.Package.targets</c>' <c>AtvStampAppxManifest</c>) a raw
/// Identity Name string belongs to, and renders the unambiguous
/// <c>(dev)</c>/<c>(test)</c> console/log marker. Every case here is driven
/// by an INJECTED name string -- no package identity, no live Package.Current
/// call -- matching every other seam in this codebase (INFRA-8).
/// </summary>
[TestClass]
public sealed class BuildKindResolverTests
{
    [TestMethod]
    public void Resolve_Null_IsNoIdentity()
    {
        Assert.AreEqual(BuildKind.NoIdentity, BuildKindResolver.Resolve(null));
    }

    [TestMethod]
    public void Resolve_Empty_IsNoIdentity()
    {
        Assert.AreEqual(BuildKind.NoIdentity, BuildKindResolver.Resolve(""));
    }

    [TestMethod]
    public void Resolve_ExactBrandName_IsRelease()
    {
        // The clean, pathhash-free Name build/Atv.Package.targets stamps under
        // AtvReleaseIdentity=true (DIST-3 amendment) -- e.g. "Agentaskvoid".
        Assert.AreEqual(BuildKind.Release, BuildKindResolver.Resolve(Branding.Name));
    }

    [TestMethod]
    public void Resolve_BrandPlusPathHash_IsDev()
    {
        // The default (UNCHANGED) dev-interactive Name shape, e.g.
        // "Agentaskvoid-bbbb1168" -- CRITICAL that this stays Dev, never Release.
        Assert.AreEqual(BuildKind.Dev, BuildKindResolver.Resolve($"{Branding.Name}-bbbb1168"));
    }

    [TestMethod]
    public void Resolve_ReltestSmokeVariant_IsDev()
    {
        // The phase-12 throwaway "-reltest" smoke identity (AtvVerifyIdentity=true)
        // is developer-machine-local, same bucket as ordinary dev-interactive.
        Assert.AreEqual(BuildKind.Dev, BuildKindResolver.Resolve($"{Branding.Name}-reltest"));
    }

    [TestMethod]
    public void Resolve_TestPoolName_IsTest()
    {
        // INFRA-16's per-worktree adapter-test pool shape: "<brand>.Test.<hash>".
        Assert.AreEqual(BuildKind.Test, BuildKindResolver.Resolve($"{Branding.Name}.Test.abcd1234"));
    }

    [TestMethod]
    public void Resolve_UnrelatedName_IsDev_NotMisclassifiedAsRelease()
    {
        // Anything that isn't the exact brand string and isn't the Test prefix
        // falls into Dev, per the documented "otherwise -> Dev" rule -- it must
        // never be silently treated as Release just because it starts with the brand.
        Assert.AreEqual(BuildKind.Dev, BuildKindResolver.Resolve($"{Branding.Name}Extra"));
    }

    [TestMethod]
    public void Marker_Release_IsNull_UnmarkedShipOutput()
    {
        Assert.IsNull(BuildKindResolver.Marker(BuildKind.Release));
    }

    [TestMethod]
    public void Marker_NoIdentity_IsNull_Documented()
    {
        Assert.IsNull(BuildKindResolver.Marker(BuildKind.NoIdentity));
    }

    [TestMethod]
    public void Marker_Dev_IsParenDev()
    {
        Assert.AreEqual("(dev)", BuildKindResolver.Marker(BuildKind.Dev));
    }

    [TestMethod]
    public void Marker_Test_IsParenTest()
    {
        Assert.AreEqual("(test)", BuildKindResolver.Marker(BuildKind.Test));
    }

    [TestMethod]
    public void MarkerFromName_ConvenienceOverload_MatchesResolveThenMarker()
    {
        string name = $"{Branding.Name}.Test.deadbeef";
        Assert.AreEqual(BuildKindResolver.Marker(BuildKindResolver.Resolve(name)), BuildKindResolver.Marker(name));
        Assert.AreEqual("(test)", BuildKindResolver.Marker(name));
    }

    [TestMethod]
    public void FormatVersionLine_Release_NoSuffix()
    {
        string line = BuildKindResolver.FormatVersionLine("1.2.3.4", Branding.Name);
        Assert.AreEqual("1.2.3.4", line);
    }

    [TestMethod]
    public void FormatVersionLine_Dev_AppendsMarker()
    {
        string line = BuildKindResolver.FormatVersionLine("1.2.3.4", $"{Branding.Name}-bbbb1168");
        Assert.AreEqual("1.2.3.4 (dev)", line);
    }

    [TestMethod]
    public void FormatVersionLine_Test_AppendsMarker()
    {
        string line = BuildKindResolver.FormatVersionLine("1.2.3.4", $"{Branding.Name}.Test.abcd1234");
        Assert.AreEqual("1.2.3.4 (test)", line);
    }

    [TestMethod]
    public void FormatVersionLine_NoIdentity_NoSuffix()
    {
        string line = BuildKindResolver.FormatVersionLine("1.2.3.4", null);
        Assert.AreEqual("1.2.3.4", line);
    }

}
