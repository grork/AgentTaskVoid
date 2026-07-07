# ERGO-14: The CLI surface for expressing grouping intent
**Status:** DECIDED
**Decision:** v1 has no grouping knob. Every session is its own taskbar icon,
keyed on the handle (ERGO-15). Glomming multiple sessions under one icon is
deferred -- addable later as an optional `--group <key>` override with zero
reshaping of existing behavior. Package identity stays a non-knob (one identity).
**Parent:** ERGO-4

Do consumers get an explicit group key (which the CLI translates into icon-URI
management behind the scenes), raw icon passthrough with documented grouping
behavior, or both? Package identity is a harder grouping boundary but likely
not offerable as a knob -- the CLI registers one identity.

Decision detail (2026-07-02): the operator dropped the "group" field for v1
(skeptical of an arbitrary extra field; glomming is rarely wanted, ERGO-15).
Separation-by-session is the whole v1 behavior; `--group` reintroduces glom
purely additively if demand appears.
