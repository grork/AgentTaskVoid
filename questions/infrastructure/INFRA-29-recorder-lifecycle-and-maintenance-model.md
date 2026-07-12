# INFRA-29: Recorder lifecycle & maintenance model
**Status:** DECIDED
**Plan:** phase-14
**Parent:** INFRA-23
**Decision:** Durable and maintained: each finding in `docs/host-events/<host>.md` is
stamped with the host version + capture date (reusing the `integrations/claude-code/`
convention), re-captured organically when a stamp is stale vs the installed host — no
CI/cadence; the verbatim core (INFRA-24) never changes on a host update, only the per-host
conduit + scenario + findings doc churn; the LIFE-24 relationship stays documentation-only.

## Question
Is the recorder a maintained artifact or a throwaway-per-investigation tool, and — since
it's a diagnostic feeding an evolving target (host event vocabularies) — what does
"maintained" concretely require?

Operator direction (2026-07-11, discovery): **durable, not throwaway** — the tool needs
to stay usable as hosts add/change events over time (Codex hooks are "weeks old and
churning" per LIFE-24's grounding notes) and to keep the findings docs
(`docs/host-events/`) current, not just a one-shot experiment.

## Decision (operator + Claude Code, answer session, 2026-07-12)
1. **What "keeping findings current" requires.** Each finding in `docs/host-events/<host>.md`
   carries a **host version + capture date** stamp — reusing the exact convention already
   in `integrations/claude-code/README.md` ("Verified against: Claude Code 2.1.207 …
   fetched 2026-07-10"). **No CI, no fixed ownership cadence** (over-engineering for a
   solo project). The re-capture trigger is **organic**: a session about to trust a host
   mapping checks the stamp and re-runs the capture if it is stale relative to the
   installed host version — the same "docs consulted by whoever authors a translator"
   model LIFE-24 already relies on.
2. **The stable/churn line follows the three-layer split (LIFE-24).** The recorder **core**
   (INFRA-24's append exe) is host-agnostic and **verbatim** — it reads stdin and writes an
   envelope; it does not understand payloads — so a host's breaking hook change **never
   touches it**. That is precisely *why* the core is dumb. What churns per host is (a) the
   conduit / hook config (which events, registration syntax), (b) the capture scenario /
   driver (INFRA-28), and (c) the findings doc. A host hook change → update the per-host
   config + re-capture + update findings; the core stays put.
3. **Relationship to LIFE-24 (unchanged, restated).** These captures are the empirical
   ground truth LIFE-24 rule 7 requires before any host mapping counts as verified. The
   operator explicitly declined (2026-07-11) formalizing that into a checked-in
   regression-fixture corpus that per-host translators are tested against — the
   relationship stays **documentation-only** (findings docs a human consults when authoring
   a translator), as captured in LIFE-24's "Open empirical items." Revisit only if manual
   consultation proves insufficient once real translator drift is observed.
