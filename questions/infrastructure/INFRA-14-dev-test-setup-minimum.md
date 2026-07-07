# INFRA-14: Dev/test setup minimum
**Status:** EXPANDED
**Expanded into:** INFRA-16, INFRA-17

Operator direction (2026-07-02, discovery): end-user first-run setup is out of
scope until the deferred distribution decision (brief assertion 2); only the
minimum needed to keep development and testing unblocked is in scope now.
What is that minimum beyond today's `Register-Identity.ps1` -- e.g. automatic
registration inside the integration-test harness (INFRA-9), re-registration
when `identity\AppxManifest.xml` changes?
