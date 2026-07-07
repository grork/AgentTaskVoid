# DIST-8: The joined release-leg spike
**Status:** DECIDED
**Parent:** DIST-5
**Decision:** Do NOT run a dedicated spike -- accept the joined release leg as sound on
inspection. DIST-7's full-package manifest resolves the `0x80073CF9` sparse-vs-full wall,
which was the only *known* failure; the residual is a single accepted assumption -- that
the experimental `AppTaskInfo` honors an installed, signed-MSIX identity the same as the
loose-layout identity every prior spike used (a PFN is a PFN). Confirmation folds into the
first real packaged build rather than a standalone spike.

Recorded prediction (the "I told you so" paper trail): this is expected to work. If it does
NOT, the failure is NOT a distribution-vehicle defect -- there is nothing better to reopen
DIST-1 ("The end-user distribution vehicle") *to*. It would mean the experimental API does
not grant its activation context under installed-MSIX identity, a platform-level problem no
packaging choice fixes. So this supersedes the original "failure reopens DIST-1" framing
below: a failure here reopens the *premise*, not the vehicle.

With DIST-7's manifest in hand: AOT publish -> `winapp pack` -> dev-cert sign ->
install -> launch via the execution alias -> confirm it drives `AppTaskInfo`. The
make-or-break confirmation for DIST-1 ("The end-user distribution vehicle") -- the
INFRA-17 spike proved the halves separately but hit the sparse-vs-full wall
(`0x80073CF9`) on the joined leg. Failure reopens DIST-1.
