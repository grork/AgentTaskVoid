# LIFE-19: Watchdog shutdown conditions
**Status:** DECIDED
**Decision:** The watchdog exits when the supervised set is empty after a reconciliation poll
(every task removed, expired, or user-hidden), with a short anti-flap idle-grace so a quick
`start->done->remove` burst does not thrash spawn/exit; the Completed-linger (LIFE-22,
"Idle-period defaults per state") naturally holds it alive through the linger window. The
watchdog performs expiry's own `Remove()` under the shared INFRA-6 write mutex (per LIFE-4,
"Whether a persistent background watchdog process is required" -- it shares the mutex as
supervisor), never deferring to "the next CLI invocation" (there may be none). On exit it
releases the LIFE-18 mutex and disables the LIFE-20 ("Logoff/reboot recovery") boot-recovery
startup item.
**Parent:** LIFE-6

When does the watchdog exit: nothing left to supervise (all supervised tasks removed
or expired), user-hide observed (the LIFE-5 channel already carries `HiddenByUser`),
terminal-state tasks past their linger (LIFE-22)? And who performs expiry's final
`Remove()` -- the watchdog itself under the shared INFRA-6 mutex (LIFE-4 decided it
shares the mutex as a supervisor), or deferral to the next CLI invocation? Dev-loop
shutdown ergonomics (F5 / shift-F5, locked exes) are INFRA-18's, not repeated here.
