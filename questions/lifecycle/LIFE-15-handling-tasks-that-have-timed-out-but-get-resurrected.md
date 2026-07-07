# LIFE-15: Handling tasks that have timed out, but get 'resurrected'
**Status**: DECIDED
**Decision:** Resurrection = the LIFE-21 ("What expiry does") recycle-bin miss path. A
post-expiry update whose handle is gone from the live sidecar checks the recycle bin; found
within the ~1-day TTL -> re-create the card from the stored restore-record (title, subtitle,
icon, and deepLink recovered -- per the 2026-07-05 LIFE-21 record amendment), move the entry
back to live, apply the update -- no re-`start`, core info intact.
Past the TTL (or after reboot) the same update is a clean unknown-handle no-op (ERGO-8, "Update
verbs for ergonomic revision" -- general upsert-on-step stays deferred). The "how do we supply
the missing icon/title" problem this question raised is answered by the recycle bin capturing
them at expiry time.

Blocked note (ratified 2026-07-04): the answer's shape depends on LIFE-21 ("What
expiry does") -- two-stage visible death may reduce resurrection to a state
transition on a still-present card; silent `Remove()` means re-creation from nothing.
The answer must also reconcile with existing decisions: ERGO-21 ("The sidecar store
design") makes a late `step` a clean unknown-handle no-op, and ERGO-8 ("Update verbs
for ergonomic revision") deferred upsert-on-step out of v1 -- the missing-core-info
problem (icon, title) only exists if that reopens. LIFE-22 ("Idle-period defaults per
state") shrinks how often this fires but not the answer's shape.

We have a 'watchdog' process & sweep system that finds orphaned AppTaskInfo's
and our own specific sidecars. All good. However, there is a timeout associated
with these that has the potential to trigger while the 'next step' hasn't happened
e.g., hasn't reset the timer on the step. This leads to the possibility that when
the next step *does* happen, it's associated handle/sidecar will be cleaned up.

How should we handle this? How will we resurrect the AppTaskInfo? If we just say
it's ok to create it again, how would we supply the core info? e.g. icon, title
etc. Sure, we've got the next step, but there is other missing information.
