using System.Text.RegularExpressions;

namespace Codevoid.AgentTaskVoid.Semantics;

/// <summary>
/// LIFE-24 S2-walk's "one shared engine normalizer" -- the single pipeline
/// EVERY single-line rendering (goal/question/summary/label) is proven
/// through once and reused: collapse whitespace runs (including embedded
/// newlines -- a multi-line prompt becomes one line) -&gt; strip light markdown
/// decorations (<c>**bold**</c>, `` `code` ``, <c>#</c> headers) -&gt; trim -&gt;
/// truncate with an ellipsis per <see cref="FieldBudgets"/>. AOT-safe by
/// construction (<c>[GeneratedRegex]</c>, never the interpreted
/// <see cref="Regex"/> constructor -- CLAUDE.md's AOT-size rule).
/// </summary>
public static partial class Normalizer
{
    /// <summary>The single ellipsis character used to mark a truncated line (matches <c>Codevoid.AgentTaskVoid.Run.LineHygiene.Ellipsis</c>'s own convention).</summary>
    public const string Ellipsis = "…";

    public static string Normalize(string? raw, int maxLength)
    {
        string s = raw ?? "";
        s = CollapseWhitespace(s);
        s = StripLightMarkdown(s);
        s = s.Trim();
        return Truncate(s, maxLength);
    }

    private static string CollapseWhitespace(string s) => WhitespacePattern().Replace(s, " ");

    private static string StripLightMarkdown(string s)
    {
        s = BoldPattern().Replace(s, "$1");
        s = CodePattern().Replace(s, "$1");
        s = HeaderPattern().Replace(s, "");
        return s;
    }

    /// <summary>Truncates to <paramref name="maxLength"/> INCLUDING the ellipsis -- same algorithm/contract as <c>Codevoid.AgentTaskVoid.Run.LineHygiene.Truncate</c>. A non-positive <paramref name="maxLength"/> is treated as "unbounded" (defensive, never a throw -- FAIL-1).</summary>
    private static string Truncate(string s, int maxLength)
    {
        if (maxLength <= 0 || s.Length <= maxLength) return s;
        if (maxLength == 1) return Ellipsis;
        return string.Concat(s.AsSpan(0, maxLength - 1), Ellipsis);
    }

    /// <summary>Any run of whitespace (spaces, tabs, CR, LF) collapses to one space -- this is what turns a multi-line prompt into a single-line rendering.</summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    /// <summary><c>**bold**</c> -&gt; <c>bold</c> (unwrap, keep the text).</summary>
    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldPattern();

    /// <summary>`` `code` `` -&gt; <c>code</c> (unwrap, keep the text).</summary>
    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex CodePattern();

    /// <summary>
    /// A markdown header marker (1-6 <c>#</c>) at the start of the (already
    /// whitespace-collapsed) string or immediately after any whitespace --
    /// stripped outright, leaving the header's own text in place. Matching on
    /// "start-or-after-whitespace" rather than a true line anchor works
    /// identically whether this runs before or after whitespace-collapsing
    /// (a header that began its own line always sits right after the space
    /// that replaced its preceding newline), so it stays correct under
    /// LIFE-24's documented collapse-then-strip order.
    /// </summary>
    [GeneratedRegex(@"(?:(?<=^)|(?<=\s))#{1,6}(?=\s|$)")]
    private static partial Regex HeaderPattern();
}

/// <summary>
/// Per-field truncation budgets for <see cref="Normalizer.Normalize"/> --
/// LIFE-24's "truncate with ellipsis per field budget." Build-phase defaults
/// (not a config tunable -- no acceptance criterion calls for one): sane
/// starting values sized to each field's altitude (a question/summary earns
/// more room than a terse per-tool activity label), matching the precedent
/// set by <c>Codevoid.AgentTaskVoid.Config.Settings.Default</c>'s own "sane build-phase
/// defaults" doc comment.
/// </summary>
public static class FieldBudgets
{
    public const int Goal = 200;
    public const int Question = 300;
    public const int Summary = 400;
    public const int Label = 150;
}
