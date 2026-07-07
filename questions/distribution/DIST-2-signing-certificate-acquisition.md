# DIST-2: Signing / certificate acquisition
**Status:** DEFERRED
**Deferred:** Ratified 2026-07-04 -- ship-time operations. The direction is decided
(signed full MSIX via winget, DIST-1); which certificate signs it is a ship-time
acquisition task the body below already recorded as deferred -- the status now matches.
Revisit at release prep; confirm Azure Trusted Signing individual-developer
eligibility then.

What signs the release MSIX? Candidates: Azure Trusted Signing (chains to a
Windows-trusted Microsoft root; cheap; CI-integrable -- but confirm current
individual-developer eligibility), a standard OV code-signing cert, or Store-signing.
Dev uses a throwaway self-signed cert (`winapp cert generate`/`install`); a self-signed
cert is explicitly REJECTED as a distribution mechanism (per-machine admin trust install
is a worse ask than Dev Mode). Deferred to ship time; recorded so it isn't lost.
