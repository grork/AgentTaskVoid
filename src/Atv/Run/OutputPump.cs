using System.Text;

namespace Atv.Run;

/// <summary>
/// ERGO-5's "decoupled reader" half: drains <paramref name="source"/> and
/// mirrors every byte, BYTE-FOR-BYTE UNTOUCHED, to <paramref name="mirror"/>
/// at full speed on the calling thread (transparency -- the terminal mirror
/// is never decoded/re-encoded, so it survives invalid-UTF8/binary-ish
/// output unmodified). INDEPENDENTLY, the same bytes are decoded (UTF-8,
/// console-default-with-replacement -- .NET's <see cref="Encoding.UTF8"/>
/// already replaces invalid sequences rather than throwing) and split into
/// completed lines for <paramref name="onLine"/>. Splits ONLY on <c>\n</c> --
/// a trailing <c>\r</c> (CRLF) or embedded <c>\r</c> (progress-bar overwrite)
/// is <see cref="LineHygiene"/>'s job, not this type's.
///
/// A caller runs <see cref="Pump"/> for each of the child's two streams on
/// its OWN thread (<c>ChildProcess.StandardOutput</c>/<c>StandardError</c>)
/// so stdout/stderr drain concurrently, and <paramref name="onLine"/> itself
/// must be cheap/non-blocking (just pushes into <see cref="StepPublisher"/>'s
/// in-memory rolling buffer) -- the actual debounced task-write I/O runs on
/// a THIRD, fully independent thread, so a chatty child never blocks on a
/// full pipe buffer waiting for that I/O (ERGO-5's correctness structure,
/// not polish).
/// </summary>
public static class OutputPump
{
    private const int ReadBufferSize = 4096;

    public static void Pump(Stream source, Stream mirror, Action<string> onLine)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(onLine);

        var decoder = Encoding.UTF8.GetDecoder();
        byte[] readBuffer = new byte[ReadBufferSize];
        char[] charBuffer = new char[ReadBufferSize]; // UTF-8 decoding never yields more chars than input bytes.
        var line = new StringBuilder();

        int read;
        while ((read = source.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            mirror.Write(readBuffer, 0, read);
            mirror.Flush();

            int charCount = decoder.GetChars(readBuffer, 0, read, charBuffer, 0, flush: false);
            for (int i = 0; i < charCount; i++)
            {
                char c = charBuffer[i];
                if (c == '\n')
                {
                    onLine(line.ToString());
                    line.Clear();
                }
                else
                {
                    line.Append(c);
                }
            }
        }

        // A final line with no trailing newline (very common -- the last
        // line a child writes before exiting) is still real content.
        if (line.Length > 0)
            onLine(line.ToString());
    }
}
