# The Agentaskvoid v2 integration API

This is the normative, host-agnostic contract for driving `atv` (brand
**Agentaskvoid**) from an agent host's own event stream. It is the surface a
translator author — first-party or third-party — targets from this document
alone, without needing to read `atv`'s source. It supersedes the v1 lifecycle
surface (`start`/`step`/`state`/`attention`/`done`/`fail`), which is retired.

If you are building a new host integration, read this document top to
bottom once, then use it as reference. The per-host translator artifacts
(conduit config + translator script + extraction table) live under
`integrations/<host>/`; this document does not describe any one host's event
names — only what `atv` itself accepts and guarantees.

**Status note (2026-07-13):** this document describes the ERGO-31/LIFE-24
v2 semantic engine as built in plan phase 15, split into two parts. **Phase
15A** (this build) ships the full verb surface, the five-state model, claim
semantics, projection legality, and the stdin/normalizer contract described
below — all of it is load-bearing and stable. **Phase 15B** (not yet built)
adds two things flagged explicitly wherever they appear: the Ready→Idle
presence-gated decay clock (§6), and multi-card fan-out addressing (§5). Until
15B ships, `agent-started`/`agent-stopped` are accepted and do bookkeeping
only — no child card is minted — and a card that reaches Ready simply stays
Ready (no automatic demotion to Idle). Everything else in this document is
final.

---

## 1. The model: five states, ranked by cost of ignoring

A card exists to answer two questions at a glance: **"what happens if I
don't switch back?"** (alerting) and **"what is happening?"** (observability).
States model the user's decision, not the host's internal event machine.

| State | Claim it makes | Decays? | AppTaskInfo projection |
|---|---|---|---|
| **Blocked** | Mid-work, stalled on your decision (permission / question / form) | Never | `NeedsAttention` + a question |
| **Broken** | Turn/session died without delivering (API error, fatal failure) | Never | `Error` |
| **Ready** | Turn finished; fresh output awaits your review | Presence-time → Idle (15B) | `Completed` |
| **Working** | Progressing; needs nothing from you | — | `Running` + a step line |
| **Idle** | Open; nothing owed either way | — | `Paused` |

Ranked by urgency: Blocked > Broken > Ready > Working > Idle. This is also
the observed taskbar group-badge priority (Error > NeedsAttention > Completed
> Paused > Running), so Blocked visually dominates every non-urgent sibling
card sharing its icon group.

**Idle has no verb.** It is reachable only via the engine's own decay clock
(15B) — no host event claims it directly. If you find yourself wanting to
call something "idle," you almost certainly want `ready` instead (the turn
finished; let the engine decide when it has gone stale).

## 2. The eight verbs

Every verb takes a **required positional `<handle>`** — a caller-chosen
opaque string identifying one card. There is no default handle.

Every verb **except `session-ended`** additionally accepts the identity
flags `--title <text>` / `--subtitle <text>` / `--icon <token>` /
`--deep-link <uri>`, and **upserts** the card: the very first semantic verb
call for a handle creates it. There is no separate "start" verb. A stateless
translator should pass the identity flags on **every** call — re-supplying
the same values is idempotent and cheap.

| Verb | Free-text flag (`-` stdin eligible) | Other flags | Lands in | Notes |
|---|---|---|---|---|
| `working <h>` | `--goal -` | — | **Working** | Sets the turn's goal (altitude 2). Absent `--goal` makes no content claim. |
| `activity <h>` | `--label -` | `--kind <k>` (required), `--agent <id>`, `--name <n>` | **Working** | The current activity line (altitude 3). Against a Blocked card: drops the question and re-enters Working, unless another locus is still pending (§5.1). |
| `blocked <h>` | `--question -` (required) | `--agent <id>` | **Blocked** | Platform-enforced: `NeedsAttention` requires a question. |
| `ready <h>` | `--summary -` | — | **Ready** | Bare preserves the current step content; `--summary` swaps to a final summary. Clears every pending Blocked locus (turn-end). |
| `broken <h>` | `--detail -` | `--reason <token>` (required) | **Broken** | Always renders as a final summary of the reason word (+ optional detail). Clears every pending Blocked locus (turn-end). |
| `agent-started <h>` | — | `--agent <id>`, `--name <n>` | *(no transition)* | Registers a child locus. 15A: bookkeeping only, no child card yet (§5). |
| `agent-stopped <h>` | — | `--agent <id>` | *(no transition, unless it clears a pending block)* | Retires a child locus (fan-in). If that locus was blocking, the card re-projects (§5.1). |
| `session-ended <h>` | — | `--reason <token>` (required) | `finished` → card removed · `error` → **Broken** | The one verb with **no** identity flags and **no** upsert — it only acts on an already-live handle. |

### Idempotency

