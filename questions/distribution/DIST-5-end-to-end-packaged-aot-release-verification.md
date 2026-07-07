# DIST-5: End-to-end packaged-AOT release verification
**Status:** EXPANDED
**Expanded into:** DIST-7, DIST-8

Operator call (2026-07-04): expand rather than keep as one monolithic spike -- the
chain (AOT publish -> full-package manifest -> `winapp pack`/sign -> install -> alias
launch -> drive the API) bundles distinct sub-questions.

The INFRA-17 spike proved the two halves separately -- NativeAOT + the experimental
projection drives the API (under a sparse registration), and full-package identity works
under `dotnet run` -- but NOT the joined release leg: AOT-publish -> `winapp pack` into a
proper full-package MSIX -> install -> launch via the execution alias -> confirm it drives
`AppTaskInfo`. The spike hit the sparse-vs-full wall (`0x80073CF9`) because it packed our
current sparse manifest, not a full-package one. Adjacent evidence is strong; this is a
short confirmation, likely a build-phase spike, not an open design risk.
