using System.Text.Json.Serialization;
using Atv.Diagnostics;
using Atv.Operations;
using Atv.Store;

namespace Atv.Cli.Verbs;

/// <summary>
/// FAIL-2/ERGO-27 C5's `list --json` shape: one row per live task.
/// <see cref="Handle"/>/<see cref="LastUpdate"/> are <see langword="null"/>
/// for an entryless task (ERGO-16: identity-global truth -- a live API task
/// with no sidecar entry is still listed).
/// </summary>
public sealed record ListTaskDto(string? Handle, string Title, string State, string ExecutingStep, DateTimeOffset? LastUpdate);

/// <summary>Source-generated (AOT/trim-safe) JSON metadata for <c>list --json</c>'s task-array shape.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ListTaskDto[]))]
internal partial class ListJsonContext : JsonSerializerContext
{
}

/// <summary>
/// `list [--json]` (ERGO-27/ERGO-16): identity-global enumeration via
/// <see cref="TaskOperations.List"/> -- read-only, lock-free (no WriteGate,
/// matching <c>SidecarStore</c>'s own "list runs lock-free outside the
/// WriteGate" contract). Gated by the same <see cref="Capability.Check"/>
/// every other verb uses, but NOT a write-path verb (ERGO-27: "each
/// write-path verb ensures a watchdog is live" -- `list` doesn't write) --
/// no <c>ensureWatchdog</c> call. Uses <see cref="Posture.RunQuery"/>:
/// `list`'s `--json` shape is its own task array (ERGO-27 C5), not the
/// generic mutating-verb {"ok":..,"reason":..} shape every lifecycle verb
/// (and `clear`) shares.
/// </summary>
public static class ListVerb
{
    public static int Run(Output output, Posture posture, Func<bool> hasIdentity, Func<bool> isSupported, TaskOperations ops, DateTimeOffset now)
    {
        return posture.RunQuery("list", null, () =>
        {
            var cap = Capability.Check(hasIdentity, isSupported);
            if (!cap.Ok) return cap;

            IReadOnlyList<TaskOperations.TaskListEntry> entries = ops.List();

            if (output.Json)
            {
                ListTaskDto[] dtos = [.. entries.Select(ToDto)];
                output.WriteJson(dtos, ListJsonContext.Default.ListTaskDtoArray);
            }
            else
            {
                foreach (var e in entries)
                    output.Data(FormatHuman(e));
            }

            return VerbResult.Success($"{entries.Count} task(s).");
        }, now);
    }

    private static ListTaskDto ToDto(TaskOperations.TaskListEntry e) => new(e.Handle, e.Title, StateLabel(e.State), e.ExecutingStep, e.LastUpdate);

    private static string FormatHuman(TaskOperations.TaskListEntry e)
        => $"{e.Handle ?? "(entryless)"}\t{StateLabel(e.State)}\t{e.Title}\t{e.ExecutingStep}\t{e.LastUpdate?.ToString("O") ?? "-"}";

    private static string StateLabel(AppTaskState state) => state switch
    {
        AppTaskState.Running => "running",
        AppTaskState.Paused => "paused",
        AppTaskState.NeedsAttention => "needsAttention",
        AppTaskState.Completed => "completed",
        AppTaskState.Error => "error",
        _ => state.ToString(),
    };
}
