# ERGO-31: The v2 semantic verb contract (the engine's public integration API)
**Status:** DECIDED
**Plan:** phase-15
**Decision:** The v2 semantic verbs below are the *sole* lifecycle surface — eight verbs
(`working`/`activity`/`blocked`/`ready`/`broken`/`agent-started`/`agent-stopped`/
`session-ended`), a closed 10-kind vocabulary, closed `broken`/`session-ended` reason
vocabularies, one free-text `-` value per call, and engine-derived Idle/Paused (no verb).
The v1 lifecycle verbs (`start`/`step`/`state`/`attention`/`done`/`fail`) retire; the
data/util verbs (`list`/`run`/`doctor`/`clear`/`remove`), the hidden `watchdog` mode, and
all global flags carry forward. This **supersedes ERGO-27** ("The consolidated v1 command
surface") as the shipped surface. The normative contract publishes to a new host-agnostic
`docs/integration-api.md`. Ratified with the operator 2026-07-13, grounded in the phase-14
Claude Code host-event traces (`docs/host-events/claude-code.md`).

## Fixed inputs (ratified in LIFE-24, 2026-07-11 — not re-litigated)
See LIFE-24 ("The host-event → task-state integration semantics") for the five-state model,
its AppTaskInfo projections, the durable mapping rules, and the conduit/translator/engine
layering. In brief: verbs are idempotent claims (an absent flag makes no claim; re-asserting
a held state never restarts clocks); the engine owns projection legality; the first semantic
verb upserts the card (no session-start verb); Blocked clears by same-locus attribution;
kinds name the mechanism, never the purpose; at most ONE free-text value per call via a `-`
flag read from stdin (UTF-8, to EOF, trailing whitespace trimmed); one shared engine
normalizer for every single-line rendering.

---

## 1. Verb signatures and the normative transition table

Every verb takes a **required positional `<handle>`** (ERGO-6, "The identifier a caller
holds"; ERGO-27 C3 — no default handle). Every verb **except `session-ended`** accepts the
identity flags `--title <t>` / `--subtitle <t>` / `--icon <token>` and **upserts** the card;
the stateless translator passes them on every call (ERGO-25, "`start` on an already-live
handle", makes re-application idempotent). Identity flags ride argv — host-constant strings
with no quote hazard (Windows forbids `"` in paths; titles are translator-set constants).
**At most one free-text `-` value per call**, read from stdin.

| Verb | `-` reads | Other flags | From ANY state → | Clock effect |
|---|---|---|---|---|
| `working <h>` | `--goal -` | — | **Working** (sets goal) | leaves Ready → clears its decay |
| `activity <h>` | `--label -` | `--kind <k>`, `--agent <id>`, `--name <n>` | **Working** (if Blocked: drops the question, re-enters Working) | leaves Ready → clears its decay |
| `blocked <h>` | `--question -` | `--agent <id>` | **Blocked** | none — never decays |
| `ready <h>` | `--summary -` | — | **Ready** | **starts** presence-gated decay on a transition INTO Ready; re-assert while already Ready never restarts it |
| `broken <h>` | `--detail -` | `--reason <token>` | **Broken** | none — never decays |
| `agent-started <h>` | — | `--agent <id>`, `--name <n>` | registers a child locus (engine mints a child card at the 2nd concurrent start); **Working** (or Blocked-preserved), advancing the parent's own step to `"Started {name}"` -- 2026-07-16 amendment, see below | leaves Ready → clears its decay |
| `agent-stopped <h>` | — | `--agent <id>` | retires the child locus (fan-in) | — |
| `session-ended <h>` | — | `--reason <token>` | `finished` → card removed · `error` → **Broken** | — |

**Claim semantics / notes:**
- `working` and `activity` both land the card in **Working** but at different altitudes:
  `working` carries the turn's **goal** (altitude 2, set once per turn from the submitted
  prompt); `activity` carries the **current activity line** (altitude 3, updated per tool).
  Both clear a pending Blocked (a new prompt or a fresh activity means the block resolved)
  and both leave Ready (new work started), clearing its decay clock.
- **`blocked` requires a literal question** (`--question -`). The platform enforces it
  (`NeedsAttention` requires `SetQuestion`) and the semantics agree. `--agent <id>` records
  the blocked-on locus for same-locus clearing (LIFE-24 S1-walk); absent → parent locus,
  degraded fallback = any activity clears (empirical item 2, now answered — see below).
- **`ready` is the only decaying state.** The decay clock accrues only while the user is
  present (device unlocked AND recent input); it is a courtesy demotion to Idle, never
  inbox-zero. Duplicate done-signals (`Stop` + `idle_prompt`) are expected and harmless — the
  second is a no-op re-assert that does not restart the clock.
- **`broken`** carries a closed `--reason <token>` plus an optional free-text `--detail -`
  (the host's real error message), rendered after the fixed reason word. It is the one
  terminal verb with a free-text value. Broken → `Error`, and `CreateTextSummaryResult` under
  `Error` renders fully with no question attached (no ERGO-10 crash surface).
- **Idle / Paused has NO verb.** It is reachable *only* via the engine's Ready→Idle decay
  clock — no host event claims it. This retires v1's `state paused`. The watchdog orphan-reap
  is a *separate* always-on clock (wall-clock + liveness), never conflated with UX decay.
- **Idempotency:** an absent optional flag makes no claim (never clears a field); re-asserting
  an already-held state never restarts its clocks (only a transition INTO Ready starts decay).
- **2026-07-16 amendment: `agent-started` advances the parent's step.** Found live (dogfooding
  the two bug fixes above this amendment): a real `agent-started` (has `--agent <id>`) used to
  leave the PARENT card's content/state completely untouched -- the transition table's
  target-state column was blank by original design, on the theory that the new child card(s)
  appearing was signal enough. In practice the parent itself froze on whatever activity line
  preceded the spawn for the whole fan-out window, which reads as stale/wrong on its own even
  though the child cards update fine. Now routes through the same `activity`-style
  archive-then-set (`"Started {name-or-agentId}"`) via the shared post-locus-change projection,
  so a currently-Blocked parent keeps its question rather than losing it. `agent-stopped`
  deliberately does NOT get the same treatment (operator decision) -- stop events arrive in a
  slow trickle well after the fact, and the child card retiring is signal enough on its own.

## 2. Canonical kind vocabulary (closed) → rendered verb word

Kinds name the **mechanism**; the label carries the **subject** (the raw `--label -` value);
the engine owns the **verb word** and its rendering. The list is closed and small; it never
gates a new tool — unmapped tools fall to the `tool` fallback, and a user map row upgrades the
wording (LIFE-24 S1-walk).

| kind | rendered word | Claude Code source tool |
|---|---|---|
| `read` | Reading | `Read` |
| `edit` | Editing | `Edit`, `NotebookEdit` |
| `write` | Writing | `Write` |
| `search` | Searching | `Grep`, `Glob` |
| `shell` | Running | `Bash` |
| `fetch` | Fetching | `WebFetch` |
| `web-search` | Searching the web | `WebSearch` |
| `plan` | *(renders the `(n/m) item` label itself)* | `TodoWrite` |
| `compacting` | Compacting conversation | `SessionStart source=compact` |
| `tool` | *(fallback: tool name + label; engine prettifies the MCP `mcp__<server>__<tool>` pattern)* | any unmapped tool |

- `search` merges content search (`Grep`) and filename glob (`Glob`) — both render "Searching";
  the label distinguishes. (Considered and rejected: splitting into `search`/`find`.)
- No `delegate`/Agent-tool kind — the `Agent` tool's subagent spawn is owned by
  `agent-started`/`agent-stopped`, not rendered as an activity. Whether a host maps its
  delegate tool to a kind at all is a per-host translator-table detail (LIFE-24), not a
  vocabulary entry here.
- **The plan-list determinate-progress channel collapses into `activity`** (ratified
  2026-07-13). `AppTaskInfo.CreateSequenceOfSteps` has **no numeric total field** — "3 of 7"
  is only ever text in a step string — so plan updates and tool activities are just strings in
  one FIFO step stream (`executingStep` + `completedSteps`), mirroring the host's own terminal
  scrollback; there is no structured slot for them to contend over. A dedicated `progress`
  verb was rejected: it would build the same `(n/m)` string for the same slot and buy nothing.
  A host plan-list (`TodoWrite`) maps to `activity --kind plan`, whose label the translator
  composes as `(n/m) <item>` (a few lines of counting code — the kind of structural quirk
  LIFE-24 keeps in the translator script, not the map table). See the parked rendering note
  below.

## 3. Reason vocabularies (closed, host-value-mapped)

- **`broken --reason <token>`** ∈ `{ rate-limit, overloaded, api-error, timeout, fatal }` →
  "Rate limited" / "Overloaded" / "API error" / "Timed out" / "Failed". `fatal` is the
  catch-all. Optional `--detail -` free-text is appended ("API error: connection reset by
  peer"). Grounded in Claude Code `StopFailure(rate_limit|overloaded|…)` and Copilot
  `errorOccurred`.
- **`session-ended --reason <token>`** ∈ `{ finished, error }` (token-only, no free-text).
  `finished` → remove the card; `error` → **Broken** (surfaces the death; the watchdog then
  reaps). Hosts value-map their own reasons onto these two: Copilot
  `complete|user_exit|abort` → `finished`, `error|timeout` → `error`; Claude Code
  `other|prompt_input_exit` → `finished`.

## 4. Fan-out addressing

- The engine mints a **child card at the 2nd concurrent `agent-started`**, retroactively
  carding the 1st worker (a definition, not a bug — fan-out is only recognizable at the 2nd
  concurrent start; LIFE-24). Children are scaffolding: Working/Completed only (solicitation
  always belongs to the session card), same icon so they glom into the session's taskbar
  group (ERGO-13, "Is grouping keyed on the exact icon URI string?").
- **Child-card handle** is derived deterministically as **`<session-handle>#<agent_id>`**, so
  `list --json` enumerates parent + children addressably and `remove <child-handle>` targets
  one. `remove <parent>` and `session-ended <parent>` **cascade** to children.
- **Name-only hosts** (`agent-started --name <n>` with no `--agent <id>`): no child card is
  minted (concurrent same-name agents are indistinguishable). The agent surfaces as a
  parent-card activity line — LIFE-24 mapping rule 5's degraded resolution.
- **Copilot CLI amendment (2026-07-16, host 1.0.71 capture):** raw
  `subagentStart`/`subagentStop` remain name-only, but the surrounding Task-tool hooks expose a
  caller-supplied unique task `name`, while the child's first hook repeats the exact task
  prompt under its `call_*` child session id. The Copilot integration therefore keeps a
  short-lived, plugin-local prompt-hash bridge (`pending hash -> parent/task`, atomically
  claimed into `call_* -> parent/task`) and supplies that task name as `--agent`. This enables
  the ordinary full-resolution engine contract without adding Copilot logic or correlation
  state to `atv` itself. Ambiguous identical prompts fail open to the name-only/degraded
  behavior; raw prompts are never persisted.

## 5. Supersession of ERGO-27 and the contract doc

- **Retire** (replaced by the semantic verbs): `start` → upsert-on-first-write + identity
  flags; `step` → `activity`; `state` → `working`/`activity` (Paused is now engine-derived,
  no verb); `attention` → `blocked`; `done` → `ready`; `fail` → `broken`.
- **Carry forward unchanged:** `list`, `run`, `doctor`, `clear`, `remove` (data/util); the
  hidden `watchdog` mode; and all global flags (`--json` / `--strict` / `--verbose` /
  `--watchdog-mode` / `--unsafe` / `--wait-for-debugger`). `remove` stays for manual removal,
  fan-out child addressing, and cascade. `--unsafe`'s ERGO-10 safe-combo-bypass role is now
  internal to the engine's claim semantics, so it is inert for the semantic verbs (kept for
  experimentation only).
- **The translator-facing contract doc is a new host-agnostic `docs/integration-api.md`** — the
  normative surface a first- or third-party translator author targets from the doc alone. The
  per-host translator artifacts stay in `integrations/<host>/` (LIFE-24: `hooks.settings.json`
  conduit + `translate.ps1` + `map.json`). ERGO-27 stays DECIDED (its `**Plan:** all-phases`
  stamp is permanent per the process rule — a v2 contract does not un-plan the shipped v1
  phase) with a superseded-by-ERGO-31 note.

## Empirical grounding used (phase-14 traces)

`docs/host-events/claude-code.md` (Claude Code 2.1.207) confirmed the load-bearing facts:
`UserPromptSubmit` → the Working entry point (`working`); `agent_id` is the subagent tag
across a subagent's whole tool lifecycle *and* on `PermissionRequest` (fan-out §4 above,
LIFE-24 empirical item 2 — ANSWERED); a permission prompt's subagent origin is on
`PermissionRequest`, not `Notification`; `idle_prompt` fires ~60 s after a clean turn end,
once, focus-independent (LIFE-24 empirical item 3 — ANSWERED — so `ready` maps `Stop` AND
`idle_prompt` as expected duplicate done-signals); a user interrupt raises no distinguishing
hook event (an interrupted card reads Working until the next event — accepted, LIFE-24).

## Spawned / parked

- **New question filed:** ERGO-32 ("A low-level / raw card-control API") — OPEN, cross-linked
  to the deferred INTER-* two-way scope. The v2 verbs are the sole surface; a raw tier that
  bypasses the engine's meaning (re-exposing the ERGO-10 crash surface and orphan-leak, and
  corroding the structured verbs as THE API — the same anti-pattern LIFE-24 rejected 3×) is
  deferred to its own question, to be answered only if a concrete consumer appears the
  semantic verbs cannot serve. Ratified 2026-07-13.
- **Parked rendering note (not a verb-contract concern):** whether the engine should *pin* the
  latest `(n/m)` count somewhere stable (e.g. compose it into the goal/subtitle line) so it
  doesn't scroll away under tool activity — vs. letting it flow past like the terminal does. A
  downstream engine-rendering nicety; does not block this contract.

Spawned from LIFE-24's conduit/translator drill-down (2026-07-11). The per-host translator
mapping tables and the remaining open empirical items stay with LIFE-24, INFRA-23 ("The
host-event behavior recorder"), and INFRA-31 ("Recorder legs for the not-yet-testable hosts
(Copilot / Codex / pi)").
