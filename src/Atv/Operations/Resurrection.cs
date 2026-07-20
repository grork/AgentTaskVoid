using Codevoid.AgentTaskVoid.Icons;
using Codevoid.AgentTaskVoid.Persistence;
using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.Operations;

/// <summary>
/// LIFE-15/LIFE-21's miss-path re-creation: rebuilds a live card from a
/// <see cref="RecycleRecord"/> tombstone (title, subtitle, deepLink, icon-ref
/// -- everything the record captured; nothing mutable, since nothing mutable
/// was ever stored).
///
/// Icon-aware as of phase 07: when a caller passes an <see cref="IconService"/>
/// to <see cref="RecreateFromRecord"/>, the recycled per-handle icon copy is
/// physically MOVED back to the live path (ERGO-23's single-owner MOVE
/// model) and that real live <see cref="Uri"/> is used. <see cref="ResolveIconUri"/>
/// remains the fallback for callers that pass no <see cref="IconService"/>
/// (keeps phase-05's existing tests, which construct
/// <see cref="RecreateFromRecord"/> calls with no icon collaborator, green
/// unchanged) -- parse <see cref="RecycleRecord.IconRef"/> as an absolute URI
/// if it looks like one, else fall back to a placeholder.
/// </summary>
public static class Resurrection
{
    /// <summary>
    /// Placeholder icon used only when no <see cref="IconService"/> is
    /// supplied AND a tombstone record's <see cref="RecycleRecord.IconRef"/>
    /// is missing or doesn't parse as an absolute URI.
    /// </summary>
    public static readonly Uri FallbackIconUri = new("ms-appx:///Assets/Square44x44Logo.png");

    /// <summary>
    /// Re-creates a live card from <paramref name="record"/>'s stored core
    /// info (title, subtitle, deepLink, icon-ref), with the given fresh
    /// baseline content. Always lands as <see cref="AppTaskState.Running"/>
    /// (the platform's <c>Create</c> has no state parameter) -- callers that
    /// need a different end state issue a follow-up <c>Update</c>.
    ///
    /// When <paramref name="icons"/> is supplied, resolves the icon by
    /// physically moving the recycled per-handle copy back to the live path
    /// (<see cref="IconService.MoveBackFromRecycle"/>) BEFORE calling
    /// <c>Create</c>, so the returned card's <c>IconUri</c> always points at
    /// a file that already exists. <paramref name="icons"/> is optional
    /// (defaults to <see langword="null"/>) purely so existing phase-05
    /// callers/tests that don't construct an <see cref="IconService"/> keep
    /// compiling and behaving exactly as before.
    /// </summary>
    public static AppTaskView RecreateFromRecord(IAppTaskStore store, RecycleRecord record, AppTaskContentDto content, IconService? icons = null)
    {
        Uri iconUri = icons is not null ? icons.MoveBackFromRecycle(record.Handle) : ResolveIconUri(record.IconRef);
        return store.Create(record.Title, record.Subtitle, record.DeepLink, iconUri, content);
    }

    /// <summary>Parses a tombstone's opaque icon reference into a usable <see cref="Uri"/>, falling back to <see cref="FallbackIconUri"/> if it's missing or unparseable.</summary>
    public static Uri ResolveIconUri(string? iconRef)
        => iconRef is { Length: > 0 } && Uri.TryCreate(iconRef, UriKind.Absolute, out var parsed)
            ? parsed
            : FallbackIconUri;
}