- **An absent optional flag makes no claim.** `working <h>` with no `--goal`
  still lands the card in Working (clearing a pending block, if any) but
  leaves the current step content exactly as it was.
- **Re-asserting an already-held state restarts nothing.** Calling `ready`
  twice in a row is a harmless no-op re-apply, not a reset.
- Duplicate "done" signals from a host (e.g. both an explicit stop event and
  a separate idle-notification event) are expected and harmless — map both
  to `ready`.

## 3. The closed kind vocabulary (`activity --kind`)

Kinds name the **mechanism**, never the purpose — the label carries the
subject, and the engine owns the rendered wording. The list is closed and
deliberately small: an unmapped tool falls back to `tool`, so the vocabulary
never blocks integrating a new tool.

| kind | rendered word | typical source |
|---|---|---|
| `read` | Reading | file read |
| `edit` | Editing | file edit |
| `write` | Writing | file write |
| `search` | Searching | content or filename search |
| `shell` | Running | a shell/process command |
| `fetch` | Fetching | a URL fetch |
| `web-search` | Searching the web | a web search |
| `plan` | *(renders `--label` as-is)* | a task/plan list update |
| `compacting` | *(fixed: "Compacting conversation")* | context compaction |
| `tool` | *(fallback: prettified `--name` + `--label`)* | any unmapped tool |

Notes:

- `search` covers both content search and filename glob — the label
  distinguishes them; there is no separate `find` kind.
- There is no `delegate` kind. Subagent spawn/retire is `agent-started`/
  `agent-stopped`, never rendered as an activity line.
- **`plan`**: `AppTaskInfo` has no numeric progress field — a host plan/todo
  list update is just a string in the same step stream every other activity
  uses. If you want to render "(3/7) Write tests," compose that full string
  yourself and pass it as `--label`; the engine does not add counting.
- **`tool` fallback**: pass the raw tool identifier via `--name` (e.g.
  `mcp__jira__create_ticket`) and the subject via `--label`. The engine
  prettifies the MCP `mcp__<server>__<tool>` naming convention into
  `Server: Tool`; any other name is prettified as a single token
  (underscores/hyphens → spaces, each word capitalized).

## 4. The closed reason vocabularies

**`broken --reason <token>`** ∈ `{ rate-limit, overloaded, api-error,
timeout, fatal }`:

| token | rendered |
|---|---|
| `rate-limit` | Rate limited |
| `overloaded` | Overloaded |
| `api-error` | API error |
| `timeout` | Timed out |
| `fatal` | Failed |

