# DIST-1: The end-user distribution vehicle
**Status:** DECIDED
**Plan:** phase-01
**Decision:** One signed **full MSIX** containing the NativeAOT `atv.exe` (plus an
`AppExecutionAlias` putting `atv` on PATH), delivered via **winget**. Built and signed
with Microsoft's **winapp CLI** (`winapp pack` / `winapp sign`) -- the same tool that
drives the dev loop (INFRA-17). Full-package identity model end to end: loose-layout
identity in dev, full MSIX at release; the hand-rolled sparse package is dropped.

Rationale (2026-07-04):
- Identity + PATH + install + update + uninstall become one per-user, no-admin,
  no-Dev-Mode operation (`winget install`). A signed *sparse* alternative would make us
  hand-build the installer, PATH management, and a pinned exe location that MSIX gives
  free -- and, per INFRA-17's spike, sparse is structurally incompatible with the winappcli
  dev loop we adopted.
- Smallest artifact that actually FUNCTIONS: the ~3 MB NativeAOT exe (INFRA-2,
  "Minimizing the on-disk size") ships inside the package unchanged; a bare exe is smaller
  but has no identity, so it can't work.
- Retires INTER-1's ("What receives Shell activations") open risk -- protocol activation is
  first-class in full MSIX, unlike an external-location sparse package.

Architectures (ratified 2026-07-05): the release ships BOTH x64 and ARM64 NativeAOT
builds; the winget manifest carries both (per-arch MSIX vs bundle is a build detail).
Folded in from the 2026-07-05 review pass rather than a standalone question.
