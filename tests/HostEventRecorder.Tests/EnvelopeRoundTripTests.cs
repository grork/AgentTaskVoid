using System.Text;
using System.Text.Json;

namespace HostEventRecorder.Tests;

/// <summary>
/// Proves the envelope's byte-faithfulness at the serialization layer
/// directly (no file/process involved) -- <see cref="RecorderCaptureTests"/>
/// re-proves the same property through the full <see cref="Recorder.Capture"/>
/// pipeline including the on-disk round trip.
/// </summary>
[TestClass]
public sealed class EnvelopeRoundTripTests
{
    public static IEnumerable<object[]> TrickyPayloads()
    {
        yield return ["ascii plain text, nothing tricky"];
        yield return ["non-ASCII: café, 中文, emoji 😀👍"]; // café, Chinese, emoji (surrogate pair)
        yield return ["embedded \"quotes\" and a\nnewline and a\ttab and a\\backslash"];
        yield return ["{\"unterminated json"]; // malformed JSON
        yield return ["not json at all -- just some prose with {braces} and [brackets] scattered in"];
        yield return ["{\"nested\": {\"valid\": true, \"array\": [1, 2, 3]}, \"unicode\": \"\\u00e9\"}"]; // valid JSON, must NOT be re-parsed/re-emitted
        yield return [""]; // empty payload
        yield return ["\r\nleading and trailing CRLF\r\n"];
    }

    [TestMethod]
    [DynamicData(nameof(TrickyPayloads))]
    public void Payload_RoundTripsExactly_ThroughEscapedStringSerialization(string original)
    {
        var envelope = new EventEnvelope
        {
            Ts = DateTimeOffset.UtcNow.ToString("O"),
            Host = "claude-code",
            Event = "PostToolUse",
            Pid = 4321,
            Session = "s1",
            Payload = original,
        };

        string json = JsonSerializer.Serialize(envelope, EnvelopeJsonContext.Default.EventEnvelope);
        var decoded = JsonSerializer.Deserialize(json, EnvelopeJsonContext.Default.EventEnvelope);

        Assert.IsNotNull(decoded);
        Assert.AreEqual(original, decoded.Payload, "the payload string must round-trip through JSON escaping/unescaping byte-for-byte.");

        // And the UTF-8 byte encoding of the round-tripped string is
        // identical to the UTF-8 bytes the recorder would have decoded
        // stdin into in the first place.
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(original), Encoding.UTF8.GetBytes(decoded.Payload));
    }

    [TestMethod]
    public void ValidJsonPayload_IsNeverReparsedOrReemitted_StaysAnOpaqueEscapedString()
    {
        // Deliberately re-ordered keys / non-canonical whitespace: if the
        // recorder ever parsed-then-re-emitted this as JSON, re-emission
        // would normalize it. Storing it as a plain escaped string must
        // preserve it verbatim, whitespace and all.
        const string original = "{  \"b\": 2,   \"a\":1 }";

        var envelope = new EventEnvelope { Ts = "t", Host = "h", Event = "e", Pid = 1, Session = "s", Payload = original };
        string json = JsonSerializer.Serialize(envelope, EnvelopeJsonContext.Default.EventEnvelope);
        var decoded = JsonSerializer.Deserialize(json, EnvelopeJsonContext.Default.EventEnvelope);

        Assert.AreEqual(original, decoded!.Payload, "verbatim whitespace/key-order must survive -- proves the payload was never parsed-and-re-emitted as JSON.");
    }

    [TestMethod]
    public void Serialized_ContainsExactlySixFields_WithExpectedNames()
    {
        var envelope = new EventEnvelope { Ts = "t", Host = "h", Event = "e", Pid = 1, Session = "s", Payload = "p" };
        string json = JsonSerializer.Serialize(envelope, EnvelopeJsonContext.Default.EventEnvelope);

        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        CollectionAssert.AreEquivalent(new[] { "ts", "host", "event", "pid", "session", "payload" }, names);
        Assert.HasCount(6, names);
    }

    [TestMethod]
    public void Serialized_FieldValues_AreCorrect()
    {
        var envelope = new EventEnvelope { Ts = "2026-07-12T18:03:44.1912837Z", Host = "claude-code", Event = "PostToolUse", Pid = 41232, Session = "sess-1", Payload = "{\"k\":\"v\"}" };
        string json = JsonSerializer.Serialize(envelope, EnvelopeJsonContext.Default.EventEnvelope);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.AreEqual("2026-07-12T18:03:44.1912837Z", root.GetProperty("ts").GetString());
        Assert.AreEqual("claude-code", root.GetProperty("host").GetString());
        Assert.AreEqual("PostToolUse", root.GetProperty("event").GetString());
        Assert.AreEqual(41232, root.GetProperty("pid").GetInt32());
        Assert.AreEqual("sess-1", root.GetProperty("session").GetString());
        Assert.AreEqual("{\"k\":\"v\"}", root.GetProperty("payload").GetString());
    }
}
