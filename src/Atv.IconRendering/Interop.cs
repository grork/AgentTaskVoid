using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace Codevoid.AgentTaskVoid.IconRendering;

/// <summary>
/// Process-lifetime COM bootstrap: one WIC imaging factory, one single-threaded
/// D2D factory, one shared DWrite factory -- exactly the three factory objects
/// ERGO-22's rendering path needs, created once and reused by every render call
/// (factories are the expensive/stateful part; the WIC bitmap + D2D render
/// target created PER render in <see cref="SoftwareCanvas"/> are cheap and
/// disposed each call).
///
/// Never releases these three: this is a short-lived CLI process, and COM
/// releases everything on process exit anyway -- matching the same
/// "no bespoke teardown ritual" spirit as the rest of this codebase's
/// short-lived-process assumptions.
/// </summary>
internal static unsafe class Interop
{
    private static readonly Lock s_lock = new();
    private static bool s_comInitialized;
    private static IWICImagingFactory* s_wicFactory;
    private static ID2D1Factory* s_d2dFactory;
    private static IDWriteFactory* s_dwriteFactory;

    public static IWICImagingFactory* WicFactory { get { EnsureInitialized(); return s_wicFactory; } }
    public static ID2D1Factory* D2DFactory { get { EnsureInitialized(); return s_d2dFactory; } }
    public static IDWriteFactory* DWriteFactory { get { EnsureInitialized(); return s_dwriteFactory; } }

    private static void EnsureInitialized()
    {
        if (s_wicFactory is not null) return;

        lock (s_lock)
        {
            if (s_wicFactory is not null) return;

            if (!s_comInitialized)
            {
                // MTA, deliberately (not STA): the factories below are created
                // once here and then shared process-wide by every subsequent
                // caller's thread (IconService has no thread-affinity contract of
                // its own, and this project's own test suite calls in from
                // whichever thread the test runner schedules). MTA + a
                // multi-threaded D2D factory (below) is the standard safe
                // combination for that -- an STA-created WIC/D2D factory would be
                // apartment-affine and unsafe to call from any thread but the one
                // that created it. S_OK / S_FALSE (already initialized) and even
                // RPC_E_CHANGED_MODE (a different model already active on this
                // thread) are all fine here: in every case COM is already usable
                // on this thread afterward, so the HRESULT is deliberately not
                // checked.
                PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
                s_comInitialized = true;
            }

            PInvoke.CoCreateInstance<IWICImagingFactory>(
                PInvoke.CLSID_WICImagingFactory, null, CLSCTX.CLSCTX_INPROC_SERVER, out IWICImagingFactory* wic)
                .ThrowOnFailure();

            PInvoke.D2D1CreateFactory(
                D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_MULTI_THREADED, typeof(ID2D1Factory).GUID, null, out void* d2dPtr)
                .ThrowOnFailure();

            PInvoke.DWriteCreateFactory<IDWriteFactory>(
                DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, out IDWriteFactory* dwrite)
                .ThrowOnFailure();

            s_d2dFactory = (ID2D1Factory*)d2dPtr;
            s_dwriteFactory = dwrite;
            s_wicFactory = wic; // last: publishes readiness to the racy fast-path check above
        }
    }
}
