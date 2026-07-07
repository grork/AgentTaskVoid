# INFRA-11: Test strategy for machines where the API is unavailable
**Status:** DECIDED
**Decision:** Two suites split by purpose (see INFRA-9). The fake-backed LOGIC suite
(all code above the INFRA-8 seam) runs everywhere, always, in parallel, asserting via
the store's `FindAll()` -- no `tasks.json` emission. The real-API ADAPTER suite (small,
data-driven, serial) runs when the API is available and is SKIPPED otherwise. So the
API-absent story: the logic suite always runs on the fake; only the adapter suite
skips. The fake mimics only the bounded set of platform behaviors the logic tests
depend on (flagship: non-atomic whole-store clobber, INFRA-5), not full parity --
INFRA-15.
**Parent:** INFRA-4

The API is gradually rolling out; `IsSupported()` can throw
CLASS_E_CLASSNOTAVAILABLE even on otherwise-correct builds, and CI machines
likely lack it entirely. Do integration tests skip, fail, or run against a
simulated `tasks.json` layer in such environments -- and how do agents keep
hill-climbing without the real taskbar present?
