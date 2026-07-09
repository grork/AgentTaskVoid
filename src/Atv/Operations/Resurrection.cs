using Atv.Persistence;
using Atv.Store;

namespace Atv.Operations;

/// <summary>
/// LIFE-15/LIFE-21's miss-path re-creation: rebuilds a live card from a
/// <see cref="RecycleRecord"/> tombstone (title, subtitle, deepLink, icon-ref
/// -- everything the record captured; nothing mutable, since nothing mutable
/// was ever stored).
///
/// Ships icon-UNAWARE, deliberately (this phase's scoping note): a tombstone
/// record's <see cref="RecycleRecord.IconRef"/> is an opaque string that
/// phase 07 will own the real move-back semantics for. Today,
/// <see cref="ResolveIconUri"/> does the simplest thing that keeps the seam
/// honest -- parse it as an absolute URI if it looks like one, else fall back
/// to a placeholder -- as a clean extension point a later phase can replace
/// without touching any caller of <see cref="RecreateFromRecord"/>.
/// </summary>
public static class Resurrection
{
    /// <summary>
    /// Placeholder icon used only when a tombstone record's
    /// <see cref="RecycleRecord.IconRef"/> is missing or doesn't parse as an
    /// absolute URI. Phase 05 ships icon-unaware (see type remarks); a real
    /// default/render pipeline lands with phase 07.
    /// </summary>
    public static readonly Uri FallbackIconUri = new("ms-appx:///Assets/Square44x44Logo.png");

    /// <summary>
    /// Re-creates a live card from <paramref name="record"/>'s stored core
    /// info (title, subtitle, deepLink, icon-ref), with the given fresh
    /// baseline content. Always lands as <see cref="AppTaskState.Running"/>
    /// (the platform's <c>Create</c> has no state parameter) -- callers that
    /// need a different end state issue a follow-up <c>Update</c>.
    /// </summary>
    public static AppTaskView RecreateFromRecord(IAppTaskStore store, RecycleRecord record, AppTaskContentDto content)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(content);

        return store.Create(record.Title, record.Subtitle, record.DeepLink, ResolveIconUri(record.IconRef), content);
    }

    /// <summary>Parses a tombstone's opaque icon reference into a usable <see cref="Uri"/>, falling back to <see cref="FallbackIconUri"/> if it's missing or unparseable.</summary>
    public static Uri ResolveIconUri(string? iconRef)
        => iconRef is { Length: > 0 } && Uri.TryCreate(iconRef, UriKind.Absolute, out var parsed)
            ? parsed
            : FallbackIconUri;
}
