using System.Text;
using Atv.Run;

namespace Atv.LogicTests.Run;

/// <summary>
/// Fake-backed (injected <see cref="MemoryStream"/>s, no real process):
/// proves <see cref="OutputPump.Pump"/> mirrors bytes untouched AND splits
/// decoded lines correctly, independent of any child/process concern (that's
/// <c>ChildProcessRealTests</c>' job, AC3).
/// </summary>
[TestClass]
public sealed class OutputPumpTests
{
    [TestMethod]
    public void Pump_MirrorsBytesByteForByteUnmodified()
    {
        byte[] input = Encoding.UTF8.GetBytes("line one\nline two\n");
        using var source = new MemoryStream(input);
        using var mirror = new MemoryStream();

        OutputPump.Pump(source, mirror, _ => { });

        CollectionAssert.AreEqual(input, mirror.ToArray());
    }

    [TestMethod]
    public void Pump_SplitsOnNewlineOnly_LinesInOrder()
    {
        byte[] input = Encoding.UTF8.GetBytes("alpha\nbeta\ngamma\n");
        using var source = new MemoryStream(input);
        using var mirror = new MemoryStream();
        var lines = new List<string>();

        OutputPump.Pump(source, mirror, lines.Add);

        CollectionAssert.AreEqual(new[] { "alpha", "beta", "gamma" }, lines);
    }

    [TestMethod]
    public void Pump_FinalLineWithNoTrailingNewline_StillEmitted()
    {
        byte[] input = Encoding.UTF8.GetBytes("first\nsecond-no-newline");
        using var source = new MemoryStream(input);
        using var mirror = new MemoryStream();
        var lines = new List<string>();

        OutputPump.Pump(source, mirror, lines.Add);

        CollectionAssert.AreEqual(new[] { "first", "second-no-newline" }, lines);
    }

    [TestMethod]
    public void Pump_CrlfLines_PreservesTrailingCrInDecodedLine()
    {
        // OutputPump itself only splits on \n -- CRLF normalization is
        // LineHygiene's job (step 2), not this type's.
        byte[] input = Encoding.UTF8.GetBytes("windows-style\r\nunix-style\n");
        using var source = new MemoryStream(input);
        using var mirror = new MemoryStream();
        var lines = new List<string>();

        OutputPump.Pump(source, mirror, lines.Add);

        CollectionAssert.AreEqual(new[] { "windows-style\r", "unix-style" }, lines);
    }

    [TestMethod]
    public void Pump_EmptySource_NoLinesNoMirrorBytes()
    {
        using var source = new MemoryStream();
        using var mirror = new MemoryStream();
        var lines = new List<string>();

        OutputPump.Pump(source, mirror, lines.Add);

        Assert.IsEmpty(lines);
        Assert.AreEqual(0, mirror.Length);
    }

    [TestMethod]
    public void Pump_ChunkBoundarySplitsMultiByteUtf8Character_DecodesCorrectly()
    {
        // A single read() from a real pipe can split a multi-byte UTF-8
        // character across two chunks -- feed the pump one byte at a time
        // (via a custom slow Stream) to prove the stateful Decoder handles
        // it, never corrupting the mirrored bytes either way.
        string text = "emoji:\U0001F600\n"; // grinning face, a 4-byte UTF-8 sequence
        byte[] input = Encoding.UTF8.GetBytes(text);
        using var source = new OneByteAtATimeStream(input);
        using var mirror = new MemoryStream();
        var lines = new List<string>();

        OutputPump.Pump(source, mirror, lines.Add);

        CollectionAssert.AreEqual(input, mirror.ToArray());
        CollectionAssert.AreEqual(new[] { "emoji:\U0001F600" }, lines);
    }

    /// <summary>Forces <see cref="OutputPump.Pump"/>'s read loop to see the smallest possible chunks, exercising the decoder's cross-chunk statefulness.</summary>
    private sealed class OneByteAtATimeStream(byte[] data) : Stream
    {
        private int _pos;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= data.Length) return 0;
            buffer[offset] = data[_pos++];
            return 1;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
