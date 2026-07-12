# FAIL-2: Output and exit-code contract
**Status:** DECIDED
**Plan:** phase-06
**Decision:** stdout = data, stderr = diagnostics. Mutating verbs print nothing on
the happy path (no id returned; caller owns the handle, ERGO-6); `list` prints
human-readable, `--json` for machines. Default mode always exits 0 (FAIL-1);
`--strict` uses a stable exit vocabulary: 0 ok, 2 API unavailable, 3 identity not
registered, 4 invalid args / unsafe combo (ERGO-10), 1 generic. `--json` on a
mutating verb returns `{"ok":...,"reason":...}` so a script can learn the truth
without leaving non-disruptive mode.

What do scripts get to rely on: stdout/stderr discipline, machine-readable
output (JSON?) for verbs that return data (create's ID per ERGO-6, list,
received responses per INTER-2), and a stable exit-code vocabulary. Interacts
with FAIL-1: a non-disruptive default still needs a way to signal 'nothing
actually happened'.

Decision detail (2026-07-02): the "did anything happen?" gap FAIL-1 leaves is
closed two ways -- `--strict` (exit codes) for interactive/CI callers, and
`--json` (structured stdout, exit stays 0) for scripts that want to know but must
not disrupt the host.
