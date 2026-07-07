# LIFE-21: What expiry does
**Status:** DECIDED
**Decision:** Expiry `Remove()`s the card (it vanishes -- no visible tombstone) AND moves a
minimal restore-record into a cold "recycle-bin" folder in package app-data. The recycle bin
is NEVER enumerated on the hot path; it is consulted ONLY on the miss path -- an update whose
handle is absent from the live sidecar. Resurrection: on that miss, if the handle is in the
recycle bin (within the ~1-day TTL), re-create the card from the record, move it back to live,
apply the update -- core info intact. Record = {handle, title, subtitle, icon-ref, deepLink,
whenTombstoned} [AMENDED 2026-07-05 review pass: subtitle + deepLink added to the record --
both are readable off the API card at expiry exactly like title/icon, and without them
resurrection silently reverted a caller-supplied subtitle/deepLink to blank/default],
captured by reading them off the API card at expiry time (ERGO-21's "no cached
content" preserved -- the live sidecar is unchanged); nothing mutable is stored (steps/state
restart fresh). Purge = opportunistic scavenge folded into existing sweeps drops records older
than ~1 day (build-phase tunable via ERGO-17, "Configuration surface for recurring defaults"),
and reboot/login-clear (LIFE-20, "Logoff/reboot recovery") wipes it. This is a SCOPED upsert
(only previously-live, tombstoned handles resurrect) -- ERGO-8's ("Update verbs for ergonomic
revision") general upsert-on-step stays deferred; the recycle bin just supplies the icon/title
that deferral lacked, so a never-seen handle is still a clean no-op. Net: clean taskbar (silent
removal) + off-hot-path resurrection for up to a day; the residual race relocates to the ~1-day
purge boundary (silent >1 day then resuming) -- negligible and graceful. The expiry pass obeys
the requirements.md freshness/ordering invariant (run after active work; re-read `lastUpdate`
under the INFRA-6 mutex). Complements LIFE-22 ("Idle-period defaults per state"): resurrection
is event-driven, so NeedsAttention still needs its long visible-idle period (nothing
resurrects it).
**Parent:** LIFE-7

On idle expiry, does the watchdog silently `Remove()` the task, or two-stage it:
transition to a visible "session died" state (Error, or Paused + summary), linger,
then remove -- so the user sees the death rather than the card vanishing? Two-stage
interacts with LIFE-15 ("Handling tasks that have timed out, but get 'resurrected'"):
a wrong-guess expiry followed by the session's next `step`. The
long-thinking-vs-session-gone ambiguity that makes wrong guesses possible is
LIFE-22's per-state problem. Whatever transition is chosen must stay inside the
ERGO-10 ("Guarding unsupported state x content x mutator combinations") safe set.
