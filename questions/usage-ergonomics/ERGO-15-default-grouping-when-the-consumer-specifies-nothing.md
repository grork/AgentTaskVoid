# ERGO-15: Default grouping when the consumer specifies nothing
**Status:** DECIDED
**Decision:** Separate by default, keyed on the session handle (ERGO-6) -- each
session gets its own taskbar icon. Glomming multiple sessions under one icon is
NOT the default (rarely wanted); it is at most opt-in, and whether that mechanism
even ships in v1 is ERGO-14. No magic auto-detection (cwd/PID rejected).
**Parent:** ERGO-4

With no explicit grouping input, do all callers glom under one shared CLI icon,
or does the CLI separate automatically -- and if automatically, keyed by what
(calling process, working directory, session)?

Decision detail (2026-07-02): the CLI already holds the session handle (ERGO-6)
and grouping is keyed by icon-URI string (ERGO-13), so the default grouping key
is simply the handle -> the CLI writes the task's icon to a per-handle path ->
each session is its own taskbar icon. This reverses the earlier "glom under one
shared icon" lean: the operator's view is that glomming is the exception, not the
default, and keying on the session id we already have beats inventing an
arbitrary "group" field.
