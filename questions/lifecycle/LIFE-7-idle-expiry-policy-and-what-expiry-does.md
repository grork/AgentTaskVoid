# LIFE-7: Idle-expiry policy and what expiry does
**Status:** EXPANDED
**Expanded into:** LIFE-21, LIFE-22, LIFE-23
**Parent:** LIFE-1

What is the default idle period, is it configurable per task, and what happens
on expiry: `Remove()` the task, or transition it to a visible terminal state
(Error/Paused) so the user can see the session died? Silence alone cannot
distinguish 'long thinking' from 'session gone' -- what does that imply for the
default?

Expansion seeds (operator, 2026-07-02) -- LIFE-4 decided the timer is
relative-expiry-based; the policy grew too complex to answer in one shot:
- Relative expiry: the idle period is measured from the last update -- explicitly not
  "expire a fixed period after creation".
- Default idle period value, and whether it is configurable per task.
- `Completed` is special: it may warrant an *extended* timeout before removal,
  and/or a "linger, then clean-up-and-replace-later" pattern rather than prompt
  removal (a finished task stays visible a while, then is removed or superseded).
- What expiry does: `Remove()` vs. transition to a visible terminal state so the
  user can see the session died.
- Distinguishing 'long thinking' from 'session gone' from silence alone, and what
  that implies for the default.
- Entryless-orphan reaping (handed off from ERGO-21, "The sidecar store design"):
  the sidecar is an INDEX, so invocation-time reconciliation leaves live API tasks
  that have NO sidecar entry (the rare "create landed, sidecar write crashed" orphan,
  dev `probe` runs, pre-sidecar leftovers) untouched -- they can only be reaped here.
  Decide whether the watchdog reaps entryless tasks at all, and if so it MUST carry a
  mass-deletion guard: an entryless task is indistinguishable from a live task whose
  entry was lost, so a wiped/corrupt sidecar (while tasks.json survives) would
  otherwise nuke every live card in one sweep. Guard e.g. refuse to reap when the
  sidecar dir is absent/empty or the unknown-fraction is suspiciously high; log
  instead. (Common turds -- unclean session death -- keep their entry and are reaped
  safely by the idle-expiry path above; only entryless tasks need this.)
