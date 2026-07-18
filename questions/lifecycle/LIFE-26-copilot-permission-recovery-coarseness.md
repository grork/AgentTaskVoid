# LIFE-26: Copilot CLI permission recovery — what clears a Blocked card after approval?

**Status:** DECIDED (2026-07-18 — records the decision made in the Copilot
integration build, previously written down only as README prose)
**Decision:** Accept the stale-after-approval window. The parent card stays
Blocked after a permission prompt until later parent activity or the turn's
final Ready, because Copilot exposes `permission_prompt` only on the parent
session and publishes no permission-approved/completed hook — there is no
event to clear the block on. Rejected: hooking every `postToolUse` (clears
only at tool completion — an hour-long build stays Blocked the whole hour —
and adds a synchronous hook process to every tool call); timers (guessing);
reading Copilot's internal transcript (private, unsupported); dropping
permission attention entirely (loses the accurate signal). Keeping the
accurate attention signal and accepting the window is the least-bad trade.

## Question

When Copilot prompts for tool permission, the plugin marks the parent card
Blocked. Copilot emits nothing when the human approves. What clears the
card, and how stale may it be?

## Where the behavior is recorded

- `docs/host-events/copilot-cli.md` — the observed hook evidence ("One live
  limitation remains").
- `integrations/copilot-cli/README.md` — Known limitations, the
  user-visible effect.
