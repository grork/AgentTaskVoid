# INFRA-9: Integration-test harness over tasks.json
**Status:** DECIDED
**Plan:** phase-03
**Decision:** Two suites split by purpose. (1) A fake-backed LOGIC suite (all code
above the INFRA-8 seam) runs everywhere, always, in parallel -- each test owns a
`FakeAppTaskStore` and asserts via its `FindAll()`. (2) A small, data-driven, SERIAL
real-API ADAPTER suite proves the thin `AppTaskStore` drives the platform: it
registers-or-asserts identity (INFRA-14), shares the single per-identity `tasks.json`
(hence serial), clears before/after each test, and asserts via `FindAll()` + a raw
`tasks.json` reader. Skipped when the API is absent (INFRA-11).
**Parent:** INFRA-4

`tasks.json` is the observable output of the real API. What should integration
tests assert against it, and what harness do they need: identity registration
on the test machine, isolation between tests that share the single
per-identity `tasks.json`, cleanup guarantees, and serialization of tests that
mutate it?

Decision detail (2026-07-03):
- Split rationale: the fake covers everything above the seam, so the only thing the
  real API proves that the fake can't is that the thin adapter faithfully drives the
  platform. Real suite shrinks to adapter fidelity; everything else pushes down to the
  fast, runs-everywhere fake suite.
- Fake suite asserts: CRUD round-trip, advance model (ERGO-8), hidden-sweep (ERGO-2),
  sidecar reconciliation (ERGO-21), and the mutex mitigation -- the fake is faithfully
  NON-ATOMIC (models INFRA-5 whole-store clobber) with a test interleave hook, so
  unprotected write -> deterministic loss, mutex-wrapped write -> no loss.
- Fake needs controllable drift hooks (inject task-vanished / HiddenByUser / unknown-Id
  for ERGO-21), and is always non-atomic so any write path skipping the mutex shows up
  as loss.
- Real suite is deliberately small (the only flaky, env-bound, non-parallel suite):
  adapter translation + `tasks.json` fidelity, >=1 end-to-end per verb, and a PERIODIC
  reality-check that the platform still clobbers as modeled (INFRA-13/INFRA-15) -- the
  heavy 4x100 multi-process run lives here as periodic verification, not a gate.
- Not covered by either: the Shell crash/blank matrix (invisible in `tasks.json`) --
  that's INFRA-10 + hand-verification (INFRA-13).
- Raw `tasks.json` reading (watchdog, LIFE-5) is tested with hand-authored fixture
  files, not a fake emitter.
