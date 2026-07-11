using System.Text;
using System.Text.RegularExpressions;

namespace Atv.Run;

/// <summary>
/// ERGO-5's fixed, bounded per-line hygiene pipeline for the `run` wrapper's
/// STEP copy only -- <see cref="OutputPump"/>'s terminal mirror is
/// byte-for-byte untouched (transparency); this is what turns a raw decoded
/// line into a short, readable string for the taskbar card. Six fixed steps,
/// no config/regex knobs beyond <paramref name="maxLength"/>
/// (<see cref="Config.Settings.RunStepMaxLength"/>) -- deliberately does NOT
/// interpret meaning (no progress %, no phase/error detection) or emulate a
/// terminal grid; see the phase file's "explicitly OUT" list.
/// </summary>
public static partial class LineHygiene
{
    /// <summary>The single ellipsis character used to mark a truncated line.</summary>
    public const string Ellipsis = "…";

    /// <summary>
    /// Step 1: strip ANSI/VT escape sequences -- one regex covering CSI
    /// (<c>ESC [ params... intermediates... final-byte</c>, the form
    /// virtually every color/cursor/erase code uses) and the other
    /// two-byte "Fe" escapes (<c>ESC</c> + one byte in <c>@A-Z\]^_</c>,
    /// excluding <c>[</c> which the CSI branch already owns). A bare
    /// <c>ESC</c> NOT followed by either -- e.g. stray/malformed input --
    /// matches neither branch here and is left for step 3's generic
    /// control-char scrub to drop on its own, WITHOUT eating the next,
    /// perfectly normal character along with it.
    /// </summary>
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B[@A-Z\\\]^_]", RegexOptions.Compiled)]
    private static partial Regex AnsiEscapePattern();

    /// <summary>
    /// Cleans one raw decoded line (may still contain embedded <c>\r</c> --
    /// <see cref="OutputPump"/> only splits on <c>\n</c>) through the fixed
    /// pipeline: strip ANSI -&gt; collapse <c>\r</c> overwrites -&gt; scrub
    /// control chars -&gt; trim -&gt; drop-if-blank (returns
    /// <see langword="null"/>) -&gt; truncate + ellipsis.
    /// </summary>
    public static string? Clean(string rawLine, int maxLength)
    {
        string s = AnsiEscapePattern().Replace(rawLine, "");
        s = CollapseCarriageReturns(s);
        s = ScrubControlChars(s);
        s = s.Trim();

        return s.Length == 0 ? null : Truncate(s, maxLength);
    }

    /// <summary>
    /// Step 2: a progress bar redrawn via repeated <c>\r</c> (no <c>\n</c>)
    /// becomes its final value -- keep only the text after the LAST
    /// remaining <c>\r</c>. A single TRAILING <c>\r</c> is instead a CRLF
    /// terminator remnant (<see cref="OutputPump"/> splits on <c>\n</c> only,
    /// so a Windows <c>"foo\r\n"</c> line arrives here as <c>"foo\r"</c>) --
    /// stripped first so a plain CRLF line is never mistaken for an
    /// overwrite and wrongly emptied.
    /// </summary>
    private static string CollapseCarriageReturns(string s)
    {
        if (s.EndsWith('\r'))
            s = s[..^1];

        int lastCr = s.LastIndexOf('\r');
        return lastCr < 0 ? s : s[(lastCr + 1)..];
    }

    /// <summary>Step 3: tabs become a single space; every other remaining C0 control char (bell, backspace, null, form-feed, ...) is dropped outright.</summary>
    private static string ScrubControlChars(string s)
    {
        StringBuilder sb = new(s.Length);
        foreach (char c in s)
        {
            if (c == '\t') sb.Append(' ');
            else if (char.IsControl(c)) continue;
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Step 6: truncates to <paramref name="maxLength"/> INCLUDING the ellipsis. A non-positive <paramref name="maxLength"/> is treated as "unbounded" -- a defensive fallback for a malformed config value, never a throw (FAIL-1).</summary>
    private static string Truncate(string s, int maxLength)
    {
        if (maxLength <= 0 || s.Length <= maxLength) return s;
        if (maxLength == 1) return Ellipsis;
        return string.Concat(s.AsSpan(0, maxLength - 1), Ellipsis);
    }
}
