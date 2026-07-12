# INFRA-7: Explorer file-watcher behavior under rapid successive updates
**Status:** DECIDED
**Plan:** phase-04
**Decision:** Explorer keeps up. Empirically (this machine, 2026-07-02): rapid
single-task updates at ~20/sec and ~5/sec both rendered cleanly -- no crash, no
blank card, no freeze. It coalesces to the latest state (caught mid-burst it
showed the final value) AND re-renders LIVE while the flyout is open (the step
line climbs in real time as tasks.json changes). No write rate-limit/debounce is
required for correctness; a light debounce is optional only to cut disk churn if
a caller updates absurdly fast (well above realistic hook rates).
**Parent:** INFRA-1

explorer.exe watches the `tasks.json` folder and applies what it sees. Under a
burst of rapid updates (e.g. line-by-line status from a wrapped tool, ERGO-5),
does the taskbar coalesce, miss, or partially read updates? Does the CLI need
to rate-limit or debounce writes?

Evidence (2026-07-02): probe `hammer` (one task, N rapid Updates). Operator
observed the live flyout: coalesces to latest, and updates live while hovered.
Realistic update rates (~1-2/sec per tool call) are far below the tested 20/sec,
and our writes are already paced by the global write mutex (INFRA-6).