`fatal` is the catch-all for anything that doesn't fit the other four. An
optional `--detail -` (free text — the host's real error message) is
appended after the rendered reason word, e.g. `"API error: connection reset
by peer"`.

**`session-ended --reason <token>`** ∈ `{ finished, error }` — token only,
no free-text detail:

- `finished` → the card is removed.
- `error` → the card surfaces as Broken (a fixed phrase; the host's own
  watchdog/reap process is expected to eventually clean it up).

Both vocabularies are closed by design. Map your host's own richer error
taxonomy onto the nearest of these five/two tokens in your translator.

## 5. Fan-out addressing — 15B, not yet built

**This section describes the target design; it is not implemented by 15A.**
`agent-started <h> --agent <id> --name <n>` and `agent-stopped <h> --agent
<id>` exist today and are safe to call — they register/retire bookkeeping —
but no child card is minted yet, no `<session>#<agent_id>` handle exists, and
`remove`/`session-ended` do not cascade to anything (there is nothing to
cascade to).

The target design once 15B ships: the engine mints a child card at the
**2nd concurrent** `agent-started` for a session (retroactively carding the
1st worker too — fan-out is only recognizable once a second worker appears).
The child handle is `<session-handle>#<agent_id>`, addressable by `list`/
`remove` like any other handle. A child card is scaffolding only — Working/
Completed, never Blocked (a question always belongs to the session card,
which has the one input point). Children retire at fan-in
(`agent-stopped`); `remove <parent>` and `session-ended <parent>` cascade to
every still-live child.

A **name-only** host (an `agent-started` that carries `--name` but no
`--agent` — i.e. the host can't tell concurrent same-name workers apart) mints
no child card even after 15B ships: the subagent instead surfaces as a line
on the parent card's own activity stream.

### 5.1 Blocked and same-locus clearing (built now, in 15A)

`blocked --agent <id>` records which **locus** raised the question: a
specific agent id, or the parent/main-thread locus if `--agent` is absent.
The block clears on the next event **attributed to that same locus**:

- that locus's own next `activity` call,
- that locus's own `agent-stopped`, or
- any turn-end event (`ready`/`broken` — these are never `--agent`-scoped,
  so they always clear every pending locus at once).

This means a subagent's own permission prompt does not get silently
dismissed by unrelated main-thread activity, and — the flip side — a
main-thread prompt is not dismissed by unrelated subagent chatter.

**Concurrent blocks**: if two loci are blocked at once, the card displays
the most recently raised question. When that locus's block clears, the
OTHER pending locus's question is surfaced instead (the card stays Blocked);
only once every locus has cleared does the card return to Working.

**Degraded fallback**: a host that cannot resolve fan-out attribution at all
simply never sends `--agent` on either `blocked` or `activity` — both then
land on the same parent locus by construction, so "any activity clears the
block" falls out of the same-locus rule automatically. No special case is
needed on the host side.

## 6. Clocks — 15B, not yet built

**This section describes the target design; it is not implemented by 15A.**
Today, a card that reaches Ready simply stays Ready until some other verb
moves it. Once 15B ships:

- **Only Ready decays**, and only while the user *had a chance* to act
  (device unlocked AND recent input) — a courtesy demotion to Idle
  (`Paused`), never an inbox-zero sweep.
- Blocked and Broken never decay — surviving the user's absence is their
  whole purpose.
- A transition **INTO** Ready starts the clock; re-asserting Ready while
  already Ready never restarts it.
- This UX decay clock is entirely separate from `atv`'s own hygiene reap
  (an orphaned card whose owning process died without a `session-ended`
  event, cleaned up on wall-clock staleness by the watchdog) — the two are
  never conflated, and a translator does not need to know the hygiene reap
  exists at all.

## 7. Free text: the `-` stdin convention

At most **one** free-text value travels per call, through a flag whose
value is exactly the single character `-`:

```
echo "Fix the login bug across all pages" | atv working my-handle --goal -
```

- Read **UTF-8**, to **EOF**, with **trailing whitespace trimmed**.
- Short, host-constrained tokens (handles, kind/reason tokens, agent ids,
  `--title`/`--subtitle`/`--icon` identity values) ride ordinary argv —
  they are translator-chosen constants, never arbitrary text, so there is no
  quoting hazard.
- A flag value that is anything other than the literal `-` is used as a
  literal string directly (this is a convenience atv itself offers on top of
  the stdin convention; a translator targeting maximum portability across
  shells should still prefer `-` for anything that might contain quotes,
  newlines, or non-ASCII text — argv quoting under some Windows shells is
  unreliable for exactly that content).
- Never re-serialize a payload fragment as JSON to pass it through — read
  the raw text, hand it to `-`, done.

The free-text-eligible flags are: `working --goal`, `activity --label`,
`blocked --question`, `ready --summary`, `broken --detail`.

## 8. The shared normalizer

Every single-line rendering (goal, question, summary, activity label) is
normalized once, identically, by the engine before display:

1. Collapse whitespace runs (including embedded newlines) into a single
   space — a multi-line prompt becomes one line.
2. Strip light markdown decoration: `**bold**` → `bold`, `` `code` `` →
   `code`, a leading `#`-`######` header marker is dropped.
3. Truncate to a field-specific budget, appending an ellipsis (`…`) if
   truncated. Budgets differ per field (a question/summary gets more room
   than a terse activity label) but the algorithm is identical everywhere.

A translator never needs to do any of this itself — hand over the raw text
verbatim (after only the stdin-vs-argv choice in §7) and let the engine do
the rest.

## 9. Projection legality

The engine guarantees, structurally, that it never emits a (state, content)
pair the Shell can't safely render — the empirically-verified safe
combination matrix is internal (`SafeCombinationMatrix.cs`) and a translator
does not need to know its cells. Concretely this means: you can call
`activity` directly against a card that is currently Blocked, with no
intermediate "clear the state first" call — the engine drops the question
and re-enters Working (or, if another locus is still blocked, re-projects
Blocked with that locus's question) as part of `activity`'s own claim. There
is no v1-era "state running before every step" chain to reproduce.

## 10. Failure posture

`atv` never disrupts its host caller by default. Every verb call:

- **Exits 0** on any failure (bad handle, platform unavailable, refused
  combination, unknown flag value) unless the global `--strict` flag is
  passed.
- Writes exactly one entry to `atv`'s own durable failure log on any
  failure, regardless of `--strict`.
- Under `--strict`, maps failures onto a small stable exit-code vocabulary
  (generic / API unavailable / identity not registered / invalid
  arguments) instead of 0.

A hook/translator author should treat `atv` as something that can never
break the host session: do not gate your own control flow on `atv`'s exit
code unless you deliberately opted into `--strict` and specifically want
that.

## 11. Data / utility surface (unchanged from v1)

`list`, `run`, `clear`, `doctor`, `remove`, and the hidden `watchdog` mode
are untouched by this document — see `atv --help` and `docs/configuration.md`.
`remove <handle>` still exists for manual removal and (once 15B ships) fan-out
child-card addressing/cascade.
