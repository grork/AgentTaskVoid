# Lifecycle

Questions about how task state is kept alive, expired, and reconciled across the
lifetime of a session or operation -- especially where the originating process
is not guaranteed to shut down cleanly.

## Questions

- [`LIFE-1`: Modeling a heartbeat / idle timeout for unclean session termination](./LIFE-1-modeling-a-heartbeat-idle-timeout-for-unclean-session-termination.md) -- EXPANDED
- [`LIFE-2`: Covering hook behavior across different agent hosts](./LIFE-2-covering-hook-behavior-across-different-agent-hosts.md) -- EXPANDED
- [`LIFE-3`: Last resort -- observing the wire transport for additional signal](./LIFE-3-last-resort-observing-the-wire-transport-for-additional-signal.md) -- DEFERRED
- [`LIFE-4`: Whether a persistent background watchdog process is required](./LIFE-4-whether-a-persistent-background-watchdog-process-is-required.md) -- DECIDED
- [`LIFE-5`: Heartbeat channel between CLI invocations and the watchdog](./LIFE-5-heartbeat-channel-between-cli-invocations-and-the-watchdog.md) -- DECIDED
- [`LIFE-6`: Watchdog lifecycle mechanics](./LIFE-6-watchdog-lifecycle-mechanics.md) -- EXPANDED
- [`LIFE-7`: Idle-expiry policy and what expiry does](./LIFE-7-idle-expiry-policy-and-what-expiry-does.md) -- EXPANDED
- [`LIFE-8`: Which agent hosts are in scope for hook coverage](./LIFE-8-which-agent-hosts-are-in-scope-for-hook-coverage.md) -- DECIDED
- [`LIFE-9`: Per-host hook surface inventory](./LIFE-9-per-host-hook-surface-inventory.md) -- EXPANDED
- [`LIFE-10`: The host-agnostic CLI abstraction hook events map onto](./LIFE-10-host-agnostic-cli-abstraction-hook-events-map-onto.md) -- DECIDED
- [`LIFE-11`: Whether we ship per-host integration artifacts](./LIFE-11-whether-we-ship-per-host-integration-artifacts.md) -- DECIDED
- [`LIFE-12`: Claude Code hook surface inventory](./LIFE-12-claude-code-hook-surface-inventory.md) -- DECIDED
- [`LIFE-13`: GitHub Copilot CLI hook surface inventory](./LIFE-13-github-copilot-cli-hook-surface-inventory.md) -- DECIDED
- [`LIFE-14`: Codex hook surface inventory](./LIFE-14-codex-hook-surface-inventory.md) -- DECIDED
- [`LIFE-15`: Handling tasks that have timed out, but get 'resurrected'](./LIFE-15-handling-tasks-that-have-timed-out-but-get-resurrected.md) -- DECIDED
- [`LIFE-16`: Watchdog granularity / scope](./LIFE-16-watchdog-granularity-scope.md) -- DECIDED
- [`LIFE-17`: Watchdog spawn mechanics](./LIFE-17-watchdog-spawn-mechanics.md) -- DECIDED
- [`LIFE-18`: Watchdog single-instance enforcement](./LIFE-18-watchdog-single-instance-enforcement.md) -- DECIDED
- [`LIFE-19`: Watchdog shutdown conditions](./LIFE-19-watchdog-shutdown-conditions.md) -- DECIDED
- [`LIFE-20`: Logoff/reboot recovery](./LIFE-20-logoff-reboot-recovery.md) -- DECIDED
- [`LIFE-21`: What expiry does](./LIFE-21-what-expiry-does.md) -- DECIDED
- [`LIFE-22`: Idle-period defaults per state, and configurability](./LIFE-22-idle-period-defaults-per-state-and-configurability.md) -- DECIDED
- [`LIFE-23`: Entryless-orphan reaping and the mass-deletion guard](./LIFE-23-entryless-orphan-reaping-and-the-mass-deletion-guard.md) -- DECIDED
- [`LIFE-24`: The host-event -> task-state integration semantics (the mapping layer)](./LIFE-24-host-event-to-task-state-integration-semantics.md) -- BLOCKED(ERGO-31, INFRA-23)
- [`LIFE-25`: Should host hooks invoke the `atv` exe directly instead of via a shell?](./LIFE-25-hooks-invoking-the-exe-directly-vs-via-a-shell.md) -- DECIDED
