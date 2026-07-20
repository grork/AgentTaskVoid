namespace Codevoid.AgentTaskVoid.IconRendering;

/// <summary>Outcome of a render call -- ERGO-22's "PNG bytes or a 'glyph not present' signal", never an exception for the expected "this glyph doesn't exist on this build" case.</summary>
public enum RenderStatus
{
    /// <summary><see cref="RenderResult.PngBytes"/> holds a valid PNG.</summary>
    Ok,

    /// <summary>The requested glyph does not exist in the target font on this machine/build -- not an error, a signal for the caller's fallback policy (main project, ERGO-23) to act on.</summary>
    GlyphNotFound,
}

/// <summary>Result of one <see cref="GlyphRenderer"/>/<see cref="ShapeRenderer"/> call. <see cref="PngBytes"/> is non-null iff <see cref="Status"/> is <see cref="RenderStatus.Ok"/>.</summary>
public readonly record struct RenderResult(RenderStatus Status, byte[]? PngBytes, int Width, int Height)
{
    public static RenderResult NotFound(int sizePx) => new(RenderStatus.GlyphNotFound, null, sizePx, sizePx);

    public static RenderResult Ok(byte[] pngBytes, int sizePx) => new(RenderStatus.Ok, pngBytes, sizePx, sizePx);
}
