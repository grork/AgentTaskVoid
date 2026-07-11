using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectWrite;

namespace Atv.IconRendering;

/// <summary>
/// ERGO-22's presence probe: does <paramref name="fontFamily"/> actually have
/// a glyph for <paramref name="codepoint"/> on THIS machine/build? Answered
/// via <c>IDWriteFontFace.GetGlyphIndices</c> alone -- no drawing, no WIC/D2D
/// surface -- so it is cheap and side-effect-free to call before committing
/// to a real render. Per DirectWrite's own documented contract,
/// <c>GetGlyphIndices</c> returns glyph index 0 (the ".notdef"/undefined
/// glyph) for any code point the font doesn't map, which is exactly the
/// deterministic, non-throwing "not present" signal <see cref="GlyphRenderer"/>
/// needs (never an exception for an expected "this glyph doesn't exist"
/// outcome -- only for genuinely unexpected COM failures).
/// </summary>
public static unsafe class GlyphProbe
{
    /// <summary><see langword="true"/> if <paramref name="fontFamily"/> exists on the system font collection AND maps <paramref name="codepoint"/> to a real glyph (not the ".notdef" placeholder).</summary>
    public static bool IsPresent(string fontFamily, int codepoint)
    {
        IDWriteFontCollection* collection = null;
        IDWriteFontFamily* family = null;
        IDWriteFont* font = null;
        IDWriteFontFace* face = null;
        try
        {
            Interop.DWriteFactory->GetSystemFontCollection(&collection, false);

            collection->FindFamilyName(fontFamily, out uint index, out BOOL exists);
            if (!exists) return false;

            collection->GetFontFamily(index, &family);
            family->GetFirstMatchingFont(
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
                DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
                &font);
            font->CreateFontFace(&face);

            uint cp = (uint)codepoint;
            ushort glyphIndex;
            face->GetGlyphIndices(&cp, 1, &glyphIndex);
            return glyphIndex != 0;
        }
        finally
        {
            if (face != null) face->Release();
            if (font != null) font->Release();
            if (family != null) family->Release();
            if (collection != null) collection->Release();
        }
    }
}
