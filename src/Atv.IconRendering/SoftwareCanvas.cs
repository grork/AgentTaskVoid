using Windows.Win32;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Graphics.Imaging;

namespace Atv.IconRendering;

/// <summary>Draws onto a fresh <see cref="SoftwareCanvas"/> canvas. <paramref name="renderTarget"/>/<paramref name="brush"/> are borrowed for the call only -- never released or cached by the callback.</summary>
internal unsafe delegate void DrawCallback(ID2D1RenderTarget* renderTarget, ID2D1SolidColorBrush* brush);

/// <summary>
/// The zero-GPU rendering surface every glyph/shape render call shares: a
/// SOFTWARE WIC bitmap render target (ERGO-22) -- <c>ID2D1Factory.
/// CreateWicBitmapRenderTarget</c> bound to a fresh 32bpp premultiplied-BGRA
/// <c>IWICBitmap</c>, with <c>D2D1_RENDER_TARGET_TYPE_SOFTWARE</c> explicitly
/// forcing Direct2D's own CPU rasterizer -- no D3D device, no driver
/// dependency, deterministic regardless of what GPU (or lack of one) is
/// present on the host.
///
/// One WIC bitmap + render target + solid-color brush per call (cheap; the
/// expensive factories are the process-lifetime singletons in
/// <see cref="Interop"/>), released before returning. Extracts straight-alpha
/// RGBA8 pixels (PNG's format) from the premultiplied-BGRA backing store for
/// <see cref="GlyphRenderer"/>/<see cref="ShapeRenderer"/> to hand to
/// <see cref="PngEncoder"/>.
/// </summary>
internal static unsafe class SoftwareCanvas
{
    /// <summary>
    /// Creates a <paramref name="sizePx"/> x <paramref name="sizePx"/> square
    /// software canvas, clears it to fully transparent, runs
    /// <paramref name="draw"/> (with a ready-made solid BLACK brush -- the
    /// only color either caller needs: DirectWrite's own color-font machinery
    /// overrides it for color glyph layers when
    /// <c>D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT</c> is set) between
    /// BeginDraw/EndDraw, and returns the result as straight-alpha RGBA8
    /// (row-major, no padding) ready for <see cref="PngEncoder.Encode"/>.
    /// </summary>
    public static byte[] Render(int sizePx, DrawCallback draw)
    {
        if (sizePx <= 0) throw new ArgumentOutOfRangeException(nameof(sizePx));

        IWICBitmap* bitmap = null;
        ID2D1RenderTarget* renderTarget = null;
        ID2D1SolidColorBrush* brush = null;
        try
        {
            Interop.WicFactory->CreateBitmap(
                (uint)sizePx, (uint)sizePx, PInvoke.GUID_WICPixelFormat32bppPBGRA, WICBitmapCreateCacheOption.WICBitmapCacheOnDemand, &bitmap);

            var props = new D2D1_RENDER_TARGET_PROPERTIES
            {
                type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_SOFTWARE,
                pixelFormat = new D2D1_PIXEL_FORMAT
                {
                    format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                    alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED,
                },
                dpiX = 96f,
                dpiY = 96f,
                usage = D2D1_RENDER_TARGET_USAGE.D2D1_RENDER_TARGET_USAGE_NONE,
                minLevel = D2D1_FEATURE_LEVEL.D2D1_FEATURE_LEVEL_DEFAULT,
            };
            Interop.D2DFactory->CreateWicBitmapRenderTarget(bitmap, &props, &renderTarget);

            renderTarget->CreateSolidColorBrush(new D2D1_COLOR_F { r = 0, g = 0, b = 0, a = 1 }, null, &brush);

            renderTarget->BeginDraw();
            renderTarget->Clear(new D2D1_COLOR_F { r = 0, g = 0, b = 0, a = 0 });
            draw(renderTarget, brush);
            renderTarget->EndDraw().ThrowOnFailure();

            return PixelExtraction.ExtractStraightRgba((IWICBitmapSource*)bitmap, sizePx, sizePx);
        }
        finally
        {
            if (brush != null) brush->Release();
            if (renderTarget != null) renderTarget->Release();
            if (bitmap != null) bitmap->Release();
        }
    }
}
