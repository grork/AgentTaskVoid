# LIFE-24: The host-event → task-state integration semantics (the mapping layer)
**Status:** DECIDED (2026-07-13)
**Plan:** phase-15
**Decision:** The integration layer is the **five-state semantic model** (Blocked / Broken /
Ready / Working / Idle) + the **seven durable mapping rules** + the **conduit / translator /
engine layering** — hosts project their event vocabularies onto the engine's idempotent
semantic claims via per-host translators, keeping atv host-agnostic. The verb contract is
ERGO-31 (v2 semantic verbs, supersedes ERGO-27). Claude Code's mapping is grounded + validated
by the phase-14 capture (`docs/host-events/claude-code.md`); other hosts' tables + empirical
item 4 defer with INFRA-31 (deferred-until-testable, recipe = INFRA-32). Empirical items 1–3
ANSWERED — badge priority is **Error > NeedsAttention > Completed > Paused > Running**, so
Blocked dominates every non-urgent state (no engine badge-mitigation needed). Consumed into a
future phase as the v2-engine + Claude-Code-translator build (the concrete `map.json`/
`translate.ps1` is build work, not a further decision). Full model + rules RATIFIED 2026-07-11
(three legs — also decided LIFE-25, spawned ERGO-31); INFRA-23 recorder shipped in phase-14.

## Question
How should a host's lifecycle *events* and *states* (Claude Code's, and by extension every
host's) be **modeled** onto the AppTaskInfo card — its state, its step/summary content, and
even how many cards a single session produces — as a deliberate *integration semantic layer*,
rather than the current largely literal, near-1:1 event→verb mapping? This is NOT a single
field addition or a state-enum split; it is a layer of integration that needs its own design
pass, probably grounded in the **union / common patterns across hosts** (Claude Code, Copilot
CLI, Codex) rather than any one host's event names.

Additionally, in trying to address this it's unclear if the solution should be something that
rolls directly into this app -- atv -- directly, of if as part of building the integration
into hosts there should be an additional scripts as part of the hook configuration that is
unique to that host and does the minimal translation into the atv verbs etc.

