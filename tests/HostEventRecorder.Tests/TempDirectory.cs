namespace HostEventRecorder.Tests;

/// <summary>
/// A unique-per-instance temp directory, deleted on <see cref="Dispose"/>.
/// Mirrors tests/Atv.LogicTests/Persistence/TempDirectory.cs -- each
/// instance gets a fresh GUID segment, so tests using this are
/// parallel-safe with no shared state between them.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "host-event-recorder-tests", Guid.NewGuid().ToString("N"));

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
