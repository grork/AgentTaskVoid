# DIST-4: Posture for the zero-pre-install script consumer
**Status:** DECIDED
**Decision:** No independent posture to decide -- fully subsumed by DIST-1 ("The end-user
distribution vehicle"), FAIL-1 ("Failure posture toward the host caller"), and FAIL-3
("Diagnosability when nothing shows on the taskbar"). Not-installed is OS command-not-found
(the caller's to guard); installed-but-no-identity is FAIL-1's no-op + exit 0 with FAIL-3's
`doctor` remedy; silent self-install is precluded by DIST-1's winget-only, consent-gated
vehicle. Nothing new here.

A consumer invoking `atv` from a plain script on a machine with nothing installed cannot
be served zero-install -- the OS gates identity behind consent (Dev Mode or a trusted
signed install), by design. Proposed posture (ratify): lean on FAIL-1's ("Failure posture
toward the host caller") non-disruptive default -- `atv` no-ops at exit 0 (the script
keeps working, minus taskbar cards) -- and `atv doctor` (FAIL-3, "Diagnosability when
nothing shows on the taskbar") prints the one-line remedy (`winget install ...`).
Explicitly reject building any silent self-install path.
