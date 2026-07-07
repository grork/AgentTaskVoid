# ERGO-11: Expressing structured and multi-valued inputs on the command line
**Status:** DECIDED
**Decision:** v1 has no multi-valued CLI inputs. The completedSteps array is
built server-side by the advance model (ERGO-8), not passed; buttons/text-input
are deferred with the interaction round-trip (INTER-*); `--assets` is deferred
(ERGO-9). Every v1 arg is a single scalar. Revisit when a multi-valued feature
actually lands.
**Parent:** ERGO-3

How are completedSteps arrays, buttons (label + action-URI pairs, up to
`MaxButtons`), and text-input templates (must contain the literal
`{userTextInput}` token) passed -- repeated flags, delimited values, or JSON?

Decision detail (2026-07-02): the lean surface deliberately sidesteps this --
steps accrue via repeated `step` calls, questions are a single string on
`attention`, states are an enum. If/when `--assets` (array) or buttons/text-input
(deferred round-trip) arrive, choose the passing convention then (leaning:
repeated flags for small arrays).
