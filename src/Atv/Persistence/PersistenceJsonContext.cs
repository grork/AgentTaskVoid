using System.Text.Json.Serialization;

namespace Codevoid.AgentTaskVoid.Persistence;

/// <summary>
/// Source-generated (trim/AOT-safe, ERGO-26's "System.Text.Json,
/// source-generated" decision) JSON metadata for every type this namespace
/// persists to disk. Both the sidecar (<see cref="SidecarEntry"/>) and the
/// recycle bin (<see cref="RecycleRecord"/>) share one context -- no
/// runtime-reflection fallback anywhere in the NativeAOT-published binary
/// (INFRA-2/INFRA-3).
/// </summary>
[JsonSerializable(typeof(SidecarEntry))]
[JsonSerializable(typeof(RecycleRecord))]
[JsonSerializable(typeof(EngineMemory))]
[JsonSerializable(typeof(BlockedLocus))]
[JsonSerializable(typeof(ReadyDecayState))]
[JsonSerializable(typeof(AgentNameHint))]
internal partial class PersistenceJsonContext : JsonSerializerContext
{
}
