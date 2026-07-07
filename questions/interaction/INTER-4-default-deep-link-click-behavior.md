# INTER-4: Default deep-link click behavior
**Status:** DEFERRED
**Deferred:** Out of v1 scope -- display-only v1 (Q1, 2026-07-02). "Focus the
originating terminal on click" needs the deferred activation infra (INTER-1). v1
still must pass a deepLink (required param); the default value is decided by
ERGO-24, "The default deepLink URI value" (a `file:` URI into app-data --
supersedes ERGO-12's original placeholder wave-off). Revisit
click-does-something-useful behavior post-v1.

Clicking a card invokes its DeepLink (docs README). For our scenarios, should
the default click bring the user to the originating terminal/window -- and is
that achievable from a URI activation? Related: ERGO-12 decides the default
deepLink *value*; this question is about what that default should *do*.
