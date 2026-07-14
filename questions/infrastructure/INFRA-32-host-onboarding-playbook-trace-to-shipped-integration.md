# INFRA-32: The host-onboarding playbook — from a runnable host to a shipped, wired atv integration
**Status:** DEFERRED
**Deferred:** Filed + deferred 2026-07-13 (answer session, spawned while deferring INFRA-31).
A *repeatable* recipe can only be distilled from repetition, and today there is exactly one
worked, proven host integration (Claude Code, phases 13–14). Codifying a generalized playbook
from n=1 would be premature generalization — it would bake Claude-Code-specific accidents in as
if they were the common shape, the very failure the recorder exists to prevent (a doc-derived
mapping is an unverified hypothesis — INFRA-23 premise, LIFE-24 rule 7). The first few host
adapters (Copilot, Codex) should be built **artisanally** — hand-crafted end-to-end — so the
genuinely common pattern emerges empirically rather than being guessed. Only then is there
enough signal to write a recipe worth following. **Trigger to revisit:** after ~2–3 host
adapters have each been built end-to-end (recorder leg → verified findings → translator →
shipped + wired integration → live dogfood), distill the common pattern into the guide. This
naturally trails INFRA-31 (the per-host legs are themselves gated on host testability), so it
sits late in the sequence by construction.
**Parent:** INFRA-23

## Question
There will **always** be a host we want to integrate but cannot yet run locally (operator,
2026-07-13). The per-host recorder legs and integration legs perpetually defer on testability
(INFRA-31), but the durable, host-general asset is a **guide / recipe**: given a target host
we *can* now run, what is the repeatable end-to-end process to take it from zero to a
first-class atv integration? Concretely, a followable playbook covering the full chain:

1. **Trace** — stand up the host's recorder leg and capture real events (the recorder-half
   conventions already exist: `docs/host-events/README.md` §"Conventions a future host leg
   follows"; INFRA-26/27/28 instantiated per host).
2. **Verify** — distill captures into a durable findings doc (`docs/host-events/<host>.md`),
   version+date stamped, resolving that host's open empirical items.
3. **Translate** — author the host's translator from the verified findings using the LIFE-24
   mapping rules 1–7: the `map.json` extraction table (event→verb routing, tool→canonical-kind,
   property paths, value maps) + `translate.ps1` (or the pi in-process equivalent).
4. **Ship** — produce the `integrations/<host>/` artifact (LIFE-11) against the normative
   `docs/integration-api.md` verb contract (ERGO-31).
5. **Wire + deliver** — install/wire it into the host's own config and get it onto the machine
   (DIST-11).
6. **Dogfood** — verify end-to-end against a live session (LIFE-24 rule 7; the "doc-only
   verification produced plausible-but-wrong configs" lesson).

The deliverable is that guide — most likely extending `docs/host-events/README.md` past its
current recorder half into the full trace→ship arc, or a new `docs/host-onboarding.md` — with
Claude Code as the worked reference implementation each step points at.

**Pointer for when this is un-deferred (added phase 18, 2026-07-14):** the "translate" step's
offline-verification technique already has a worked, kept example — `translate.ps1` is tested
against a compiled stub `atv` (records argv+stdin, never the real binary or a live host) at
`tests/Atv.LogicTests/Integrations/{ClaudeCodeTranslatorHarness.cs, TestAssets/StubAtv/}`. Worth
citing directly rather than re-inventing when this guide's step 3 gets written.

## Why this surfaced
Answer session, 2026-07-13, while settling INFRA-31's disposition. INFRA-31 (the specific
Copilot / Codex / pi recorder legs) defers on host testability, but the operator noted the
enduring value is not any one leg — it is the *reusable methodology* for onboarding host N+1,
since the "host we lack but want" condition never goes away. That methodology is host-general
and (once distilled) buildable independent of any host being testable, so it does not belong
buried inside the deferred, recorder-only INFRA-31 — it is its own cross-cutting question.

## What makes it non-trivial (constraints)
- **Distillation needs ≥2–3 worked examples.** The whole point is to capture the *common*
  pattern; with only Claude Code done, there is nothing to generalize *from* without inventing
  it. This is the deferral's core reason, not an incidental caveat.
- **It spans four owners.** The chain crosses the recorder family (INFRA-23..30), the mapping
  semantics (LIFE-24), the shipped artifact (LIFE-11), and delivery/wiring (DIST-11). The guide
  must reference each without duplicating or re-deciding it — it is connective tissue, not new
  policy.
- **Hosts diverge in ways the recipe must accommodate, not paper over.** Spawn-per-event hosts
  (Claude Code / Copilot / Codex) vs pi's in-process TypeScript conduit; rich vs low payload
  resolution (LIFE-24 §fidelity-degrades-gracefully); churning hook surfaces (Codex). A recipe
  written from Claude Code alone risks encoding its comforts as requirements.

## Scope note
DEFERRED (not out of scope — sequenced late). In scope for the product eventually: shipping
per-host integrations is committed (LIFE-8/LIFE-11), and a repeatable onboarding recipe is how
that scales past the hand-built first few. Related: INFRA-31 (the deferred per-host legs that
each execution of this recipe would produce; this is the recipe they follow), INFRA-30
(rollout policy — one host per phase, gated on testability), INFRA-23..29 (recorder design +
`docs/host-events/README.md` recorder-half conventions), LIFE-24 (the mapping rules the
translate step applies), LIFE-11 (the artifact shape the ship step produces), ERGO-31
(`docs/integration-api.md` verb contract), DIST-11 (delivery + wiring the recipe closes with).
