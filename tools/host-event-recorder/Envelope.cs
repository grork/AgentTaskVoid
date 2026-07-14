using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HostEventRecorder;

/// <summary>
/// The fixed six-field capture record (phase-14 Part A spec). Field order
/// here is the field order on the wire (STJ source-gen serializes in
/// declared-member order). <see cref="Payload"/> is the host's stdin bytes,
/// already UTF-8-decoded to a plain string -- STJ's normal string escaping
/// is what makes it a "JSON-escaped string" on the wire; it is never parsed
/// or re-emitted as JSON itself.
/// </summary>
public sealed class EventEnvelope
{
    public required string Ts { get; init; }
    public required string Host { get; init; }
    public required string Event { get; init; }
    public required int Pid { get; init; }
    public required string Session { get; init; }
    public required string Payload { get; init; }
}

/// <summary>
/// Source-gen serialization context (AOT-safe, no reflection-based
/// <c>JsonSerializer</c> overloads -- CLAUDE.md's NativeAOT posture applies
/// to this tool too even though it isn't published trimmed by default).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventEnvelope))]
public sealed partial class EnvelopeJsonContext : JsonSerializerContext
{
}

/// <summary>
/// The single serialization entry point for the envelope (INFRA-25). Uses
/// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> so the on-disk
/// JSONL is greppable -- a payload's inner quotes render as <c>\"</c> and
/// non-ASCII stays literal UTF-8, rather than STJ's HTML-safe default
/// (<c>"</c>, <c>é</c>). This is a pure REPRESENTATION change: the
/// encoder only affects how the string is escaped on the wire, never the
/// decoded value, so byte-fidelity (the whole point, LIFE-24) is untouched --
/// the round-trip tests prove it. "Unsafe" denotes HTML-injection safety,
/// irrelevant for a local diagnostic log. Both <see cref="Recorder.Capture"/>
/// and the tests go through this one instance so they exercise the identical
/// wire form. The explicit <see cref="JsonNamingPolicy.CamelCase"/> mirrors the
/// context attribute (a parameterized context ctor does not inherit it).
/// </summary>
public static class EnvelopeSerialization
{
    public static readonly EnvelopeJsonContext Context = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static string Serialize(EventEnvelope envelope)
        => JsonSerializer.Serialize(envelope, Context.EventEnvelope);
}
