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