## Why this surfaced
The phase-13 Claude Code integration dogfood (2026-07-10, first real end-to-end hook run).
The shipped `integrations/claude-code/` mapping (LIFE-10, "Host-agnostic CLI abstraction hook
events map onto") works mechanically — every hook fires, the card starts/steps/attention/
done/removes — but driving it against a live session made it clear the *semantics* are
loose: the mapping was authored event-by-event, not from a model of what an agent session's
states actually *mean*. Several distinct nuances all pointed at the same missing layer.

## The threads to review (operator, 2026-07-10) — the whole event set, not piecemeal
1. **`running` is wrong at session start.** `SessionStart → start` currently lands the card in
   `Running` before the user has even sent a message. A just-opened session that is waiting
   for its first prompt is not "working" — that is a different state (idle / awaiting-input)
   than mid-turn `Running`.
2. **"Idle" is overloaded.** When a session is sitting idle there are genuinely different
   situations the current model collapses: (a) it *completed a turn* and is waiting for the
   next prompt; (b) it is idle/paused with nothing pending (maybe `Paused`?); (c) a *tool* is
   requesting permission; (d) a *question* has been raised. Today (a)–(b) both look like
   `Completed`-then-idle and (c)–(d) both look like `NeedsAttention`, flattening real
   distinctions the user would want to tell apart at a glance.
3. **Review ALL the events.** This wants a systematic pass over the full host event vocabulary
   (SessionStart/PostToolUse/Notification/Stop/SessionEnd and their siblings — and the
   equivalents on other hosts), asking what each *means* for the card, not just wiring each to
   the nearest existing verb.
4. **Subagents may deserve their own cards.** Subagent start/stop events could spawn *new*
   AppTaskInfo cards with the *same icon* but *different sessions/handles*, so they **glom
   together on the taskbar** as one icon group while still being individually visible in the
   flyout — leaning on the icon-keyed grouping (ERGO-13, "Empirical: is grouping keyed on the
   exact icon URI string?"; ERGO-4/ERGO-15 grouping model). This is a concrete idea to weigh,
   not a decision.
5. **Step content is a raw JSON pass-through.** The current `step` text is the host payload
   more-or-less verbatim (e.g. `PowerShell: {"command":"Get-ChildItem","description":"…"}`).
   There is likely a lot of nuance in what a step *should* convey (human-readable, host-
   normalized, which tool, at what altitude) — and doing it well again points at the **union /
   common patterns across sessions and hosts**, not per-host string formatting.
6. **Two-way communication looms but is NOT being reopened now.** This modeling review makes
   the deferred interaction round-trip (INTER-1/2/3) feel more load-bearing — the operator is
   "starting to wonder if we need to revisit two-way," but is explicitly **not ready to rip
   that bandaid off yet**. Noted here as an adjacent pressure, deliberately left deferred.

## What makes it non-trivial (constraints)
- **The platform bounds what is expressible.** AppTaskInfo has its *own* state enum, and the
  ERGO-10 ("Guarding unsupported state × content × mutator combinations") crash matrix limits
  which (content, state) combinations actually render (some silently mis-render, some crash
  `explorer.exe`). A richer semantic model can only use states/combos the Shell will actually
  paint — "running **and** needs-attention at once," for instance, may simply not be a legal
  card state. Any new model must be reconciled against `SafeCombinationMatrix.cs`.
- **Hosts differ; the value is in the union.** Claude Code, Copilot CLI, and Codex have
  different event vocabularies and payloads (LIFE-12/13/14 inventories). A per-host mapping
  that is semantically sharp for one host but ad hoc across hosts misses the point — the
  design should find the common lifecycle model the host adapters each project onto, keeping
  `atv` itself host-agnostic (LIFE-2, "Covering hook behavior across different agent hosts").
- **Immutability + grouping costs.** Icon is set only at Create and grouping is icon-URI-keyed
  (ERGO-13); the subagent-cards idea and any icon/state rework interact with the per-handle
  icon model (ERGO-22) and the separate-by-session default (ERGO-15).
- **Content model is whole-replacement.** `step`/summary replace content wholesale (ERGO-8),
  and the safe content shapes are constrained (ERGO-3/ERGO-9/ERGO-11) — normalizing step text
  richly is bounded by what content shapes are safe to emit.

## Not A or B (explicitly)
Earlier framing offered (A) surface the attention reason as a field in `list --json`, or
(B) split `state` into orthogonal progress + solicitation fields. The operator rejected both
as too small: "I don't think it's really A or B. I think there's a layer of *integration* that
we need to think a little bit more about." The deliverable of this question is that integration
layer / semantic model — of which exposing-the-reason and any state re-shape would be
downstream consequences, not the thing itself.

## Scope note
Per the operator (2026-07-10): file OPEN for later discovery/answering; this is a **big** one
that likely needs its own thinking about the union / common patterns across hosts. It does
**not** change the current v1 build plan (phase 13 ships the working, if literal, Claude Code
mapping as-is). Two-way communication (INTER-*) stays deferred and is explicitly **not** being
reopened by this question yet. Parked to be taken up after the current build wraps.

---

## RATIFIED 2026-07-11: the semantic model and the durable mapping rules
Locked in an interactive answer session (operator + Fable). This section is the model;
the "Still OPEN" section below is what remains before the question is DECIDED.

### The projected experience
A card exists to answer two questions at a glance: **"what happens if I don't switch
back?"** (alerting) and **"what is happening?"** (observability — users watch the kettle
boil, they don't just listen for the beep). States model the *user's decision*, not the
host's event machine.

Five states, opinionated vocabulary (users learn it), ranked by the cost of ignoring:

| State | Claim it makes | Decays? | AppTaskInfo projection |
|---|---|---|---|
| **Blocked on you** | Mid-work, stalled on your decision (permission / question / form) | Never | `NeedsAttention` + `SetQuestion` |
| **Broken** | Turn/session died without delivering (API error, fatal failure) | Never | `Error` |
| **Ready for you** | Turn finished; fresh output awaits review | Presence-time → Idle | `Completed` |
| **Working** | Progressing; needs nothing | — | `Running` + `SequenceOfSteps` |
| **Idle, promptable** | Open; nothing owed either way (fresh / decayed / between prompts) | — | `Paused` (defined vocabulary) |

- **"Idle" is not a state** — it is four states distinguished by *why* the session waits
  (decision / review / failure / nothing). The hosts emit distinct signals for each;
  only a mapping can flatten them.
- **Blocked/Broken are session-truths** — true whether the user is present or not; they
  never decay (surviving your absence is their purpose). **Ready is an
  interaction-truth** ("fresh output for you") — the only presence-relative state. Its
  decay clock accrues only while the user *had a chance* to act: device unlocked AND
  recent input (Windows exposes lock/unlock, suspend/resume, last-input natively). Long
  windows — a courtesy demotion, not inbox-zero.
- **UX decay ≠ hygiene reap.** Presence-gated decay is semantics; the watchdog's orphan
  reap (card whose session died without a session-end event) stays wall-clock +
  liveness. Two different clocks, never conflated.
- **Content is a causal narrative** at three altitudes: which session (identity) → what
  the turn is for (goal, from the submitted prompt — currently discarded) → what it is
  doing now (human-phrased activity, never raw payload JSON). Progress is never
  fabricated: liveness (a visibly-changing activity line) plus the host's own task/plan
  list as the only determinate progress (maps ~literally onto `SequenceOfSteps`
  completedSteps/currentStep, e.g. "3 of 7").
- **Notices ≠ states.** Events the session already absorbed (tool failure with retry,
  auto-classifier denial) annotate the activity line or the completion summary; an
  event only transitions state if it ends the turn or raises a question.
- **Cards per concurrent locus of work** (alerting AND observability, so actionability
  alone is not the unit): the session always has a card; subagents get child cards only
  under fan-out (≥2 in flight), keyed (session, agent-instance), same icon so they glom
  into the session's taskbar group (ERGO-13, "Empirical: is grouping keyed on the exact
  icon URI string?"). Children are scaffolding: Working/Completed only — solicitation
  always belongs to the session card (one terminal, one input point) — and they retire
  at fan-in. Fan-out is only recognizable at the 2nd concurrent start, so the 1st
  worker is carded retroactively (a definition, not a bug). A subagent is NOT a
  session: hosts model it as tagged participation in the parent's stream, and so do we.

### The durable mapping rules (how to onboard ANY host's event vocabulary)
1. Classify each host event by the **claim it licenses**, not its name: changes
   who-waits-on-whom → state transition; changes what's-happening → content update;
   reports something already absorbed → notice, or ignore.
2. A state transition must answer "why is it waiting / what does user action buy":
   decision pending → Blocked; delivered → Ready; died → Broken; prompt accepted →
   Working; open-with-nothing-owed → Idle. No clear claim → not a state event.
3. Blocked requires a literal question to show. The platform enforces this
   (`NeedsAttention` requires `SetQuestion` — ERGO-10, "Guarding unsupported state ×
   content × mutator combinations") and the semantics agree: every real block has one.
4. Hosts mark the *moment* waiting begins; the layer owns *duration*. Only Ready
   decays. Never encode a host-side timeout into state.
5. The model is fixed; hosts vary only in rendering **resolution**. Fidelity degrades
   gracefully with payload richness: agent instance-id → full child cards with live
   activity; name-only → name + state + elapsed; nothing → parent activity line only.
6. Never echo host payload; render the human claim at the right altitude.
7. Doc-derived mappings are hypotheses. No host mapping counts as verified without a
   live event capture (INFRA-23, "Host-event behavior recorder", exists for exactly
   this).

### Grounding on today's hosts (doc fetches 2026-07-11)
- **Claude Code** (~30 events now vs the 9 in LIFE-12, "Claude Code hook surface
  inventory"): `UserPromptSubmit` = entry to Working (unused by the shipped phase-13
  mapping); `Notification: idle_prompt` = "Claude is done and waiting for your next
  prompt" (docs verbatim) = the Ready signal; `StopFailure(rate_limit|overloaded|…)` =
  Broken; every hook fired inside a subagent carries `agent_id` + `agent_type` (docs:
  "Present only when the hook fires inside a subagent call") = full fan-out resolution;
  `prompt_id` (v2.1.196+) correlates events to turns = goal-line support;
  `Stop.last_assistant_message` = completion-summary material. Subagents run NO
  lifecycle of their own — bracket events (`SubagentStart`/`SubagentStop`) plus tagged
  participation in the parent session's stream.
- **Copilot CLI** (same 13 events as LIFE-13, "GitHub Copilot CLI hook surface
  inventory"): `notification: agent_idle` = the Ready signal; `sessionEnd` reasons
  (`complete|error|abort|timeout|user_exit`) split done/Broken/gone at session end;
  `errorOccurred(errorContext, recoverable)` = Broken/notice material. Fan-out
  resolution is LOW: `subagentStart/Stop` carry name only (no instance id — concurrent
  same-name agents indistinguishable), built-in general-purpose agents emit nothing,
  tool-event attribution to subagents is undocumented. Note its `permissionRequest`
  event fires BEFORE the permission service (auto-allow included) — it does NOT mean "a
  human is being asked"; `notification: permission_prompt` is the user-facing signal.
- **Codex CLI** (Sonnet dive 2026-07-11, corroborating LIFE-14, "Codex hook surface
  inventory"): a real hooks system, stable since ~v0.124 — spawned command, snake_case
  JSON on stdin, common fields `hook_event_name`/`session_id`/`transcript_path`/
  `permission_mode` = Claude-convergent (third data point of vocabulary convergence).
  Coverage: Working (`UserPromptSubmit`), Blocked (`PermissionRequest`), Ready (`Stop`;
  no idle signal), subagents (`SubagentStart`/`SubagentStop`) all present; activity is
  holed by an open bug (openai/codex #20204 — only shell/unified_exec/apply_patch/MCP
  tools emit tool hooks); Broken ABSENT via hooks (turn failures only surface in the
  separate `exec --json`/app-server transports); session end ABSENT (the watchdog reap
  is the card-removal path, and Ready→Idle decay degrades a quiet death gracefully).
  Codex hooks are weeks old and churning — capture before trusting any row.
- **pi** (Mario Zechner's pi-coding-agent, pi-mono; Sonnet dive 2026-07-11 — the first
  host with NO spawn-per-event surface): all event handlers are in-process TypeScript
  extensions (`pi.on(event, handler)` from `~/.pi/agent/extensions/`); the alternatives
  are a `--mode json`/`rpc` JSONL stdout stream or passive session files. Events cover
  `session_start` AND `session_shutdown` (both with reasons — a better session-end than
  Codex), `turn_start`/`turn_end`, `input` (goal material), tool execution with args
  and results, errors via `isError`. NO subagent events (subagents are DIY via its
  API); NO built-in permission system (Blocked may simply never occur on a default pi
  setup — the model absorbs the absent state). pi is the outlier class that forced the
  conduit layer to be named: its integration is a thin TS extension invoking the
  structured verbs directly — translation in the host's own stack, fully independent
  of atv.
- **Claude Code and Copilot are blind to user interrupt** (nothing fires) → an
  interrupted card reads Working until the next event. Accepted: the interrupting user
  is, by definition, at the terminal.

### Open empirical items (targets for the INFRA-23 recorder)
1. `NeedsAttention`'s rank in the taskbar group-badge priority (documented: Error >
   Completed > Paused > Running; NeedsAttention untested). If Blocked doesn't dominate
   the badge, the model's most urgent state is its least visible. (An atv/taskbar
   experiment, not a hook capture.) — **ANSWERED (2026-07-13):** the chain is
   **Error > NeedsAttention > Completed > Paused > Running**. Verified on the live
   taskbar (Win 11 26200) by staging shared-icon glommed pairs (throwaway probe forcing
   one shared IconUri): Blocked+Completed badges as the exclamation (Blocked wins);
   Blocked+Broken badges as the red X (Broken wins). So **Blocked dominates every
   non-urgent state** — the concern (Blocked hidden behind Ready) does NOT occur; only
   Broken outranks it, which is acceptable (both are "must look" session-truths). No
   engine badge-mitigation is needed. Recorded in `docs/windows-ui-shell-tasks/README.md`.
   (Aside: the flyout **list order** puts NeedsAttention first even when Error owns the
   badge — list order ≠ badge priority.)
2. Claude Code: which event types actually fire inside subagents; `agent_id` uniqueness
   across PARALLEL spawns; whether a subagent-triggered `Notification:
   permission_prompt` carries the `agent_id` (would let the session's Blocked state
   name the stalled worker). — **ANSWERED (2026-07-13, `docs/host-events/claude-code.md`):**
   subagent-scoped `SubagentStart/Stop` + `Pre/PostToolUse`/`PostToolBatch`/
   `PostToolUseFailure`/`PermissionRequest` all carry the subagent's `agent_id`; `agent_id`
   is unique across parallel spawns; `Notification:permission_prompt` does NOT carry it —
   attribution lives on `PermissionRequest` (drives ERGO-31 §4's fan-out addressing).
3. Claude Code: does `idle_prompt` fire after a user interrupt, and on what timing /
   repetition? — **ANSWERED (2026-07-13):** `idle_prompt` fires ~60 s after a clean turn
   end, once, focus-independent; the after-interrupt sub-case stays weakly answered (never
   idled past ~60 s post-interrupt in capture). So `ready` maps both `Stop` and `idle_prompt`
   as expected, harmless duplicate done-signals.
4. Copilot CLI: do tool hooks fire during subagent execution at all; `sessionId` scope
   in subagent payloads. (Host not installed on this box — needs the deferred
   phase-13 leg's machine.) — **DEFERRED with INFRA-31** (2026-07-13, DEFERRED
   deferred-until-testable); answered when Copilot's recorder leg is built on a machine
   that can run it, per INFRA-30's rollout policy and the INFRA-32 onboarding recipe.

## RATIFIED 2026-07-11 (same session, second half): layer placement — conduit / translator / engine

Settled after the four-host reality check above and four rejected intermediates:
- **Smart per-host scripts** (all logic in hook one-liners): untestable, and the
  JSON-embedded-in-JSON escaping layer is where both phase-13 live bugs occurred.
- **Host adapters compiled into atv** (`atv ingest claude-code`): one host's event
  drift forces atv releases on every other host's users; a new host requires
  contributing to atv rather than rolling your own.
- **Declarative host-profile files interpreted by `atv ingest --profile`**: funnels
  all integration through one anonymous verb; the structured verbs stop being the API.
- **Semantic verbs reading raw payload + profile from stdin**: rejected by operator.

### The three layers

| Layer | Lives | Owns | Rule |
|---|---|---|---|
| **Conduit** | External, per host | Event delivery only | Logic-free. Spawn-per-event hosts: the hook/settings config. In-process hosts (pi): a thin extension/shim. |
| **Translator** | External, per host, any tech | **Extraction**: routing (event → verb), property paths, tool → canonical-kind map, value maps | Stateless: one payload in, at most one structured verb call out. Ships as a real artifact (e.g. `integrations/<host>/translate.ps1`, or code inside pi's extension), never as JSON-embedded one-liners. |
| **Engine** (atv) | The binary | The structured semantic verbs (the public integration API), **rendering**, ALL state and clocks (fan-out memory, retroactive carding, goal memory, plan progress, decay, presence, reap) | If it needs memory or a clock, it is engine. |

**Extraction vs rendering — the boundary that makes this work.** Extraction is
host-specific but data-shaped: Claude Code `PostToolUse` + a tool-map row
(`Edit` → kind `file-edit`, label path `$.tool_input.file_path`) yields the pair
(`file-edit`, `C:\repo\src\auth.ts`). Rendering is host-independent code: the engine
turns that pair into "Editing auth.ts" (basename, verb word per kind, truncation,
agent attribution, safe-combination projection). Same split for goal (clean/truncate
the raw prompt) and summary (first line of the last assistant message). Translators
hand over raw plucked values; the engine does everything interesting to them.

### Consequences
- **The verb contract is now the load-bearing artifact** (the ERGO-27, "The
  consolidated v1 command surface", successor pass): the v2 semantic verbs
  (`working --goal`, `activity --kind <canonical> --label <raw> [--agent <id>
  --name <n>]`, `blocked --question`, `ready --summary`, `broken --reason`,
  `agent-started`/`agent-stopped`, `session-ended`), the canonical kind vocabulary,
  the reason vocabulary, and text-passing ergonomics (arbitrary text as Windows argv
  is a quoting hazard; a verb reading ONE text value from stdin, e.g. `--question -`,
  is a candidate).
- `atv ingest` and a formal profile schema are dropped. A translator MAY be
  table-driven internally; that is its own business.
- **Accepted trades:** (a) cross-host semantic consistency rests on the verb-contract
  doc plus first-party translators — we author all four hosts' translators embodying
  the mapping rules — rather than central enforcement; we never had enforcement power
  over third parties anyway. (b) Spawn-per-event hosts cost two processes per event
  (shell + atv) — already the status quo of the shipped phase-13 integration.

## RATIFIED 2026-07-11 (third leg — the conduit/translator drill-down)

### The per-host artifact shape (spawn-per-event hosts)

```
integrations/<host>/
  hooks.settings.json   conduit: N copies of one logic-free line
  translate.ps1         translator: read stdin, consult map, emit at most one verb
  map.json              sidecar extraction table: event→verb routing,
                        tool→canonical-kind, label property paths, value maps
```

- The conduit line is a plain program+args invocation of the translator file,
  passing the event name as an argument (e.g. `powershell.exe -NoProfile
  -ExecutionPolicy Bypass -File translate.ps1 -Event PostToolUse`) — it parses
  identically under bash/cmd/PowerShell, so the `"shell": "powershell"` selection
  footgun is gone. This decided LIFE-25 ("Should host hooks invoke the `atv` exe
  directly instead of via a shell?").
- `map.json` is a **first-party convention, never a public schema**: atv never
  reads it; the structured verbs remain the only API. It exists so INFRA-23
  captures become reviewable table diffs and users can add rows (e.g. their own
  MCP tools) without touching code.
- Structural quirks that don't fit rows stay as code in the script; the table
  must never grow into a language.

### Translator tech: per-host, the author's choice (the layer contract stays tech-agnostic)
- pi: TypeScript inside the extension — conduit and translator collapse into one
  in-process artifact.
- Our three Windows spawn-per-event hosts (Claude Code, Copilot CLI, Codex):
  Windows PowerShell **5.1-compatible-subset** script — the only in-box
  JSON-capable runtime, forced by the DIST-4 ("Posture for the zero-pre-install
  script consumer") paste-and-go posture (pwsh 7 / Node / Python are all
  installs). The conduit MAY prefer `pwsh` when present; the script targets the
  5.1 subset regardless.
- Disciplines, each mapped to a live bug class:
  1. A real `-File` script, never embedded one-liners — deletes the JSON-in-JSON
     escaping layer where both phase-13 bugs lived.
  2. Arbitrary text reaches atv via stdin (`--flag -`), never argv — native-exe
     argument quoting under PS 5.1 is unreliable for quotes/newlines.
  3. Explicit UTF-8 at both ends (stdin read through a UTF-8 StreamReader,
     `$OutputEncoding` pinned) — the shipped one-liners have a latent mojibake
     bug for non-ASCII payloads.
  4. Never re-serialize payload fragments (mapping rule 6's translator-side
     face).
  Disciplines 2 and 4 are contract-level rules for ANY translator tech; 1 and 3
  are their PowerShell instances.

### The engine absorbs projection legality
The shipped artifact chains `state running` ahead of every `step` to escape the
ERGO-10 (SequenceOfSteps, NeedsAttention, no-question) trap. In the v2 model that
transition is the engine's: `activity` against a Blocked card drops the question
and re-enters Working as part of the verb's claim semantics. Translators never
compensate for safe-combination rules.

### The S1-walk verb-claim semantics (RATIFIED 2026-07-11, this leg)
- **Card creation on first write; no session-start verb.** The first semantic verb
  upserts the card; every upserting verb accepts the identity flags
  (`--title`/`--subtitle`/`--icon`), and the stateless translator always passes
  them (every payload carries `cwd`; ERGO-25, "`start` on an already-live handle",
  makes re-application idempotent). Amends the model: the session has a card
  **from its first turn onward** — "fresh" drops out of Idle's sub-cases.
  `SessionStart` maps to nothing, so its source-split problem (resume / clear /
  compact) dissolves; optional row: `source=compact` → `activity --kind
  compacting` ("Compacting conversation").
- **Blocked clears by same-locus attribution.** `blocked` takes an optional
  `--agent <id>`; the engine records the blocked-on locus and clears on the next
  event attributed to it (its next activity, its `agent-stopped`, or a turn-end
  `ready`/`broken`). Denial is covered — the denied model continues inside the
  same locus. Parent-level blocks clear only on parent-attributed events, so
  worker chatter leaves a main-thread prompt standing. Depends on empirical
  item 2 (does `Notification` carry `agent_id`); degraded fallback = any activity
  clears. Concurrent blocks: display the latest question; when its locus
  progresses, surface the other. (A permission-DECISION event is confirmed
  unavailable — attribution is the mechanism.)
- **Idempotent claims.** An absent optional flag makes no claim (never clears a
  field); re-asserting an already-held state never restarts its clocks (only a
  transition INTO Ready starts the decay clock). Duplicate done-signals
  (`Stop` + `idle_prompt`) are expected and harmless; `idle_prompt` stays mapped
  because it is plausibly the only done-signal after a user interrupt.
- **Kinds name the mechanism, never the purpose.** The kind list is small and
  closed (read / edit / write / search / shell / fetch / …) and lives in the
  engine with its kind→verb-word rendering; purpose lives in the label ("Running
  dotnet build", never "Compiling" — intent classification of shell commands is
  an infinite problem at the wrong layer). Unmapped tools fall back to
  name-plus-label rendering, so the engine list never gates a new tool; a user
  map row upgrades the wording.

### The S2-walk findings (text torture; RATIFIED 2026-07-11)
Scenario: multi-line unicode prompt with quotes → goal; unmapped MCP tool
(`mcp__jira__create_ticket`); multi-paragraph `last_assistant_message` → summary.
The stdin discipline survives by construction (payload text never touches a
command line); the walk surfaced these contract details:
1. atv's stdin text is **UTF-8, read to EOF, trailing whitespace trimmed** (a
   PowerShell pipe appends a newline).
2. **One shared engine normalizer** for every single-line rendering
   (goal/question/summary/label): collapse whitespace runs → strip light
   markdown decorations (`**`, backticks, `#`) → truncate with ellipsis per
   field budget. (Stripping accepted despite the added normalizer complexity —
   operator, 2026-07-11.)
3. **One free-text value per verb call** (the `-` flag) confirmed sufficient
   across the whole verb set; short host-constrained tokens (handles, dir-leaf
   subtitles, kind/reason tokens) ride argv safely (Windows forbids `"` in
   paths).
4. **Unmapped-tool fallback:** translator sends `--kind tool --label
   <tool_name>`; the engine renders name+label, prettifying the MCP
   `mcp__<server>__<tool>` pattern (an MCP-wide convention, so engine-side is
   fair). Degradation: tool shown without its subject until a map row is added.
5. Large `PostToolUse` payloads (`tool_response` can be big) make PS 5.1
   parse slowly per event — accepted; async hooks absorb it.

### Resolution — all residuals closed or homed (2026-07-13)
- ~~The verb contract~~ — **DONE.** ERGO-31 ("The v2 semantic verb contract") is DECIDED
  (2026-07-13) and supersedes ERGO-27 ("The consolidated v1 command surface"); the ratified
  semantics above were its fixed inputs.
- **The per-host translator mapping tables** — Claude Code's is grounded + validated (its
  capture, `docs/host-events/claude-code.md`, answered empirical items 2 & 3; ERGO-31
  §2/§3/§4 + the capture fully determine the routing), so authoring the concrete
  `map.json`/`translate.ps1` is **build work** for the v2-engine + Claude-Code-translator
  plan phase, not an open decision. The other hosts' tables **defer with INFRA-31**
  (deferred-until-testable) and follow the INFRA-32 onboarding recipe.
- **Empirical items** — 1, 2, 3 all **ANSWERED**; item 4 **defers with INFRA-31**.
- **v2 build note:** fan-out child cards must reuse the parent session's exact `IconUri`
  (not `IconService.Place`, which mints per-handle paths and defeats glomming) — captured
  in `requirements.md`.
