# INFRA-10: Testing behavior only observable in Shell rendering
**Status:** DECIDED
**Plan:** phase-05
**Decision:** The ERGO-10 validator ("Guarding unsupported state x content x mutator
combinations") is our own logic -- the platform API validates nothing -- so it's
covered by the STANDARD test suite, no special category and no real API. A data-driven
test exhaustively walks the state x content x mutator space and asserts the validator
accepts every documented-safe cell and refuses every unsafe one; it's also exercised in
situ when verb-path logic tests run against the fake. That validates the only thing
automation can see here: that our product never EMITS a combo we believe unsafe. It
does NOT validate that unsafe combos actually crash, or that the matrix is still
accurate -- both are opaque to automation (`Update()` succeeds, `tasks.json` looks
fine, only the out-of-process Shell shows it), so they're delegated to INFRA-13
("Windows build compatibility strategy") as periodic human re-verification (low-pri
"dark matter"). The fake plays no special role and must NOT model crash-on-bad-combo.
The matrix lives as data in code (one source; the doc references it).
**Parent:** INFRA-4

Some failure modes (explorer.exe crashes, blank cards -- see
`docs/windows-ui-shell-tasks/state-content-compatibility.md`) are invisible to
both the API and `tasks.json`: `Update()` succeeds and the data persists. How
do tests cover this class -- e.g. unit-testing a validation layer that encodes
the known compatibility matrix (ERGO-10)? And does anything verify the
empirical matrix itself stays true on new Windows builds?
