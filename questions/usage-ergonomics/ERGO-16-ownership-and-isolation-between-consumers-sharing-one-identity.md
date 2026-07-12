# ERGO-16: Ownership and isolation between consumers sharing one identity
**Status:** DECIDED
**Plan:** phase-10
**Decision:** No ownership/consumer layer in v1. Everyday cleanup is targeted
`remove <handle>` (needs no ownership). `list` and `clear` are inherently
identity-global (one identity = one pool the API can't partition). `clear` was
gated (`--all` or a confirmation) so a stray script can't wipe a live agent's
task -- **but that gate was REMOVED 2026-07-05 (ERGO-27, "The consolidated v1 command
surface"): an explicitly-invoked `clear` executes immediately (operator: "they invoked
it, do it"); cross-consumer safety now rests on guidance (use targeted `remove`), not a
gate.** Clear scope (ratified 2026-07-05): `clear` purges all ACTIVE handles -- every
task plus its sidecar entry and icon copy -- but NOT the LIFE-21 ("What expiry does")
recycle bin; a `--include-recycle-bin` flag extends the purge to it. (LIFE-20's
("Logoff/reboot recovery") boot-recovery clear is the internal, non-interactive
exception -- it bypasses the gate and wipes the recycle bin per LIFE-21.)
Cross-consumer isolation, if ever wanted, returns with `--group` (the group
key doubling as the owner key) -- deferred exactly like glom (ERGO-14).

All consumers of the CLI share one package identity and thus one task
namespace (docs README: identity is a hard grouping boundary; there is one
`tasks.json` per identity). Today `clear` removes everything. Should
destructive or global verbs (clear, garbage collection per ERGO-2, list) be
scoped to the calling consumer's own tasks by default, and how would
ownership be tracked?

Decision detail (2026-07-02): "ownership" would have meant a consumer-identity
layer above individual tasks (stamp each task with which consumer made it, so
clear/list could filter to "just mine"). We deliberately did not build that layer
-- there is no consumer id above the per-session handle (group dropped, ERGO-14),
so "the caller's own tasks" is just the handles it holds, cleaned up with targeted
`remove`. No owner key to scope by, and no need for one.
