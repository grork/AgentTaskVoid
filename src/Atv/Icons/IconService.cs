using Atv.IconRendering;
using Atv.Persistence;

namespace Atv.Icons;

/// <summary>
/// ERGO-13/15/23: the main project's icon management -- everything the
/// quarantined <c>Atv.IconRendering</c> project deliberately doesn't own
/// (filesystem, caching, per-handle placement/grouping, fallback policy,
/// lifecycle). No interface (matches <see cref="SidecarStore"/>/
/// <see cref="RecycleBin"/>'s INFRA-8-style seam): construct directly with
/// injected directories -- prod = <see cref="AppPaths.IconsDir"/>/
/// <see cref="AppPaths.RecycleBinDir"/>, tests = temp dirs, no package
/// identity or rendering-project fakes needed (the rendering project has no
/// filesystem/handle/policy surface to fake against -- see
/// <c>tests/Atv.IconRendering.Tests</c> for its own direct-render coverage).
///
/// Three file populations under <paramref name="iconsDir"/>/<paramref name="recycleBinDir"/>
/// (ERGO-23):
/// <list type="bullet">
/// <item><c>icons/handles/&lt;encoded handle&gt;.png</c> -- the per-handle
/// copies (ERGO-13/15's grouping mechanism: each handle gets its own file
/// even for an identical glyph, so identical glyphs still separate into
/// distinct taskbar icons). Lifecycle-twinned with the sidecar entry.</item>
/// <item><c>icons/cache/&lt;glyph-size key&gt;.png</c> -- the canonical
/// render-once cache. A pure regenerable accelerator: safe to wipe anytime
/// (worst case, a re-render), opportunistically age-pruned via
/// <see cref="PruneCache"/>. Keyed by a deterministic, collision-free
/// (glyph, size) string (codepoint hex + pixel size) rather than a
/// cryptographic hash -- codepoints are already unique, filename-safe keys,
/// so hashing them would only obscure the cache directory's contents for no
/// collision-avoidance benefit.</item>
/// <item><c>&lt;recycleBinDir&gt;/&lt;encoded handle&gt;.png</c> -- the
/// recycle-bin-side copy, co-located beside <see cref="RecycleBin"/>'s own
/// <c>.json</c> tombstone for the same handle (same directory, same
/// <see cref="HandleEncoding"/> filename convention -- deliberately NOT a
/// dependency on <see cref="RecycleBin"/> itself; the co-location is by
/// directory/naming CONVENTION, so <see cref="RecycleBin"/> and this type
/// stay decoupled while their per-handle files still travel together).</item>
/// </list>
/// Single-owner MOVE model: <see cref="MoveToRecycle"/>/
/// <see cref="MoveBackFromRecycle"/> physically relocate the file (never
/// copy), so a per-handle icon is always live XOR recycled, never both,
/// never neither while a handle or recycle record still references it.
///
/// Callers are responsible for invoking every member from inside a
/// <see cref="WriteGate"/> critical section when paired with a sidecar/
/// recycle-bin write, exactly like <see cref="SidecarStore"/>/
/// <see cref="RecycleBin"/> -- this type has no mutex awareness of its own.
/// </summary>
public sealed class IconService
{
    /// <summary>ERGO-22's chosen render size: one PNG per glyph, no DPI-scale variants -- see the type-level remarks on why.</summary>
    public const int DefaultSizePx = 64;

    /// <summary>
    /// ERGO-29's byte-size cap on a <c>--icon-file</c> source file, checked
    /// against <see cref="FileInfo.Length"/> BEFORE the file is ever read
    /// into memory (so an oversized file never gets fully loaded, let alone
    /// decoded). 5 MB is generous for any reasonable source logo/icon (real
    /// ones are typically well under 1 MB) while still bounding worst-case
    /// memory use decisively -- paired with <c>RasterNormalizer.MaxSourceDimensionPx</c>'s
    /// declared-dimension cap as the second half of the decompression-bomb
    /// defense (a small file can still declare a huge frame; that is caught
    /// downstream, in <c>RasterNormalizer</c>, on the decoded header alone).
    /// </summary>
    public const long DefaultMaxIconFileBytes = 5 * 1024 * 1024;

