namespace Codevoid.AgentTaskVoid.Semantics;

/// <summary>ERGO-31 §3's closed <c>broken --reason</c> vocabulary. <see cref="Fatal"/> is the catch-all.</summary>
public enum BrokenReasonToken
{
    RateLimit,
    Overloaded,
    ApiError,
    Timeout,
    Fatal,
}

/// <summary>Token &lt;-&gt; <see cref="BrokenReasonToken"/> parsing and the fixed rendered phrase for each.</summary>
public static class BrokenReasons
{
    private static readonly IReadOnlyDictionary<string, BrokenReasonToken> Tokens = new Dictionary<string, BrokenReasonToken>(StringComparer.Ordinal)
    {
        ["rate-limit"] = BrokenReasonToken.RateLimit,
        ["overloaded"] = BrokenReasonToken.Overloaded,
        ["api-error"] = BrokenReasonToken.ApiError,
        ["timeout"] = BrokenReasonToken.Timeout,
        ["fatal"] = BrokenReasonToken.Fatal,
    };

    public static bool TryParse(string? token, out BrokenReasonToken reason)
    {
        if (token is not null && Tokens.TryGetValue(token, out reason))
            return true;

        reason = default;
        return false;
    }

    /// <summary>ERGO-31 §3's fixed rendered phrase, prefixed onto an optional free-text <c>--detail</c> (e.g. "API error: connection reset by peer").</summary>
    public static string Render(BrokenReasonToken reason) => reason switch
    {
        BrokenReasonToken.RateLimit => "Rate limited",
        BrokenReasonToken.Overloaded => "Overloaded",
        BrokenReasonToken.ApiError => "API error",
        BrokenReasonToken.Timeout => "Timed out",
        BrokenReasonToken.Fatal => "Failed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown broken reason."),
    };
}
