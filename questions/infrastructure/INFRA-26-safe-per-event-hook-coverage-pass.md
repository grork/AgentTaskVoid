# INFRA-26: Safe per-event hook coverage pass
**Status:** DECIDED
**Plan:** unplanned
**Parent:** INFRA-23
**Decision:** A per-host safe/care/skip matrix lives in `docs/host-events/<host>.md`,
classified by one axis — "does camping this event suppress or replace a default host
action?"; passive log-and-exit-0 is safe even on decision-capable events (it declines to
decide), and v1 **skips the replacement class** outright (can't observe it without
becoming it); an unsafe camp found mid-session is pulled from the config and its reason
recorded in the doc.

## Question
"Safe coverage ≠ blanket coverage." Registering a hook can itself change host behavior —
e.g. a Claude Code `WorktreeCreate` command hook REPLACES default git worktree creation
(the hook must print a path or creation fails outright). Camping on "as close to every
event as is safe" (INFRA-23's question) requires a per-event, per-host pass classifying
each candidate event as safe-to-camp / camp-with-care / skip-with-reason, done BEFORE any
capture session (INFRA-28) runs against a real host.

## Decision (operator + Claude Code, answer session, 2026-07-12)
1. **Where the matrix lives.** In the durable findings doc `docs/host-events/<host>.md` (a
   table), not inline hook-config comments — the classification *is* a finding, and the
   doc is the token-cheap reference a future session reads first. Config comments would
   duplicate and drift.
2. **The one classification axis: does camping this event suppress/replace a default host
   action?**
   - **Safe-to-camp** — observational / notification / decision-*optional* events. A
     passive hook that logs stdin and `exit 0` is safe **even on decision-capable events**
     (e.g. Claude Code `PreToolUse`): declining to emit a decision changes nothing. This
     covers the bulk of the vocabulary.
   - **Skip (v1) — the replacement class.** Events where the host's default is "the hook
     does the work" (the `WorktreeCreate`-style replacement). You cannot observe these
     without *reproducing the replaced work*, i.e. becoming the thing under test — out of
     scope for a pure observer. Skip with the reason recorded.
   - **Camp-with-care** collapses into the above for v1: any event that *would* need the
     recorder to do real work to stay safe is treated as skip, not carefully reproduced.
3. **Derivation, then confirmation.** The matrix is first derived from each host's own
   hook docs (which events are notification/observational vs decision/replacement), then
   confirmed empirically by the capture run itself.
4. **Mid-session unsafe camp.** Capture runs are supervised (INFRA-28) and the hook config
   is version-controlled: observe the misbehavior → pull/downgrade that event's row →
   record the observed reason in the findings doc. No heavier process — this is a
   diagnostic we run, not a shipped artifact.
5. **Relationship to INFRA-27.** The replacement class is exactly where "the hook must
   actually do the replaced work" would force a blocking posture. By skipping that class,
   the recorder stays a pure observer and INFRA-27's blocking budget never has to stretch
   to accommodate replacement work — only the teardown-race case remains.
