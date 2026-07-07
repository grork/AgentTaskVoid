# INTER-2: Routing a received response back to the waiting consumer
**Status:** DEFERRED
**Deferred:** Out of v1 scope -- display-only v1 (Q1, 2026-07-02). No response is
routed back to a caller in v1; revisit with INTER-1.

The hook/script that posted the question may be blocked waiting for the
answer. Once INTER-1's receiver has the response, what channel delivers it --
a file the consumer polls/watches, a named pipe, something else? What happens
when the consumer has already exited by the time the user answers, and can
multiple questions be outstanding at once?
