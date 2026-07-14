using System.Text.RegularExpressions;

namespace Atv.Semantics;

/// <summary>
/// LIFE-24's extraction/rendering split, the rendering half: host-independent
/// code that turns an <see cref="ActivityKind"/> + raw label/name into the
/// human-phrased line the card actually shows (ERGO-31 §2's "rendered word"
/// column). Translators (phase 18) hand over raw plucked values; this is
/// where "everything interesting" happens to them, per LIFE-24's own worked
/// example.
/// </summary>
public static partial class Rendering
{
    /// <summary>ERGO-31 §2: <c>compacting</c> renders this fixed phrase regardless of label.</summary>
    public const string CompactingLine = "Compacting conversation";

    /// <summary>
    /// Builds one <c>activity</c> line for <paramref name="kind"/>. Three
    /// shapes (ERGO-31 §2):
    /// <list type="bullet">
    /// <item><see cref="ActivityKind.Plan"/> -- the label IS the fully
    /// composed <c>"(n/m) item"</c> text (the translator composes it, not the
    /// engine) -- rendered as-is, just normalized.</item>
    /// <item><see cref="ActivityKind.Compacting"/> -- the fixed
    /// <see cref="CompactingLine"/> phrase, label ignored.</item>
    /// <item><see cref="ActivityKind.Tool"/> -- the unmapped-tool fallback:
    /// <paramref name="name"/> (the raw tool identifier, MCP-prettified) +
    /// <paramref name="label"/> (the subject).</item>
    /// <item>everything else -- <c>"&lt;verb word&gt; &lt;label&gt;"</c>.</item>
    /// </list>
    /// <paramref name="label"/> is normalized here (once, in the one place
    /// every activity line is built) via <see cref="Normalizer.Normalize"/>
    /// with <see cref="FieldBudgets.Label"/>.
    /// </summary>
    public static string BuildActivityLine(ActivityKind kind, string? label, string? name)
    {
        string normalizedLabel = Normalizer.Normalize(label, FieldBudgets.Label);

        return kind switch
        {
            ActivityKind.Plan => normalizedLabel,
            ActivityKind.Compacting => CompactingLine,
            ActivityKind.Tool => BuildToolFallback(name, normalizedLabel),
            _ => normalizedLabel.Length > 0 ? $"{ActivityKinds.VerbWord(kind)} {normalizedLabel}" : ActivityKinds.VerbWord(kind),
        };
    }

    private static string BuildToolFallback(string? name, string normalizedLabel)
    {
        string? prettyName = name is { Length: > 0 } ? PrettifyToolName(name) : null;

        return (prettyName, normalizedLabel.Length > 0) switch
        {
            (null, false) => "Running a tool",
            (null, true) => normalizedLabel,
            (not null, false) => prettyName,
            (not null, true) => $"{prettyName}: {normalizedLabel}",
        };
    }

    /// <summary>
    /// ERGO-31 §2's "engine prettifies the MCP <c>mcp__&lt;server&gt;__&lt;tool&gt;</c>
    /// pattern" -- an MCP-wide naming convention, so prettifying it engine-side
    /// (rather than per-translator) is fair (LIFE-24 S2-walk item 4). A name
    /// that doesn't match the pattern is prettified as a single token instead
    /// (underscores/hyphens -&gt; spaces, each word capitalized).
    /// </summary>
    public static string PrettifyToolName(string rawName)
    {
        Match match = McpToolPattern().Match(rawName);
        return match.Success
            ? $"{PrettifyToken(match.Groups[1].Value)}: {PrettifyToken(match.Groups[2].Value)}"
            : PrettifyToken(rawName);
    }

    private static string PrettifyToken(string token)
    {
        string spaced = token.Replace('_', ' ').Replace('-', ' ');
        string[] words = spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? token : string.Join(' ', words.Select(Capitalize));
    }

    private static string Capitalize(string word)
        => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    /// <summary>ERGO-31 §2's <c>mcp__&lt;server&gt;__&lt;tool&gt;</c> convention: server = up to the first double-underscore, tool = the rest (which may itself contain underscores).</summary>
    [GeneratedRegex(@"^mcp__([^_]+)__(.+)$")]
    private static partial Regex McpToolPattern();
}
