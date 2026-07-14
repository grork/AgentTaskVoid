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
v2 semantic engine as built in plan phase 15 (split into 15A and 15B for the
build itself — that split is not part of the contract). The full verb
surface, the five-state model, claim semantics, projection legality, the
stdin/normalizer contract, the Ready→Idle presence-gated decay clock (§6),
and multi-card fan-out addressing (§5) are ALL load-bearing and stable as of
this build.

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
`--icon-file <path>` / `--deep-link <uri>`, and **upserts** the card: the
very first semantic verb call for a handle creates it. There is no separate
"start" verb. A stateless translator should pass the identity flags on
**every** call — re-supplying the same values is idempotent and cheap.
`--icon` and `--icon-file` are mutually exclusive — supplying both on one
call is a usage error (see "Icons" below).

| Verb | Free-text flag (`-` stdin eligible) | Other flags | Lands in | Notes |
|---|---|---|---|---|
| `working <h>` | `--goal -` | — | **Working** | Sets the turn's goal (altitude 2). Absent `--goal` makes no content claim. |
| `activity <h>` | `--label -` | `--kind <k>` (required), `--agent <id>`, `--name <n>` | **Working** | The current activity line (altitude 3). Against a Blocked card: drops the question and re-enters Working, unless another locus is still pending (§5.1). |
| `blocked <h>` | `--question -` (required) | `--agent <id>` | **Blocked** | Platform-enforced: `NeedsAttention` requires a question. |
| `ready <h>` | `--summary -` | — | **Ready** | Bare preserves the current step content; `--summary` swaps to a final summary. Clears every pending Blocked locus (turn-end). |
| `broken <h>` | `--detail -` | `--reason <token>` (required) | **Broken** | Always renders as a final summary of the reason word (+ optional detail). Clears every pending Blocked locus (turn-end). |
| `agent-started <h>` | — | `--agent <id>`, `--name <n>` | *(no transition)* | Registers a child locus; mints a real child card at the 2nd concurrent registration (§5). |
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

### Icons

**`--icon <token>`** (ERGO-20): a curated Segoe Fluent Icons name (e.g.
`Robot`, `Bug`), a single literal emoji character, or a raw file path (see
`--icon-file` below — any value that's neither a curated name nor a single
character is treated as a path). Absent `--icon`, the default is the Robot
glyph.

**Theme-neutral tile (ERGO-28, phase 16):** every monochrome Segoe glyph —
including the default Robot — renders as a **white glyph on a fixed
accent-color rounded-rect tile** (`#0078D4`), not a bare glyph. This fixes
solid-black-on-a-dark-taskbar invisibility without ever inspecting the
system theme: one static asset, no runtime re-render (icon immutability,
ERGO-25, and the URI grouping key, ERGO-13, both rule that out). Color emoji
render **bare** (already full-color, theme-safe art) — they are never
composited onto the tile.

**`--icon-file <path>` (ERGO-29, phase 16):** bring your own image — PNG,
JPG, or ICO. `atv` reads the file, validates it (byte-size cap, a
magic-number format allowlist — not the file extension — and WIC decode),
and normalizes it to the pipeline's 64px PNG: downscaled if oversized,
aspect-ratio-preserving **transparent** letterbox padding if non-square (the
supplied mark is never placed on the accent tile — it stays bare/full-bleed,
since a caller's own logo can't be recolored for guaranteed contrast). A
rejected file (too large, wrong format, corrupt data) falls back down the
same chain as an unavailable `--icon` token (default glyph → drawn shape),
logged, never a hard failure. **`--icon` and `--icon-file` are mutually
exclusive** — supplying both is a usage error.

The resulting per-handle icon file is cached and moved through
remove/expire/resurrect/purge exactly like a rendered glyph (ERGO-23) — same
lifecycle, regardless of source. Icons are immutable per card (set once, at
first upsert) either way (ERGO-25); grouping is keyed on the exact icon URI
string (ERGO-13), so two callers supplying the "same" logo via different
paths do not glom into one taskbar group.

**Known v1 caveat:** the fixed accent tile is not verified against Windows
high-contrast themes — it targets ordinary light/dark taskbar theming only.

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

## 5. Fan-out addressing

`agent-started <h> --agent <id> [--name <n>]` registers a child locus.
Registering alone does nothing visible — the engine mints a REAL child card
only at the **2nd concurrent** `agent-started` for a session (retroactively
carding the 1st worker too, in the SAME call that crosses the threshold —
fan-out is only recognizable once a second worker appears, so there is
nothing to card before then). A 3rd, 4th, … concurrent worker each mint their
own card the moment they register, once fan-out is already established for
that session.

