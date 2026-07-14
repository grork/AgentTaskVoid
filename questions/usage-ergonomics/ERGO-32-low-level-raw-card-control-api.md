# ERGO-32: A low-level / raw card-control API
**Status:** DEFERRED (2026-07-13)
**Deferred:** No raw tier ships; the ERGO-31 ("The v2 semantic verb contract") verbs stay the sole
lifecycle surface. Rationale (full detail in the body below): no concrete consumer needs it (pi
differs only in *delivery*, not vocabulary, and uses the smallest verb subset); "more control" is
mostly control we deliberately removed (raw state×content re-exposes the ERGO-10 crash surface;
opting out of the engine clocks leaks taskbar entries); the one genuine gap — unmodeled content
shapes (buttons / text-input / rich result assets) — overlaps the deferred two-way round-trip
(INTER-1/2/3), so a raw API would front-run a parked decision; and it is the same
"structured-verbs-stop-being-the-API" anti-pattern LIFE-24 rejected 3×. **Revisit trigger:** a
concrete consumer (host or tool) the semantic verbs genuinely cannot serve, weighed against the
INTER-* two-way work. Operator call, 2026-07-13.

## Question
Should `atv` expose a supported **low-level tier** — raw card control mapping ~1:1 to the
AppTaskInfo primitives (set an arbitrary state, set a specific content shape, set/clear a
question) — *alongside* the ERGO-31 ("The v2 semantic verb contract") semantic verbs, for a
host or tool that wants to drive the card imperatively rather than emit claims the engine
interprets? If so, in what shape (a `card`/`raw` verb namespace vs. repurposing the retired
v1 verbs), and how do a raw write and a semantic claim over the same handle interact?

## Why deferred, not decided (2026-07-13)
Raised while answering ERGO-31 (operator: is there utility in a "low-level" abstraction for
tools that want more control — e.g. pi as "a different beast" given raw material rather than
shoe-horned into the v2 paradigm?). The reasoning that landed it here as its own OPEN question
rather than an answer inside ERGO-31:

- **pi's difference is delivery, not vocabulary.** pi is "different" because it is in-process
  TypeScript with no spawn-per-event — which the LIFE-24 conduit/translator/engine layering
  already accommodates (its conduit+translator collapse into one TS extension). pi still
  projects the same lifecycle onto the same claims (`turn_start`→Working, `turn_end`→Ready,
  error→Broken); with no permission system and no subagents it simply uses the *smallest*
  subset of the semantic verbs. It is the host least likely to want raw control.
- **"More control" is mostly control we deliberately removed.** Setting arbitrary
  (state, content) combos re-exposes the ERGO-10 ("Guarding unsupported state × content ×
  mutator combinations") crash surface (some combos hard-crash `explorer.exe`). Opting out of
  the engine's clocks means opting out of Ready decay and orphan-reap — i.e. leaking taskbar
  entries. Those are the engine being the product, not withheld capabilities.
- **The genuine gap is content shapes the semantic verbs don't model** — `CreatePreviewThumbnail`,
  `CreateGeneratedAssetsResult`, and `AddButton`/`SetTextInput`. But buttons + text-input ARE
  the deferred two-way round-trip ([[INTER-1]]/[[INTER-2]]/[[INTER-3]]). A low-level API would
  front-run a decision deliberately parked. This question is cross-linked to that scope.
- **It is the anti-pattern LIFE-24 rejected 3×.** `atv ingest`, the profile-schema, and
  raw-payload-on-stdin were each rejected because "the structured verbs stop being the API." A
  raw card-control tier is the same corrosion in a different hat — the lazy integrator's path
  of least resistance, after which cards stop meaning the same thing across hosts.

## Trigger to revisit
A concrete consumer (host or tool) that the ERGO-31 semantic verbs genuinely cannot serve —
at which point weigh it against the INTER-* two-way work, since the real unmet capability
(interactive buttons / text input / rich result assets) largely overlaps that scope.

Filed by the ERGO-31 answer session (2026-07-13).
