# INTER-1: What receives Shell activations from buttons / text input
**Status:** DEFERRED
**Deferred:** Out of v1 scope -- v1 is display-only (Q1, 2026-07-02). The
interaction round-trip (buttons, text input, protocol activation, a receiver
process) is post-v1. Revisit when the round-trip is scoped. [Updated 2026-07-05
review pass: the originally recorded first-thing-to-validate -- whether protocol
activation works under an external-location sparse package -- is RETIRED; DIST-1
("The end-user distribution vehicle") dropped sparse for a signed full MSIX,
where protocol activation is first-class. The empirical findings below (no inert
URI; the receiver must be windowless) still stand.]

When a user clicks an `AddButton` button or submits `SetTextInput` text, the
Shell launches the content's actionUri (with `{userTextInput}` substituted).
Something we own must be on the other end: presumably a custom URI scheme
registered via the sparse package manifest (protocol activation), launching
some process of ours. What scheme do we register, what process handles the
activation (a fresh CLI instance? the watchdog from LIFE-4?), and does
protocol activation actually work under an external-location sparse package?

Findings (2026-07-03, from an ERGO-21 deepLink probe):
- Clicking a card whose deepLink uses an UNREGISTERED custom scheme (`atv://...`) pops a
  "look for an app in the Store" prompt -- so an inert click REQUIRES a registered
  handler; there is no no-op URI scheme.
- The receiver must be WINDOWLESS: a console exe launched by protocol activation flashes
  a terminal window, so the handler cannot be the console CLI -- it needs a separate
  GUI-subsystem process (or the LIFE-4 watchdog).
- These killed the idea of smuggling the handle in the deepLink for v1; ERGO-21 stays
  sidecar-as-source-of-truth.
