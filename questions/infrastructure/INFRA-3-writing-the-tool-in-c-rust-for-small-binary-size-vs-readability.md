# INFRA-3: Writing the tool in C++/Rust for small binary size vs. readability
**Status:** DECIDED
**Plan:** phase-01
**Decision:** Stay in C# and target NativeAOT, gated on an early spike whose
make-or-break criterion is the release-AOT on-disk binary size. Rust/C++ reopens
only if that gate fails.

Should we consider writing the entire tool in C++ or Rust to ensure the on-disk
size is very small? I *deeply* care about the readability, understandability,
and 'ease of working on' in the code, but depending on the full depth of
complexity of the code to support the scenarios we need, maybe the actual code
is narrow.

Decision detail (2026-07-02):
- C# keeps readability and the typed CsWinRT projection of this experimental
  namespace; a Rust/C++ rewrite would hand-roll WinRT/COM activation. Operator
  correction: CsWinRT has no separate "init" phase -- it is a thin wrapper over
  the WinRT (COM) calls, so there is no CsWinRT startup cost to remove; .NET's
  runtime bring-up + JIT is the startup cost, and NativeAOT removes the JIT and
  trims the runtime.
- NativeAOT gives a single native exe with no runtime dependency and small size,
  which is why it can dominate a native rewrite on nearly every axis at once.
- The gate is a spike, scheduled early, that answers: for this simple app, how
  small can a release-targeted, AOT'd, trimmed single-file binary get with
  non-extraordinary measures? The operator makes a make-or-break call on the
  resulting number. The spike must also confirm the risky combination actually
  builds and runs: NativeAOT + the experimental CsWinRT projection + sparse
  package identity/activation.
- This is the same spike that resolves INFRA-2 (on-disk size).
