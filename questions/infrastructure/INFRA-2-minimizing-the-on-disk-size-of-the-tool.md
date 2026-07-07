# INFRA-2: Minimizing the on-disk size of the tool
**Status:** DECIDED
**Decision:** Accept the NativeAOT single-file self-contained exe -- **~3.0-3.5 MB**
(~2.5 MB after free size levers), delivered inside the ~5 MB MSIX (compressed payload
~1.5 MB on the wire). Judged acceptable ("download and rock it"). Bank the free
ship-config levers, take no extraordinary measures: `OptimizationPreference=Size`,
`InvariantGlobalization` (if nothing relies on culture-sensitive formatting), the feature
switches (`StackTraceSupport`/`UseSystemResourceKeys`/`DebuggerSupport`/`EventSourceSupport`
off), and don't ship the `.pdb`.

Sub-1 MB is explicitly NOT a goal: the floor is ~1-1.3 MB .NET NativeAOT runtime + ~1.5-2 MB
CsWinRT (`WinRT.Runtime` + projection), so reaching <=1 MB would require abandoning
C#/CsWinRT -- a native C++/Rust rewrite or a shared-runtime framework dependency -- which
INFRA-3's ("Writing the tool in C++/Rust... vs. readability") readability priority rejects.
Not a hard line for the operator; 3-5 MB is fine (2026-07-04). (Non-gating aside, resolved
2026-07-04: CsWinRT 3.0 -- a .NET 10, AOT-first rewrite with whole-app vtable dedup --
targets exactly this footprint but is preview-only and a breaking migration, so no
near-term "free" win; CsWinRT 2.2's opt-in size knobs (`GeneratedWinRTExposedType`,
`CsWinRTAotOptimizerEnabled`) are build-phase levers to try/measure now. Revisit at 3.0
RTM. No decision depends on it.)

How can we minimize the on-disk size of our tool? Managed code is easy to read,
but either ends up with very large binaries when merged into a 'single exe' (10s
or 100s of MB), or has many DLL dependencies in the folder which makes it
difficult to share. This is somewhat related to the distribution system, but
it's foundational and has knock-on implications. I want people to get it
quickly, and just rock it.

Operator direction (2026-07-02): the size-minimization *strategy* is decided by
INFRA-3 -- a NativeAOT, trimmed, single-file native exe (no runtime dependency).
What remains here is the concrete achievable floor and whether it is acceptable,
answered by the same early make-or-break spike as INFRA-3's gate ("how small can
a simple app get with non-extraordinary measures?"). Stays OPEN until the spike
produces a number and the operator judges it.
