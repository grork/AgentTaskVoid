namespace Codevoid.AgentTaskVoid.Semantics;

/// <summary>
/// ERGO-31 §2's closed kind vocabulary: names the MECHANISM (never the
/// purpose -- LIFE-24 mapping rule "kinds name the mechanism, never the
/// purpose"). Small and closed by design: an unmapped host tool falls back
/// to <see cref="Tool"/> rather than growing this list, so the engine never
/// gates a new tool.
/// </summary>
public enum ActivityKind
{
    Read,
    Edit,
    Write,
    Search,
    Shell,
    Fetch,
    WebSearch,
    Plan,
    Compacting,
    Tool,
}

/// <summary>Token &lt;-&gt; <see cref="ActivityKind"/> parsing (ERGO-31 §2's exact CLI tokens) and the fixed rendered-verb-word table.</summary>
public static class ActivityKinds
{
    private static readonly IReadOnlyDictionary<string, ActivityKind> Tokens = new Dictionary<string, ActivityKind>(StringComparer.Ordinal)
    {
        ["read"] = ActivityKind.Read,
        ["edit"] = ActivityKind.Edit,
        ["write"] = ActivityKind.Write,
        ["search"] = ActivityKind.Search,
        ["shell"] = ActivityKind.Shell,
        ["fetch"] = ActivityKind.Fetch,
        ["web-search"] = ActivityKind.WebSearch,
        ["plan"] = ActivityKind.Plan,
        ["compacting"] = ActivityKind.Compacting,
        ["tool"] = ActivityKind.Tool,
    };

    /// <summary>Parses a <c>--kind</c> token. Case-sensitive, exact match against ERGO-31 §2's literal tokens (lowercase, hyphenated) -- deliberately not case-insensitive: these are host-translator-constant strings, not user-typed input.</summary>
    public static bool TryParse(string? token, out ActivityKind kind)
    {
        if (token is not null && Tokens.TryGetValue(token, out kind))
            return true;

        kind = default;
        return false;
    }

    /// <summary>
    /// ERGO-31 §2's rendered verb word -- prefixed onto the normalized label
    /// for the six "verb + subject" kinds. The other four
    /// (<see cref="ActivityKind.Plan"/>/<see cref="ActivityKind.Compacting"/>/
    /// <see cref="ActivityKind.Tool"/>) render their OWN full content shape
    /// instead (see <see cref="Rendering.BuildActivityLine"/>) and have no
    /// single "verb word" -- calling this for one of them is a programming
    /// error in this codebase (every call site routes through
    /// <see cref="Rendering.BuildActivityLine"/>, which never reaches here
    /// for those three).
    /// </summary>
    public static string VerbWord(ActivityKind kind) => kind switch
    {
        ActivityKind.Read => "Reading",
        ActivityKind.Edit => "Editing",
        ActivityKind.Write => "Writing",
        ActivityKind.Search => "Searching",
        ActivityKind.Shell => "Running",
        ActivityKind.Fetch => "Fetching",
        ActivityKind.WebSearch => "Searching the web",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"{kind} renders its own content shape -- see Rendering.BuildActivityLine, not VerbWord."),
    };
}