    private const string FileExtension = ".png";

    private readonly string _handlesDir;
    private readonly string _cacheDir;
    private readonly string _recycleBinDir;
    private readonly int _sizePx;
    private readonly long _maxIconFileBytes;
    private readonly Action<string> _log;

    public IconService(string iconsDir, string recycleBinDir, int sizePx = DefaultSizePx, Action<string>? log = null, long maxIconFileBytes = DefaultMaxIconFileBytes)
    {
        if (sizePx <= 0) throw new ArgumentOutOfRangeException(nameof(sizePx));
        if (maxIconFileBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxIconFileBytes));

        _handlesDir = Path.Combine(iconsDir, "handles");
        _cacheDir = Path.Combine(iconsDir, "cache");
        _recycleBinDir = recycleBinDir;
        _sizePx = sizePx;
        _maxIconFileBytes = maxIconFileBytes;
        _log = log ?? (_ => { });
    }

    // ---- placement: fallback chain (chosen -> default -> drawn shape) + render-once cache + per-handle copy ----

    /// <summary>
    /// Resolves <paramref name="token"/> to PNG bytes (chosen glyph -&gt;
    /// <see cref="IconTokens.Default"/> -&gt; drawn shape, each miss logged --
    /// FAIL-3/non-disruptive, FAIL-1) and writes them to <paramref name="handle"/>'s
    /// live per-handle path, overwriting any existing copy there. Returns the
    /// resulting file <see cref="Uri"/>, ready to hand to
    /// <c>IAppTaskStore.Create</c>'s <c>iconUri</c> parameter.
    /// </summary>
    public Uri Place(string handle, IconToken token)
    {
        byte[] png = RenderWithFallback(token);
        return WriteHandleCopy(handle, png);
    }

    private byte[] RenderWithFallback(IconToken token)
    {
        if (TryRenderToken(token, out byte[]? bytes))
            return bytes!;

        _log($"icon: {Describe(token)} unavailable -- falling back to the default glyph.");

        if (!token.Equals(IconTokens.Default) && TryRenderToken(IconTokens.Default, out bytes))
            return bytes!;

        _log("icon: default glyph also unavailable -- falling back to the drawn-shape fallback.");
        return GetOrRenderShape();
    }

    private bool TryRenderToken(IconToken token, out byte[]? bytes) => token.Kind switch
    {
        IconTokenKind.RawPath => TryReadAndNormalizeRawPath(token.Value, out bytes),
        IconTokenKind.Emoji => TryRenderGlyph($"emoji-{token.Codepoint:X}-{_sizePx}", () => GlyphRenderer.RenderEmoji(token.Value, _sizePx), token, out bytes),
        // "segoe-tile" (not "segoe"): phase 16 (ERGO-28) changed WHAT a Segoe
        // glyph render actually produces (white-on-accent-tile, not a bare
        // black glyph) without touching the render-once CACHE's read-first
        // logic -- an unchanged cache key would silently keep serving a
        // pre-phase-16 install's already-cached bare-glyph bytes forever
        // (observed live on this dev machine's own persisted LocalState
        // cache while verifying this phase). The new prefix guarantees a
        // fresh render on first use after upgrading; the stale
        // "segoe-*.png" cache entries are simply orphaned (harmless -- the
        // cache is a pure accelerator, see PruneCache).
        IconTokenKind.SegoeGlyph => TryRenderGlyph($"segoe-tile-{token.Codepoint:X}-{_sizePx}", () => GlyphRenderer.RenderSegoeGlyph(token.Codepoint, _sizePx), token, out bytes),
        _ => throw new ArgumentOutOfRangeException(nameof(token), token.Kind, "Unknown icon token kind."),
    };

    /// <summary>
    /// ERGO-29's `--icon-file` trust boundary: reads and validates a
    /// caller-supplied source file, then hands it to
    /// <see cref="RasterNormalizer"/> for decode/fit/flatten -- the SOURCE
    /// file is only ever read here, never written or moved (AC3's path-safety
    /// requirement); the DESTINATION path is always derived from the handle
    /// via <see cref="HandleEncoding"/>, never from this path, so nothing
    /// about a caller-controlled source path can influence where bytes get
    /// written (no traversal vector exists to begin with).
    ///
    /// Every rejection tier -- missing file, oversized, disallowed format,
    /// malformed image data -- returns <see langword="false"/> with a logged
    /// reason (FAIL-3) so the caller's existing fallback chain
    /// (<see cref="RenderWithFallback"/>) engages exactly like a genuinely
    /// absent glyph would.
    /// </summary>
    private bool TryReadAndNormalizeRawPath(string path, out byte[]? bytes)
    {
        bytes = null;

        if (!File.Exists(path))
        {
            _log($"icon file: '{path}' does not exist.");
            return false;
        }

        long length;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"icon file: could not read the size of '{path}' ({ex.GetType().Name}).");
            return false;
        }

        if (length <= 0 || length > _maxIconFileBytes)
        {
            _log($"icon file: '{path}' is {length} bytes, outside the allowed 1..{_maxIconFileBytes} byte range -- rejected before reading its contents.");
            return false;
        }

        byte[] raw;
        try
        {
            raw = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"icon file: failed to read '{path}' ({ex.GetType().Name}).");
            return false;
        }

        NormalizeResult result;
        try
        {
            result = RasterNormalizer.Normalize(raw, _sizePx);
        }
        catch (Exception ex)
        {
            // RasterNormalizer.Normalize already catches its own decode
            // failures (FAIL-1); this mirrors TryRenderGlyph's broad catch as
            // defense-in-depth against a genuinely unexpected interop failure
            // escaping it, so it degrades to the fallback chain instead of
            // taking the whole verb down.
            _log($"icon file: unexpected failure normalizing '{path}' ({ex.GetType().Name}: {ex.Message}).");
            return false;
        }

        if (result.Status != NormalizeStatus.Ok)
        {
            _log($"icon file: '{path}' rejected -- {result.Status}: {result.Detail}");
            return false;
        }

        bytes = result.PngBytes;
        return true;
    }

    /// <summary>
    /// Render-vs-cache-write failures are handled deliberately differently:
    /// a RENDER failure (glyph genuinely absent, or an unexpected
    /// interop/COM exception) means this tier truly has nothing to offer, so
    /// it reports "unavailable" and the caller moves to the next fallback
    /// tier. A CACHE-WRITE failure after a successful render is a completely
    /// different thing -- the glyph rendered fine, it just couldn't be
    /// persisted to the accelerator (disk full, permissions, ...) -- so
    /// <see cref="TryWriteCache"/> swallows that separately and this still
    /// returns the freshly-rendered bytes. Conflating the two would wrongly
    /// treat a perfectly good, present glyph as "unavailable" over a
    /// transient cache-write hiccup.
    /// </summary>
    private bool TryRenderGlyph(string cacheKey, Func<RenderResult> render, IconToken tokenForLogging, out byte[]? bytes)
    {
        string cachePath = Path.Combine(_cacheDir, cacheKey + FileExtension);
        if (TryReadCache(cachePath, out bytes))
            return true;

        RenderResult result;
        try
        {
            result = render();
        }
        catch (Exception ex)
        {
            // Broad by design (FAIL-1): an unexpected interop/rendering
            // failure must degrade to "try the next fallback tier", never
            // propagate and take the whole verb down with it.
            _log($"icon: render failed for {Describe(tokenForLogging)} ({ex.GetType().Name}: {ex.Message}).");
            bytes = null;
            return false;
        }

        if (result.Status != RenderStatus.Ok)
        {
            bytes = null;
            return false;
        }

        bytes = result.PngBytes!;
        TryWriteCache(cachePath, bytes);
        return true;
    }

    private byte[] GetOrRenderShape()
    {
        string cachePath = Path.Combine(_cacheDir, $"shape-default-{_sizePx}{FileExtension}");
        if (TryReadCache(cachePath, out byte[]? cached))
            return cached!;

        byte[] bytes = ShapeRenderer.RenderDefaultShape(_sizePx).PngBytes!;
        TryWriteCache(cachePath, bytes);
        return bytes;
    }

    /// <summary>Best-effort cache persist: the cache is a pure regenerable accelerator (safe to fail/wipe anytime), so a write failure here is logged and swallowed, never propagated -- the caller already has the real bytes in hand regardless.</summary>
    private void TryWriteCache(string path, byte[] bytes)
    {
        try
        {
            WriteCache(path, bytes);
        }
        catch (Exception ex)
        {
            _log($"icon cache: failed to persist '{Path.GetFileName(path)}' ({ex.GetType().Name}: {ex.Message}) -- continuing with the in-memory render.");
        }
    }

    private static bool TryReadCache(string path, out byte[]? bytes)
    {
        if (!File.Exists(path)) { bytes = null; return false; }
        try { bytes = File.ReadAllBytes(path); return true; }
        catch (IOException) { bytes = null; return false; }
        catch (UnauthorizedAccessException) { bytes = null; return false; }
    }

    private void WriteCache(string path, byte[] bytes)
    {
        Directory.CreateDirectory(_cacheDir);
        WriteAtomic(path, bytes, _cacheDir);
    }

    private Uri WriteHandleCopy(string handle, byte[] png)
    {
        Directory.CreateDirectory(_handlesDir);
        string finalPath = HandlePathFor(handle);
        WriteAtomic(finalPath, png, _handlesDir);
        return new Uri(finalPath);
    }

    /// <summary>Same atomic temp-file + rename pattern as <see cref="SidecarStore.Write"/>/<see cref="RecycleBin.Tombstone"/> -- a reader never observes a partially-written icon file.</summary>
    private static void WriteAtomic(string finalPath, byte[] bytes, string directory)
    {
        string tempPath = Path.Combine(directory, $".tmp-{Guid.NewGuid():N}{FileExtension}");
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, finalPath, overwrite: true);
    }

    // ---- ownership: single-owner MOVE model (ERGO-23) -------------------------

    /// <summary>Deletes the live per-handle icon copy, if any -- the entry-drop/reconciliation-drop and <c>remove</c> reap paths. Best-effort: a missing file is a clean no-op, never an error.</summary>
    public bool ReapLiveIcon(string handle) => TryDelete(HandlePathFor(handle));

    /// <summary>Moves the live per-handle icon into the recycle-bin folder, beside its tombstone record -- the MOVE model's tombstone half (pairs with <see cref="RecycleBin.Tombstone"/>). Returns <see langword="false"/> (no-op) if there was no live icon for this handle.</summary>
    public bool MoveToRecycle(string handle) => TryMove(HandlePathFor(handle), RecyclePathFor(handle));

    /// <summary>
    /// Moves a recycled icon back to the live per-handle path -- the
    /// resurrection half of the MOVE model (pairs with a successful
    /// <see cref="RecycleBin.TryResurrect"/> + <see cref="RecycleBin.Remove"/>).
    /// Returns the resulting live <see cref="Uri"/>. If there is no recycled
    /// icon to move (should not happen under the structural "always exactly
    /// one place" guarantee, but this degrades gracefully rather than
    /// trusting that blindly) falls back to placing <see cref="IconTokens.Default"/>
    /// fresh, logged.
    /// </summary>
    public Uri MoveBackFromRecycle(string handle)
    {
        if (TryMove(RecyclePathFor(handle), HandlePathFor(handle)))
            return new Uri(HandlePathFor(handle));

        _log($"{handle}: no recycled icon found to move back -- rendering the default glyph fresh.");
        return Place(handle, IconTokens.Default);
    }

    /// <summary>Deletes a recycled icon copy without moving it -- the TTL/reboot purge half of the MOVE model (co-deletes with <see cref="RecycleBin.Scavenge"/>'s dropped records), and the cleanup for a `start`-resurrection that carried its own fresh icon rather than the recycled one. Best-effort.</summary>
    public bool ReapRecycledIcon(string handle) => TryDelete(RecyclePathFor(handle));

    private string HandlePathFor(string handle) => Path.Combine(_handlesDir, HandleEncoding.Encode(handle) + FileExtension);

    private string RecyclePathFor(string handle) => Path.Combine(_recycleBinDir, HandleEncoding.Encode(handle) + FileExtension);

    private static bool TryDelete(string path)
    {
        if (!File.Exists(path)) return false;
        try { File.Delete(path); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool TryMove(string sourcePath, string destPath)
    {
        if (!File.Exists(sourcePath)) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Move(sourcePath, destPath, overwrite: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // ---- maintenance: cache prune + orphan backstop sweep ----------------------

    /// <summary>Opportunistic age-based prune of the canonical render-once cache -- a pure accelerator, safe to wipe anytime (worst case, a re-render). Callers invoke this explicitly/opportunistically (matching <see cref="RecycleBin.Scavenge"/>'s own "never on a hot path" contract); not auto-invoked by <see cref="Place"/>.</summary>
    public int PruneCache(DateTimeOffset now, TimeSpan maxAge)
    {
        if (!Directory.Exists(_cacheDir)) return 0;

        int pruned = 0;
        foreach (string file in Directory.EnumerateFiles(_cacheDir, "*" + FileExtension))
        {
            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(file);
            if (now.UtcDateTime - lastWriteUtc <= maxAge)
                continue;

            try { File.Delete(file); pruned++; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        if (pruned > 0)
            _log($"icon cache prune: removed {pruned} stale cache entr{(pruned == 1 ? "y" : "ies")}.");
        return pruned;
    }

    /// <summary>
    /// Orphan-icon backstop sweep (ERGO-23, following the LIFE-23 ruling):
    /// deletes every live per-handle icon file whose handle is in neither
    /// <paramref name="liveHandles"/> nor <paramref name="recycleHandles"/>.
    /// Identity-scoped (this directory holds only this package identity's own
    /// icons) and reaped aggressively -- no confirmation guard, a single
    /// bulk-count log line.
    /// </summary>
    public int SweepOrphans(IReadOnlyCollection<string> liveHandles, IReadOnlyCollection<string> recycleHandles)
    {
        if (!Directory.Exists(_handlesDir)) return 0;

        var owned = new HashSet<string>(liveHandles, StringComparer.Ordinal);
        owned.UnionWith(recycleHandles);

        int reaped = 0;
        foreach (string file in Directory.EnumerateFiles(_handlesDir, "*" + FileExtension))
        {
            string handle = HandleEncoding.Decode(Path.GetFileNameWithoutExtension(file));
            if (owned.Contains(handle)) continue;

            try { File.Delete(file); reaped++; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        if (reaped > 0)
            _log($"icon orphan sweep: reaped {reaped} handle-icon file(s) with no owning handle or recycle record.");
        return reaped;
    }

    private static string Describe(IconToken token) => token.Kind switch
    {
        IconTokenKind.Emoji => $"emoji '{token.Value}'",
        IconTokenKind.SegoeGlyph => $"Segoe glyph U+{token.Codepoint:X4}",
        IconTokenKind.RawPath => $"path '{token.Value}'",
        _ => token.Value,
    };
}
