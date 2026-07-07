# Infrastructure / Programming Paradigm

Questions about the runtime behaviour, implementation language, binary size, and
code-level architecture required to support the product's scenarios and testing
aims.

## Questions

- [`INFRA-1`: Concurrency / race conditions from brokering all in-proc API calls](./INFRA-1-concurrency-race-conditions-from-brokering-all-in-proc-api-calls.md) -- EXPANDED
- [`INFRA-2`: Minimizing the on-disk size of the tool](./INFRA-2-minimizing-the-on-disk-size-of-the-tool.md) -- DECIDED
- [`INFRA-3`: Writing the tool in C++/Rust for small binary size vs. readability](./INFRA-3-writing-the-tool-in-c-rust-for-small-binary-size-vs-readability.md) -- DECIDED
- [`INFRA-4`: Code-level testability architecture](./INFRA-4-code-level-testability-architecture.md) -- EXPANDED
- [`INFRA-5`: Empirical behavior of concurrent API calls from separate processes](./INFRA-5-empirical-behavior-of-concurrent-api-calls-from-separate-processes.md) -- DECIDED
- [`INFRA-6`: Whether CLI read-modify-write sequences need cross-process serialization](./INFRA-6-whether-cli-read-modify-write-sequences-need-cross-process-serialization.md) -- DECIDED
- [`INFRA-7`: Explorer file-watcher behavior under rapid successive updates](./INFRA-7-explorer-file-watcher-behavior-under-rapid-successive-updates.md) -- DECIDED
- [`INFRA-8`: The seam between CLI logic and the WinRT API for unit testing](./INFRA-8-seam-between-cli-logic-and-the-winrt-api-for-unit-testing.md) -- DECIDED
- [`INFRA-9`: Integration-test harness over tasks.json](./INFRA-9-integration-test-harness-over-tasks-json.md) -- DECIDED
- [`INFRA-10`: Testing behavior only observable in Shell rendering](./INFRA-10-testing-behavior-only-observable-in-shell-rendering.md) -- DECIDED
- [`INFRA-11`: Test strategy for machines where the API is unavailable](./INFRA-11-test-strategy-for-machines-where-the-api-is-unavailable.md) -- DECIDED
- [`INFRA-12`: Per-invocation / per-operation latency baseline & budget](./INFRA-12-per-invocation-per-operation-latency-baseline-budget.md) -- DEFERRED
- [`INFRA-13`: Windows build compatibility strategy for an experimental API](./INFRA-13-windows-build-compatibility-strategy-for-an-experimental-api.md) -- DECIDED
- [`INFRA-14`: Dev/test setup minimum](./INFRA-14-dev-test-setup-minimum.md) -- EXPANDED
- [`INFRA-15`: The bounded set of platform behaviors the fake must mimic](./INFRA-15-bounded-set-of-platform-behaviors-the-fake-must-mimic.md) -- DECIDED
- [`INFRA-16`: Test-time identity provisioning and deep isolation](./INFRA-16-test-time-identity-provisioning-and-deep-isolation.md) -- DECIDED
- [`INFRA-17`: Dogfood/run ergonomics without a load-bearing script](./INFRA-17-dogfood-run-ergonomics-without-a-load-bearing-script.md) -- DECIDED
- [`INFRA-18`: Handling 'Watchdog' background process during active development & inner loop](./INFRA-18-handling-watchdog-background-process-during-active-development-inner-loop.md) -- EXPANDED
- [`INFRA-19`: Inner-loop watchdog suppression](./INFRA-19-inner-loop-watchdog-suppression.md) -- DECIDED
- [`INFRA-20`: Reaping stale dev watchdogs / the locked-exe problem](./INFRA-20-reaping-stale-dev-watchdogs-the-locked-exe-problem.md) -- DECIDED
- [`INFRA-21`: Debugging watchdog mode itself](./INFRA-21-debugging-watchdog-mode-itself.md) -- DECIDED
- [`INFRA-22`: GUI-subsystem exe + AttachConsole for flash-free OS-launched instances](./INFRA-22-gui-subsystem-exe-attachconsole-for-flash-free-os-launched-instances.md) -- DEFERRED
