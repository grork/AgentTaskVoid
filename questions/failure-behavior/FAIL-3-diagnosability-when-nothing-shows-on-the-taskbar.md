# FAIL-3: Diagnosability when nothing shows on the taskbar
**Status:** DECIDED
**Plan:** phase-06
**Decision:** Three diagnostics. (1) Durable log in the PACKAGE-RELATIVE app-data
location (`ApplicationData.Current.LocalFolder` -- leveraging package-identity
isolation, the same container as tasks.json and the ERGO-7 sidecar), NOT
a hand-rolled global LocalAppData path. Logs FAILURES by default; success not
logged by default (if ever enabled, very minimal, behind a verbose/debug toggle).
Entries {timestamp, verb, handle, error}; size/age-rotated. (2) `atv doctor`
self-check (IsSupported, identity registration, API availability). (3)
`--verbose` for live detail on stderr.

`Update()` can succeed while the Shell renders nothing (see
`docs/windows-ui-shell-tasks/state-content-compatibility.md`), the rollout can
leave the API absent, identity can be missing -- and under FAIL-1's likely
posture the caller won't see an error. What diagnostics do users get: a
doctor/self-check verb, a verbose mode, a log file?

Decision detail (2026-07-02): package-relative storage inherits the identity's
isolation for free and matches the file-as-interface debugging style. `doctor`
targets the single most likely support question given the gradual rollout --
"why is nothing on my taskbar?". The blank-render class (Update succeeds, Shell
shows nothing) is handled upstream by the ERGO-10 validation layer, not doctor.
Tuning value left to build-phase: exact log rotation size/age thresholds.
