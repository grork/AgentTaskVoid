# ERGO-12: Defaults for parameters that are secretly required
**Status:** DECIDED
**Decision:** Casual callers supply neither. `iconUri` defaults to a built-in
default glyph (ERGO-20) rendered to the per-handle path (ERGO-15); `deepLink`
defaults to a benign placeholder -- its meaningful click behavior is deferred
with the round-trip (INTER-4). [AMENDED 2026-07-05: "exact value an
implementation detail" proved not wavable (no inert URI exists, INTER-1);
ERGO-24 ("The default deepLink URI value") decided the value -- a `file:` URI to
the tool's app-data folder.]
**Parent:** ERGO-3

`deepLink` and `iconUri` look nullable but the native side requires real URIs
(see the docs README gotchas). What defaults does the CLI supply so casual
callers don't have to care -- and what do those defaults actually do when the
user clicks the card (deepLink) or looks at the taskbar (iconUri, which also
drives grouping, ERGO-13)?

Decision detail (2026-07-02): both are non-null-required by the native side, so
the CLI always fills them. The icon default flows from ERGO-20 (a default glyph)
written to a per-session path, so the default stays separation-by-session
(ERGO-15). The deepLink default just has to be inert and non-erroring in v1; what
a click should actually do is INTER-4 (deferred).
