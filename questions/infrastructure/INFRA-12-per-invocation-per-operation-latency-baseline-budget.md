# INFRA-12: Per-invocation / per-operation latency baseline & budget
**Status:** DEFERRED
**Deferred:** Operator call (2026-07-04): we are overthinking performance budgets.
Realistic hook rates (~1-2 writes/sec) against the measured ~18ms serialized write
(INFRA-5, "Empirical behavior of concurrent API calls from separate processes") leave
no plausible budget problem, lock-derived scenarios included. No baseline, budget, or
measurement-harness work; the INFRA-6 bounded mutex-wait timeout stays a build-phase
tuning value. Revisit only if real usage shows a blocked host.

Hooks fire on frequent agent events and can block the host until they return
(hooks-first priority -- operator, 2026-07-02). What per-invocation latency is
acceptable, and what does it actually measure on this code?

Correction (operator, 2026-07-02): the original phrasing "JIT, CsWinRT init" is
wrong -- CsWinRT is a thin wrapper over WinRT/COM, not a separate init phase. The
.NET startup cost is runtime bring-up + JIT; NativeAOT (INFRA-3) removes the JIT.

Expansion seeds (operator, 2026-07-02) -- do NOT baseline against the current
partial build; with so much still to build, an early number would mislead.
Capture these before measuring:
- Per-operation, not one number: setting up a task (create), tearing down
  (remove), and updating a task each cost differently and must be measured
  separately.
- When to measure: defer until enough is built to be representative.
- Acceptable budget/threshold per operation, given a blocked hook blocks the host.
- Methodology: cold vs warm start, machine/environment, AOT vs JIT build, and
  whether a running watchdog (LIFE-4) affects it.
