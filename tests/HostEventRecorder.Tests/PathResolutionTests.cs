namespace HostEventRecorder.Tests;

[TestClass]
public sealed class PathResolutionTests
{
    private const string BaseDir = @"C:\exe\dir";

    [TestMethod]
    public void NoArgNoEnv_FallsBackToBaseDirectoryPlusDefaultCaptureDirName()
    {
        string result = PathResolution.ResolveCaptureDir(null, null, BaseDir);
        Assert.AreEqual(Path.Combine(BaseDir, Constants.DefaultCaptureDirName), result);
    }

    [TestMethod]
    public void EnvOnly_EnvWins()
    {
        string result = PathResolution.ResolveCaptureDir(null, @"D:\driver\captures", BaseDir);
        Assert.AreEqual(@"D:\driver\captures", result);
    }

    [TestMethod]
    public void ArgvOnly_ArgvWins()
    {
        string result = PathResolution.ResolveCaptureDir(@"E:\manual\captures", null, BaseDir);
        Assert.AreEqual(@"E:\manual\captures", result);
    }

    [TestMethod]
    public void ArgvAndEnvBothSet_ArgvTakesPrecedence()
    {
        string result = PathResolution.ResolveCaptureDir(@"E:\manual\captures", @"D:\driver\captures", BaseDir);
        Assert.AreEqual(@"E:\manual\captures", result);
    }

    [TestMethod]
    public void EmptyStringOverrides_TreatedAsAbsent()
    {
        // An empty (but non-null) env/argv value must not "win" over the
        // exe-adjacent fallback -- an empty string is not an explicit path.
        string result = PathResolution.ResolveCaptureDir("", "", BaseDir);
        Assert.AreEqual(Path.Combine(BaseDir, Constants.DefaultCaptureDirName), result);
    }

    [TestMethod]
    public void ResolveLogFilePath_EmbedsSessionIdInFilename()
    {
        string path = PathResolution.ResolveLogFilePath(@"C:\caps", "2026-07-12-demo");
        Assert.AreEqual(@"C:\caps\session-2026-07-12-demo.jsonl", path);
    }

    [TestMethod]
    public void ResolveLogFilePath_SanitizesInvalidFilenameCharactersInSessionId()
    {
        // ':' is filesystem-invalid on Windows and reliably present in
        // Path.GetInvalidFileNameChars() -- more legible in a failure
        // message than an arbitrary array-indexed control character.
        CollectionAssert.Contains(Path.GetInvalidFileNameChars(), ':');
        string path = PathResolution.ResolveLogFilePath(@"C:\caps", "weird:id");

        Assert.AreEqual(@"C:\caps\session-weird_id.jsonl", path);
    }
}
