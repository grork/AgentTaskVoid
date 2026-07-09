namespace Atv.Persistence;

/// <summary>
/// The sidecar's per-handle payload (ERGO-21, "The sidecar store design",
/// decision detail DP2): an INDEX only -- <c>handle -&gt; Id</c> plus a
/// liveness stamp. Deliberately nothing else: no cached content/title, no
/// group/owner/cwd (those either live in the API itself under ERGO-8's
/// whole-content-replacement model, or were cut for v1 -- group by ERGO-14,
/// owner by ERGO-16, cwd deferred with INTER-4).
/// </summary>
public sealed record SidecarEntry(string Id, DateTimeOffset LastUpdate, int SchemaVersion)
{
    /// <summary>Bump when this shape changes; keeps forward-compat cheap (ERGO-21 DP2).</summary>
    public const int CurrentSchemaVersion = 1;
}
