namespace HostEventRecorder.Tests;

[TestClass]
public sealed class MutexNamingTests
{
    [TestMethod]
    public void SameFile_CaseDifference_ResolvesToSameMutexName()
    {
        string a = MutexNaming.DeriveMutexName(@"C:\Users\x\captures\session-1.jsonl");
        string b = MutexNaming.DeriveMutexName(@"c:\users\X\CAPTURES\Session-1.JSONL");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void SameFile_ForwardVsBackSlash_ResolvesToSameMutexName()
    {
        string a = MutexNaming.DeriveMutexName(@"C:\Users\x\captures\session-1.jsonl");
        string b = MutexNaming.DeriveMutexName("C:/Users/x/captures/session-1.jsonl");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void SameFile_TrailingSeparator_ResolvesToSameMutexName()
    {
        string a = MutexNaming.DeriveMutexName(@"C:\Users\x\captures\session-1.jsonl");
        string b = MutexNaming.DeriveMutexName(@"C:\Users\x\captures\session-1.jsonl\");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void SameFile_RelativeVsAbsolute_ResolvesToSameMutexName()
    {
        // Deliberately does NOT call Directory.SetCurrentDirectory -- that's
        // process-wide mutable state and this suite runs method-level
        // parallel (MSTestSettings.cs). Instead: resolve the SAME relative
        // path "by hand" against the (unchanged) real cwd via Path.Combine,
        // and confirm MutexNaming.DeriveMutexName treats the relative form
        // and that manually-absolutized form as the same file -- exactly
        // the equivalence GetFullPath itself performs internally.
        string cwd = Directory.GetCurrentDirectory();
        const string relativeSegment = @"hostrec-mutex-relative-test\session-1.jsonl";
        string absoluteEquivalent = Path.Combine(cwd, relativeSegment);

        string relative = MutexNaming.DeriveMutexName(relativeSegment);
        string absolute = MutexNaming.DeriveMutexName(absoluteEquivalent);

        Assert.AreEqual(absolute, relative);
    }

    [TestMethod]
    public void DifferentFiles_ResolveToDifferentMutexNames()
    {
        string a = MutexNaming.DeriveMutexName(@"C:\Users\x\captures\session-1.jsonl");
        string b = MutexNaming.DeriveMutexName(@"C:\Users\x\captures\session-2.jsonl");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void MutexName_HasLocalPrefix_AndNoPathSeparatorsAfterIt()
    {
        string name = MutexNaming.DeriveMutexName(@"C:\Users\x\captures\session-1.jsonl");

        StringAssert.StartsWith(name, Constants.MutexNamePrefix);
        string suffix = name[Constants.MutexNamePrefix.Length..];
        Assert.IsFalse(suffix.Contains('\\') || suffix.Contains('/'), "the hashed suffix must contain no path separators -- named-mutex names can't contain them.");
    }

    [TestMethod]
    public void MutexName_ActuallyConstructibleAsARealNamedMutex()
    {
        string name = MutexNaming.DeriveMutexName(@"C:\Users\x\captures\session-1.jsonl");

        using var mutex = new Mutex(initiallyOwned: false, name, out bool createdNew);
        Assert.IsTrue(createdNew);
    }
}