The child handle is exactly `<session-handle>#<agent_id>` — deterministic, so
a translator (or a human) can always compute it without asking `atv`
anything. It is addressable by `list`/`remove` and every other semantic verb
like any other handle: once minted, a subagent's own further activity should
target the CHILD handle directly (e.g. `atv activity <session>#<agent_id>
--kind read --label file.ts`), not the parent.

A child card is scaffolding: it starts Working (a bare step baseline) and can
reach Ready via its own `ready` call — and that is the ENTIRE reachable
range. "Working/Completed only" is exhaustive, not merely "never Blocked":
**`blocked`, `broken`, and `session-ended --reason error` are all refused
against a child handle**, because each would land the child in a third state
(NeedsAttention, Error) beyond the two sanctioned ones (`--strict`'s
`InvalidArguments` code, non-strict just a logged no-op, always a true
no-op — the child's card is left exactly as it was). Route a subagent's own
permission/question prompt, failure, or session-error to the PARENT handle
instead — e.g. `blocked <session> --agent <id> --question -`, which uses the
same-locus attribution machinery in §5.1 rather than the child handle at all;
`broken <session> --reason ...` for a subagent-caused failure; `session-ended
<session> --reason error` to end the whole session (which itself cascades to
every live child, below). `session-ended <child> --reason finished` (i.e.
plain removal) is UNAFFECTED by this refusal — removing a child outright
never leaves it in a new state, so it behaves exactly like `remove
<child-handle>`.

**Retirement:** a child retires (its card is removed) at its OWN
`agent-stopped <h> --agent <id>` — never merely because concurrency later
drops back below 2. **Cascade:** `remove <parent-handle>` and
`session-ended <parent-handle>` (either `--reason`) both remove every
still-live child along with the parent — a session that's over takes its
fanned-out workers with it. `remove <child-handle>` targets exactly that one
child; it does not affect the parent or any sibling.

A **name-only** host (an `agent-started` that carries `--name` but no
`--agent` — i.e. the host can't tell concurrent same-name workers apart)
mints no child card at all, ever: there is no locus id to address a card by,
so the subagent instead surfaces as a line on the parent card's own activity
stream (a separate `activity <session> --kind tool --name <n> --label ...`
call is the translator's job — `agent-started` alone never renders anything).

**The child's icon is the parent's own resolved `--icon` value for that same
call, reused byte-for-byte** — never re-rendered or re-placed on a per-child
path. This is deliberate: Agentaskvoid's taskbar grouping is keyed on the
exact icon URI string (empirically verified, ERGO-13), so every card sharing
one icon URI groups into one taskbar cluster. A child minted with its own,
different icon path would silently secede from the parent's group.

### 5.1 Blocked and same-locus clearing

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

## 6. Clocks

- **Only Ready decays**, and only while the user *had a chance* to act —
  device unlocked AND recent input, sampled by `atv`'s own background
  watchdog process, never something a translator measures or reports itself.
  A card that decays lands in Idle (`Paused`) — a courtesy demotion to
  reduce visual noise, never an inbox-zero deletion; the card is still there,
  still addressable, still `list`-able.
- Blocked and Broken never decay — surviving the user's absence is their
  whole purpose.
- A transition **INTO** Ready starts the clock fresh; re-asserting Ready
  while already Ready (e.g. a duplicate `ready` call) never restarts it —
  the same idempotency rule §2 already describes, extended to the clock.
  Leaving Ready for any other state (another `working`/`activity`/`blocked`/
  `broken` claim) clears the clock; a later return to Ready starts a brand
  new one.
- The exact decay threshold and "recent input" window are `atv`-internal
  tuning, not part of this contract — a translator never configures, queries,
  or needs to reason about either.
- This UX decay clock is entirely separate from `atv`'s own hygiene reap
  (an orphaned card whose owning process died without a `session-ended`
  event, cleaned up on wall-clock staleness by the watchdog) — the two run
  as independent passes and are never conflated. In practice the hygiene
  reap is the more aggressive of the two for a truly abandoned card (no
  further writes of ANY kind, ever), while the decay clock is the one that
  fires for a card the user is actively near but simply hasn't looked at —
  a translator does not need to know the hygiene reap exists at all, or
  reason about which of the two will act first on any given card.

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
`remove <handle>` still exists for manual removal, and works identically
against a parent handle (cascading to every live child, §5) or a child
handle (targeting exactly that one card).
