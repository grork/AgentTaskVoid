# INFRA-13: Windows build compatibility strategy for an experimental API
**Status:** DECIDED
**Plan:** phase-06
**Decision:** Don't version-pin -- detect capability at runtime (`IsSupported()` +
identity, wrapped for the CLASS_E_CLASSNOTAVAILABLE COMException) and degrade
non-disruptively (FAIL-1, "Failure posture toward the host caller"). Document a soft
floor (Win11 26100+, AppTaskContract v2) for expectation-setting only; the runtime
check is authoritative -- robust to both "not rolled out yet" and "changed/removed
later." `atv doctor` (FAIL-3, "Diagnosability when nothing shows on the taskbar") is
the detection surface. Empirical behaviors (the state x content crash matrix,
grouping-by-IconUri) can't be auto-detected -- a documented manual checklist re-checks
them on new builds (low-pri, per the operator note below); shifts update the
matrix-as-data (INFRA-10) and the fake's mimicked list (INFRA-15). GA/contract change
is not pre-solved; blast radius stays localized to the one adapter (INFRA-8).

The namespace is [Experimental], gated behind a gradual rollout, split across
contract v1/v2, and key Shell behaviors we depend on are empirical and
undocumented (grouping keyed by IconUri, the state x content crash matrix).
What build range do we claim support for, how do we detect what a given
machine actually does, and what is the plan when a new Windows build shifts
the empirical behavior or the API goes GA with changes?

Operator note (2026-07-03): the periodic re-verification that the empirical behaviors
still hold on a new Windows release -- delegated here from INFRA-10 (testing the
state x content crash matrix) and INFRA-15 (behaviors the fake must mimic) -- is
acknowledged "dark matter" future work and is LOW priority generally.
