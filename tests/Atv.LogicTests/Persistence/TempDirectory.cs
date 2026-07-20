namespace Codevoid.AgentTaskVoid.LogicTests.Persistence;

/// <summary>
/// A unique-per-instance temp directory, deleted on <see cref="Dispose"/>.
/// Not created on disk until first used by the code under test -- mirrors
/// production, where <c>SidecarStore</c>/<c>RecycleBin</c> lazily create
/// their own directory on first write. Each instance gets a fresh GUID
/// segment, so tests using this are parallel-safe (phase-04 acceptance
/// criterion 5) with no shared state between them.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "atv-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup -- a leftover temp dir is harmless.
        }
    }
}
