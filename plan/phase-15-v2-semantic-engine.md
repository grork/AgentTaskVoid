# Phase 15: v2 semantic engine + the integration API contract

**Depends on:** phase 05 (task operations), phase 06 (config/posture/log), phase 08
(CLI framework + composition root), phase 09 (watchdog).
**Unblocks:** phase 16 (`--icon-file` attaches to the v2 upserting verbs), phase 17
(create-branch gating + `--cwd` on the v2 surface), phase 18 (the translator calls
these verbs).

## Goal

Replace the literal v1 lifecycle verbs with the ERGO-31 semantic verb contract: eight
verbs that make idempotent *claims* about a session's state, an engine that owns all
rendering, clocks, and fan-out memory, and a normative host-agnostic contract doc
(`docs/integration-api.md`) a translator author can target from the doc alone. This is
the engine half of LIFE-24's conduit / translator / engine layering; phase 18 builds
the Claude Code translator against it.

**Arbiter note:** ERGO-31 ("The v2 semantic verb contract") supersedes ERGO-27 ("The
consolidated v1 command surface") as the arbiter on any surface question from this
phase on. Where this file summarizes, ERGO-31 and LIFE-24's ratified sections are
normative.

**Sizing note:** this is the largest phase in the plan. It may execute in sub-parts
(e.g. A: verb surface + claim semantics; B: clocks + fan-out) at the orchestrator's
discretion — that split is progress.md's business, not a plan commitment.

## Decisions implemented

### The verb contract (ERGO-31, "The v2 semantic verb contract")

- **Eight verbs, the sole lifecycle surface:** `working` / `activity` / `blocked` /
  `ready` / `broken` / `agent-started` / `agent-stopped` / `session-ended`. Every verb
  takes a required positional `<handle>` (no default handle, ERGO-27 C3 carried
  forward). Every verb except `session-ended` accepts the identity flags
  `--title`/`--subtitle`/`--icon` and **upserts** the card — the first semantic verb
  creates it; there is no session-start verb. ERGO-31 §1's transition table is the
  normative spec (per-verb flags, from-any-state targets, clock effects).
- **At most ONE free-text value per call**, via a `-` flag read from **stdin**: UTF-8,
  read to EOF, trailing whitespace trimmed. Short host-constrained tokens (handles,
  kind/reason tokens, identity flags) ride argv.
- **Closed kind vocabulary** (ERGO-31 §2): `read`/`edit`/`write`/`search`/`shell`/
  `fetch`/`web-search`/`plan`/`compacting`/`tool`, each with its engine-owned rendered
  verb word. `tool` is the unmapped fallback (renders tool name + label, prettifying
  the MCP `mcp__<server>__<tool>` pattern). No `delegate` kind — subagent spawn is
  `agent-started`/`agent-stopped`. Plan-list progress collapses into `activity --kind
  plan` (no dedicated progress verb; the `(n/m)` label is translator-composed).
- **Closed reason vocabularies** (ERGO-31 §3): `broken --reason` ∈ `{rate-limit,
  overloaded, api-error, timeout, fatal}` with optional `--detail -` appended;
  `session-ended --reason` ∈ `{finished, error}` (token-only): `finished` → remove the
  card, `error` → Broken (the watchdog then reaps).
- **Retire** `start`/`step`/`state`/`attention`/`done`/`fail`. **Carry forward
  unchanged:** `list`/`run`/`doctor`/`clear`/`remove`, the hidden `watchdog` mode, and
  all global flags. `--unsafe` becomes inert for the semantic verbs (projection
  legality is now engine claim semantics); kept for experimentation only.
- **`docs/integration-api.md`** is the new normative, host-agnostic translator-facing
  contract: verbs + transition table, kind and reason vocabularies, the stdin `-`
  rule, idempotency/claim semantics, fan-out addressing, and the FAIL-1/FAIL-2
  posture (exit-0 default, `--strict` opt-in) a hook author must never override.

### The five-state model + claim semantics (LIFE-24, "The host-event → task-state integration semantics")

- **Five states** ranked by cost of ignoring, with fixed AppTaskInfo projections:
  Blocked (`NeedsAttention` + `SetQuestion`), Broken (`Error`), Ready (`Completed`),
  Working (`Running` + `SequenceOfSteps`), Idle (`Paused`). Idle has **no verb** — it
  is reachable only via the engine's Ready→Idle decay.
- **Idempotent claims:** an absent optional flag makes no claim (never clears a
  field); re-asserting a held state never restarts its clocks (only a transition INTO
  Ready starts decay). Duplicate done-signals are expected and harmless.
- **`blocked` requires a literal question** (platform-enforced: `NeedsAttention`
  requires `SetQuestion`, ERGO-10). **Same-locus clearing:** `--agent <id>` records
  the blocked-on locus; the block clears on the next event attributed to that locus
  (its next activity, its `agent-stopped`, or a turn-end `ready`/`broken`). Absent
  agent id → parent locus; degraded fallback (no attribution available) = any
  activity clears. Concurrent blocks: display the latest question; when its locus
  progresses, surface the other.
- **The engine absorbs projection legality:** `activity` against a Blocked card drops
  the question and re-enters Working as part of the verb's claim; every emitted
  (state, content) pair must be a `SafeCombinationMatrix.cs` safe cell. Translators
  never compensate for safe-combination rules (retires the v1 artifact's
  `state running`-before-`step` chain).
- **Content is a causal narrative** at three altitudes: identity (title/subtitle) →
  goal (`working --goal`, from the submitted prompt) → current activity (`activity`,
  human-phrased, never raw payload JSON). Notices ≠ states: absorbed events annotate
  the activity line or completion summary; only turn-ending or question-raising
  events transition state.
- **One shared engine normalizer** for every single-line rendering
  (goal/question/summary/label): collapse whitespace runs → strip light markdown
  decorations (`**`, backticks, `#`) → truncate with ellipsis per field budget.

### Clocks: presence-gated decay vs the hygiene reap (LIFE-24)

- **Only Ready decays.** Its clock accrues only while the user *had a chance* to act:
  device unlocked AND recent input (Win32 last-input + session lock state; CsWin32
  like the existing interop — build detail). Long windows — a courtesy demotion to
  Idle (`Paused`), never inbox-zero. Blocked and Broken never decay (session-truths;
  surviving absence is their purpose).
- **UX decay ≠ hygiene reap.** The existing watchdog expiry machinery (LIFE-22
  per-state idle periods, wall-clock `lastUpdate`, invariant #6 freshness ordering,
  recycle-bin tombstones) stays as-is as the hygiene path; mapping the five semantic
  states onto LIFE-22's decided per-state periods is a build detail. The two clocks
  are never conflated.
- **Engine memory lives in the sidecar** (per-handle, under the WriteGate): semantic
  state, goal, blocked-on locus, decay-accrual bookkeeping, fan-out registry. "If it
  needs memory or a clock, it is engine."

### Fan-out addressing (ERGO-31 §4, LIFE-24, requirements.md)

- `agent-started` registers a child locus; the engine **mints a child card at the 2nd
  concurrent start**, retroactively carding the 1st worker. Child handle =
  **`<session-handle>#<agent_id>`** (deterministic; `list` enumerates parent +
  children as real handles; `remove <child>` targets one). Children are scaffolding:
  Working/Completed only — solicitation always belongs to the session card. They
  retire at fan-in (`agent-stopped`); `remove <parent>` and `session-ended <parent>`
  **cascade** to children.
- **A child card must REUSE the parent session's exact `IconUri`** — passed straight
  to `Create`, never minted via `IconService.Place` (per-handle paths would break
  ERGO-13 icon-URI-keyed glomming). This is the requirements.md v2 fan-out note.
- **Name-only hosts** (`--name` without `--agent`): no child card; the agent surfaces
  as a parent-card activity line (mapping rule 5's degraded resolution).

## Files affected

```
src/Atv/Cli/CommandLine.cs               # v2 verb grammar, `-` stdin flags, v1 verbs removed
src/Atv/Cli/Dispatcher.cs                # route the 8 semantic verbs
src/Atv/Cli/CompositionRoot.cs           # wire engine collaborators (presence probe, normalizer)
src/Atv/Cli/Verbs/*                      # new semantic verb handlers; v1 lifecycle handlers removed
src/Atv/Operations/*                     # claim semantics over the phase-05 core (validator/matrix stay)
src/Atv/Semantics/* (new)                # five-state model, kind/reason vocab, normalizer, rendering, fan-out registry
src/Atv/Persistence/SidecarEntry.cs      # + semantic state, goal, locus, decay bookkeeping, child registry
src/Atv/Watchdog/WatchdogLoop.cs         # + Ready→Idle decay pass (separate from the hygiene reap)
src/Atv/Run/*                            # re-seat the run wrapper on the v2 semantics (contract unchanged)
src/Atv/Program.cs                       # usage text
docs/integration-api.md (new)            # the normative translator-facing contract
tests/Atv.LogicTests/Semantics/*, Cli/*  # TDD suites (fake-backed)
tests/Atv.AdapterTests/*                 # ≥1 real-API e2e per semantic verb
```

## Acceptance criteria (written first)

1. **Transition table proven:** for each of the 8 verbs, logic tests cover its
   ERGO-31 §1 row from EVERY prior semantic state, including the clock-effect column
   (fake clock). Idempotency: absent optional flags clear nothing; re-asserting a held
   state restarts no clock.
2. **Blocked semantics:** `blocked` without a question is refused (validation, exit-0
   posture); same-locus clearing proven for agent-attributed and parent-locus blocks
   (worker chatter leaves a main-thread prompt standing); concurrent-block
   latest-then-surface-other; degraded no-attribution fallback.
3. **Projection legality is structural:** a test enumerates every (state, content)
   pair the engine can emit and asserts each is a safe cell of
   `SafeCombinationMatrix.cs`; `activity` against a Blocked card lands (question
   dropped, Working re-entered) — the v1 step-after-attention refusal is gone.
4. **Stdin + normalizer:** `-` flags read UTF-8 to EOF and trim trailing whitespace;
   multi-line unicode/quote torture strings land intact; the shared normalizer
   (collapse/strip/truncate) is applied to goal, question, summary, and label
   renderings, proven once and reused.
5. **Clocks:** with a fake presence source, Ready decay accrues only while
   present, only a transition INTO Ready starts it, decay lands the card in `Paused`;
   a control proves the LIFE-22 hygiene reap still fires on wall-clock `lastUpdate`
   regardless of presence (the two clocks are independent).
6. **Fan-out:** 2nd concurrent `agent-started` mints a child card AND retroactively
   cards the 1st; child handle format `<session>#<agent_id>`; the child's `IconUri`
   is byte-identical to the parent's; `agent-stopped` retires; parent `remove`/
   `session-ended` cascade; name-only registration mints no child and renders the
   parent activity line.
7. **Surface migration:** the parser rejects the six v1 lifecycle verbs; `list`,
   `run`, `clear`, `doctor`, `remove`, `watchdog`, and all global flags behave
   per their existing suites (green, adapted only where they touched v1 verbs); the
   `run` wrapper's phase-11 observable contract (exit-code passthrough, debounce,
   lingering card) is preserved over the v2 internals.
8. **`docs/integration-api.md`** exists and is self-contained: a translator author
   can produce a correct integration from it alone (verbs, vocabularies, stdin rule,
   claim semantics, fan-out, posture). ERGO-27's record carries its
   superseded-by-ERGO-31 note.
9. **Real API + AOT:** adapter suite has ≥1 real-API e2e per semantic verb (incl. one
   fan-out mint/cascade path); NativeAOT publish clean; invariants #2/#3/#4/#5/#6/#7
   re-verified.

## Out of scope

The Claude Code translator, `map.json`, and plugin (phase 18); `--icon-file` and the
tile treatment (phase 16); `.atv.json` and `--cwd` (phase 17); the parked `(n/m)`
pinning rendering nicety (ERGO-31's parked note); any raw card-control tier (ERGO-32,
DEFERRED); two-way interaction (INTER-*, DEFERRED); Copilot/Codex/pi anything.
