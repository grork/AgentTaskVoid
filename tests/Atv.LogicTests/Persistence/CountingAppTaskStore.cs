using Atv.Store;

namespace Atv.LogicTests.Persistence;

/// <summary>
/// Thin counting decorator over an <see cref="IAppTaskStore"/> -- used to
/// structurally prove the ERGO-21 "scoped 2026-07-07" invariant that
/// update-class per-handle resolution (<c>Reconciler.ResolveHandle</c>)
/// never calls <see cref="FindAll"/> and never sweeps (never calls
/// <see cref="Remove"/>), and conversely that the full pass
/// (<c>Reconciler.ReconcileAll</c>) uses exactly one <see cref="FindAll"/>.
/// </summary>
internal sealed class CountingAppTaskStore(IAppTaskStore inner) : IAppTaskStore
{
    public int FindAllCallCount { get; private set; }
    public int FindCallCount { get; private set; }
    public int RemoveCallCount { get; private set; }

    /// <summary>Counts <see cref="Update"/> calls (whole-content writes) -- phase 11 uses this to prove a debounce tick with no new output makes ZERO content writes (only a sidecar keepalive touch), and a tick with new output makes exactly ONE regardless of how many lines arrived since the last tick.</summary>
    public int UpdateCallCount { get; private set; }

    /// <summary>
    /// Counts <see cref="Create"/> calls -- phase 19A uses this as the
    /// structural proof that a redirected <c>activity</c> claim against an
    /// already-carded child never re-mints/re-creates it: <c>IconService.Place</c>
    /// is only ever reachable (in <see cref="Atv.Semantics.SemanticEngine"/>)
    /// from the SAME genuine-creation branch that also calls
    /// <see cref="IAppTaskStore.Create"/> (<c>ApplyRepoDefaults</c> ->
    /// <c>ResolveCreateTimeIcon</c>), so zero new <see cref="Create"/> calls
    /// across a claim structurally implies zero new icon placements too --
    /// <see cref="Atv.Icons.IconService"/> itself is sealed with no interface,
    /// so it cannot be spied on directly the way this store can.
    /// </summary>
    public int CreateCallCount { get; private set; }

    public bool IsSupported() => inner.IsSupported();

    public IReadOnlyList<AppTaskView> FindAll()
    {
        FindAllCallCount++;
        return inner.FindAll();
    }

    public AppTaskView? Find(string id)
    {
        FindCallCount++;
        return inner.Find(id);
    }

    public AppTaskView Create(string title, string subtitle, Uri deepLink, Uri iconUri, AppTaskContentDto content)
    {
        CreateCallCount++;
        return inner.Create(title, subtitle, deepLink, iconUri, content);
    }

    public bool Update(string id, AppTaskState state, AppTaskContentDto content)
    {
        UpdateCallCount++;
        return inner.Update(id, state, content);
    }

    public bool UpdateState(string id, AppTaskState state) => inner.UpdateState(id, state);

    public bool UpdateTitles(string id, string title, string subtitle) => inner.UpdateTitles(id, title, subtitle);

    public bool UpdateDeepLink(string id, Uri deepLink) => inner.UpdateDeepLink(id, deepLink);

    public bool Remove(string id)
    {
        RemoveCallCount++;
        return inner.Remove(id);
    }
}
