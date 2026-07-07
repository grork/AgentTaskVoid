# INTER-3: The consumer-facing CLI surface for asking and awaiting
**Status:** DEFERRED
**Deferred:** Out of v1 scope -- display-only v1 (Q1, 2026-07-02). No ask/await
verb in v1; revisit with INTER-1/INTER-2.

What does a consumer invoke to ask? E.g. an `ask` verb that posts
NeedsAttention + SetQuestion content and blocks until an answer or timeout --
or split post/wait verbs? How does an await interact with idle expiry (LIFE-7)
and with the host's own hook timeout (hooks-first: a blocked hook blocks the
agent)?
