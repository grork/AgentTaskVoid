# LIFE-4: Whether a persistent background watchdog process is required
**Status:** DECIDED
**Decision:** Yes -- an idle-expiry watchdog is required. Opportunistic sweep alone
cannot catch a session that posts a task and dies before any further invocation
runs to sweep it. Not a fixed timer. Design decomposes into LIFE-5 (channel),
LIFE-6 (lifecycle), LIFE-7 (policy).
**Parent:** LIFE-1

Can idle-timeout handling be done opportunistically (each CLI invocation sweeps
stale tasks), or is a persistent background process required? The failure mode
that decides this: after the last-ever invocation of a dead session, nothing
runs again to clean up its task unless something persists.

Operator clarification (2026-07-02): the brief's 'mostly stateless' describes
the API, not a constraint on this tool -- a persistent watchdog is not ruled
out on principle.

Decision detail (2026-07-02): Opportunistic-sweep-only is insufficient by
construction -- the deciding failure mode above has no sweeper left to run. The
watchdog is idle-expiry-based: expiry is measured relative to the last update, so it
is explicitly not "expire X after creation". Deterministic user-hidden cleanup
is a separate, cheaper concern handled without the watchdog (ERGO-2). The
watchdog's channel, lifecycle, and expiry policy are LIFE-5 / LIFE-6 / LIFE-7.

Supporting evidence (Sonnet host research, 2026-07-02): none of Claude Code,
Copilot CLI, or Codex emits a session-end signal reliable on unclean exit
(Ctrl+C / kill / closed terminal) -- Copilot's `sessionEnd` is best-effort,
Claude Code's fires on clean exit only, Codex has none (LIFE-12/13/14). This
confirms the watchdog + staleness timeout is necessary, not merely chosen: we
cannot architect cleanup around an end event. The liveness heartbeat rides the
pre/post-tool hooks, which DO fire reliably on all three.
