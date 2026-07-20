namespace Codevoid.AgentTaskVoid.Semantics;

/// <summary>ERGO-31 §3's closed <c>session-ended --reason</c> vocabulary -- token-only, no free-text (unlike <see cref="BrokenReasonToken"/>, no <c>--detail</c>).</summary>
public enum SessionEndedReasonToken
{
    /// <summary>The turn/session ended normally -- the card is removed.</summary>
    Finished,

    /// <summary>The session died -- the card surfaces as Broken (the watchdog then reaps it).</summary>
    Error,
}

/// <summary>Token &lt;-&gt; <see cref="SessionEndedReasonToken"/> parsing.</summary>
public static class SessionEndedReasons
{
    public static bool TryParse(string? token, out SessionEndedReasonToken reason)
    {
        switch (token)
        {
            case "finished":
                reason = SessionEndedReasonToken.Finished;
                return true;
            case "error":
                reason = SessionEndedReasonToken.Error;
                return true;
            default:
                reason = default;
                return false;
        }
    }
}
