# INFRA-23: The host-event behavior recorder (diagnostics tooling, separate from atv)
**Status:** EXPANDED
**Expanded into:** INFRA-24, INFRA-25, INFRA-26, INFRA-27, INFRA-28, INFRA-29 (+ INFRA-30, INFRA-31, added 2026-07-12; + INFRA-32, added 2026-07-13)

## Question
Design the standalone "real-world event behaviour" diagnostics tool for agent-CLI
hosts: hook configurations that camp on (as close to) every host event (as is safe)
and append each firing — full raw payload, verbatim — to a single structured log for
post-hoc analysis, with confirmed findings persisted as a token-cheap local reference
(the `docs/windows-ui-shell-tasks/` pattern) so future sessions read results instead
of re-running experiments.

## Ratified paradigm (operator, 2026-07-11 — the fixed part)
- **Separate from `atv`**: no atv dependency, no brand coupling; usable on machines
  where atv isn't installed.
- **Observe everything, analyze after, persist forever**: capture is raw/verbatim (no
  normalization, truncation, or interpretation at capture time); interpretation
  happens afterward, and outcomes — including explicit "did not fire" results — land
  in a durable findings doc (e.g. `docs/host-events/`).
- **Why it exists**: doc-derived hook mappings keep being wrong live (the phase-13
  dogfood caught two real bugs a docs-only pass missed; LIFE-24 "The host-event →
  task-state integration semantics" mapping rule 7: no host mapping counts as verified
  without live capture). First consumer: LIFE-24's open empirical items; also gates
  the deferred phase-13 Copilot CLI integration leg.

## Expansion note (operator + Claude Code, discovery, 2026-07-11)
"The details to sort out" bundled six largely-independent decisions into one question —
a classic EXPANDED candidate. Split along the operator's own opening framing (discrete
tool, shared infra, durable/updatable, event coverage) plus the original six items, each
of which is now its own child:

- [`INFRA-24`: Recorder tool architecture & repo placement](./INFRA-24-recorder-tool-architecture-and-repo-placement.md)
  — subsumes item 3 (form) + item 6's placement half.
- [`INFRA-25`: Host-event log envelope schema](./INFRA-25-host-event-log-envelope-schema.md)
  — subsumes item 4.
- [`INFRA-26`: Safe per-event hook coverage pass](./INFRA-26-safe-per-event-hook-coverage-pass.md)
  — subsumes item 1, unchanged.
- [`INFRA-27`: Observer posture vs teardown blocking budget](./INFRA-27-observer-posture-vs-teardown-blocking-budget.md)
  — subsumes item 2, unchanged.
- [`INFRA-28`: Capture scenario design & session driver](./INFRA-28-capture-scenario-design-and-session-driver.md)
  — subsumes item 5.
- [`INFRA-29`: Recorder lifecycle & maintenance model](./INFRA-29-recorder-lifecycle-and-maintenance-model.md)
  — subsumes item 6's lifecycle half.

A seventh candidate — formalizing captured payloads into a checked-in fixture corpus that
per-host translators (LIFE-24) get regression-tested against — was raised and explicitly
declined by the operator as a separate question (2026-07-11): the relationship stays as
already documented in LIFE-24 (rule 7, "Open empirical items"), consumed by whoever
authors a translator, not a formal pipeline.

Later additions (2026-07-12, phase-14 planning) — surfaced when phase-14 was scoped to
Claude Code only:
- [`INFRA-30`: Recorder rollout & harness integration](./INFRA-30-recorder-rollout-and-harness-integration.md)
  — DECIDED, consumed by phase-14: the core is proven only by a live host integration, and
  rollout is one host per phase gated on testability.
- [`INFRA-31`: Recorder legs for the not-yet-testable hosts](./INFRA-31-recorder-legs-for-not-yet-testable-hosts.md)
  — DEFERRED (2026-07-13): deferred-until-testable, single umbrella; pi carved out of v1 scope
  (LIFE-8). Each leg is planned directly by INFRA-30's policy when its host becomes testable.
- [`INFRA-32`: The host-onboarding playbook — trace to shipped integration](./INFRA-32-host-onboarding-playbook-trace-to-shipped-integration.md)
  — DEFERRED (2026-07-13, spawned while deferring INFRA-31): the reusable recipe for taking a
  runnable host from trace → verified mapping → shipped + wired atv integration. Deferred until
  the first few adapters are hand-built, since a repeatable recipe can only be distilled from
  repetition (n>1).

## Relationship to the plan
Once the children above are DECIDED, this becomes a new plan phase (phase 14) executed
under the normal executor→reviewer loop. Sequencing intent: before the deferred phase-13
Copilot CLI integration leg (the recorder is how that leg's mapping gets verified) and
before any LIFE-24 implementation work lands in atv.
