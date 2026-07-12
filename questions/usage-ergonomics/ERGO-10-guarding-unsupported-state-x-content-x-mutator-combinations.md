# ERGO-10: Guarding unsupported state x content x mutator combinations
**Status:** DECIDED
**Plan:** phase-05
**Decision:** The CLI only ever emits combinations from the documented safe set,
and hard-rejects anything outside it (refuse + log, non-disruptively per FAIL-1)
-- crashing explorer.exe is unacceptable. The safe set is encoded as a validation
layer (unit-tested, INFRA-10). An `--unsafe` bypass may exist for experimentation
but is off by default.
**Parent:** ERGO-3

The Shell renders only a small documented subset of the combinations the API
accepts; some others crash explorer.exe (see
`docs/windows-ui-shell-tasks/state-content-compatibility.md`). Should the CLI
hard-reject unsupported combinations, warn and proceed, or offer an escape
hatch -- given the matrix is empirical and may shift across Windows builds?

Decision detail (2026-07-02): forced by banking the `attention` verb (ERGO-9) --
`SetQuestion` on the wrong shape/state is a real explorer.exe stack-overflow
crash, not just a blank card. The lean surface already stays inside the safe
cells; the validation layer makes that a guarantee, not a convention. Whether the
matrix still holds on a new Windows build is INFRA-13/INFRA-10, and this
validation layer is the single place that encodes it. Its implementation lives
behind the INFRA-8 seam so it is unit-testable.
