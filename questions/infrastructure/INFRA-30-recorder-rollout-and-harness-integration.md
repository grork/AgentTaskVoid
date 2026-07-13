# INFRA-30: Recorder rollout & harness integration
**Status:** DECIDED
**Plan:** phase-14
**Parent:** INFRA-23
**Decision:** The verbatim core is unfalsifiable in isolation — it is proven only by a live
host integration that spawns it through real hooks and produces real captures. So every
recorder phase pairs its build with the integration that validates it, and rollout is one
host per phase, each gated on that host being testable. Phase-14 deliberately couples the
core with the Claude Code integration as the core's proof; whether and how the other hosts'
legs get built is a separate, not-yet-processed question (INFRA-31, OPEN).

## Question
INFRA-24..29 decided the recorder's *design* host-generally, and INFRA-28 the per-host
driver harness. What they left implicit — and what the 2026-07-12 scoping of phase-14 to
Claude Code made load-bearing — is two coupled things:
1. **What "done" means for the core.** Unit tests + AOT smoke prove byte-fidelity and the
   guarded append, but not that the exe works as a host-event recorder. Is the core
   meaningfully complete without a live integration exercising it?
2. **How the recorder rolls out across hosts** — all at once, or incrementally — and how a
   phase's scope reflects a build that spans multiple hosts over time.

Without these decided, a phase author improvises scope inline (the first phase-14 draft's
scattered "Claude Code only" caveats), and a reader can't tell whether "core built" means
"core proven."

## Decision (operator + Claude Code, 2026-07-12)
1. **Proof in the pudding — the core is validated only by a live integration.** The shared
   core (INFRA-24/25) has no standalone "done": its unit suite + AOT smoke are a
   *ready-to-integrate* bar, not a validation. It is proven when a real host spawns it via
   real hooks and the captured envelopes are correct against real payloads. Every recorder
   phase therefore includes the live integration that proves what it built.
2. **Phase-14 = core + the Claude Code integration, coupled deliberately.** Claude Code is
   not merely "host #1"; it is the integration that validates the core. Phase-14 is not
   complete — and the core is not proven — until the Claude Code live capture works.
   Phase-14's own acceptance carries this.
3. **Rollout: one host per phase, gated on testability.** The core is built once. Each
   additional host's leg (conduit, matrix, driver, capture, findings — INFRA-26/27/28/29
   instantiated for that host) is its own future phase, planned only when that host is
   installed and testable on the working box. The verbatim core admits a new host with no
   change (INFRA-29), so waiting costs nothing and nothing in phase-14 is provisional on
   their behalf.
4. **The design is not re-decided per host.** INFRA-24..29 keep their `phase-14` stamp — the
   core is fully consumed there, and the reusable per-host pattern is established and
   documented in `docs/host-events/README.md`. A future host leg *instantiates* that decided
   design; it does not re-open those questions.
5. **The remaining hosts are a separate, not-yet-processed question.** Whether and how the
   Copilot / Codex / pi legs get built is **INFRA-31 (OPEN)** — the answer process has not
   been run over it. It is NOT pre-deferred here; a future session decides its disposition
   (a deferred-until-testable outcome is likely, but that is that session's call).

## Relationship to process
This question is fully consumed by phase-14: it decides a policy that phase-14 enacts and
demonstrates, so it carries an ordinary `phase-14` stamp — no incremental / partial-
consumption machinery on `process.md` is needed (an earlier draft reached for one; the
mis-framing was the smell). The remaining-host work lives in INFRA-31 and is tracked by the
ordinary question lifecycle (OPEN → answered when its host is testable), not by a lingering
plan stamp on this one.
