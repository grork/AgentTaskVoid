# Phase 19: Card fidelity — subagent activity routing + the never-blank title chain

**Status:** 19A + 19B + Part C (19D) + Part D DONE (committed). 19C (AC11 live dogfood) in
progress — two live-dogfood re-runs each surfaced a further defect beyond the original Part
A/B scope (Part C: a premature `ready` mid-fan-out; Part D: a cancelled subagent's card/parent
state never clean up), both found, diagnosed, and fixed within this same phase per the
operator's explicit ruling that live-dogfood findings against AC11's own text are phase-19
scope, not a deferred concern. AC11 must be re-run once more, covering the cancellation
scenario, before the phase can be signed off.
**Depends on:** phase 15 (ERGO-31 §5's fan-out addressing, `EngineMemory.CardedAgentLoci`),
phase 17 (the `--cwd` anchor + `ApplyRepoDefaults` chain ERGO-33 terminates), phase 18 (the
Claude Code translator that surfaced both gaps live).
**Unblocks:** nothing downstream; makes phase 18's plugin (and any future host translator
built the same way) render subagent activity and card titles correctly.

## Goal

Two independent defects, both surfaced by the same phase-18 live dogfood, both "the card
shows the wrong information," and both validated by the same live scenario — a real fan-out
session in a repo with no `.atv.json`:

- **Part A:** a carded subagent's own activity renders on the PARENT card instead of its own
  child card. ERGO-31 §5's addressing rule is decided but unimplemented.
- **Part B (ERGO-33):** a card with no title anywhere in the chain renders blank.

They share no code (`ClaimActivity` on the hot path; `ApplyRepoDefaults` on the create
branch) and neither depends on the other. They are one phase because they share a
**validation cost**: one supervised build-and-dogfood cycle rather than two. Part A first —
it is the harder design and the phase's original identity.

## Execution structure (read this first)

Three passes, serial, matching the 14A/14B and 15A/15B pattern already in `progress.md`:

| Sub | Scope | Who |
|-----|-------|-----|
| **19A** | Part A — the redirect + its regression and baseline tests (AC1–7) | executor + reviewer subagents |
| **19B** | Part B — ERGO-33's engine and translator halves (AC8–10) | executor + reviewer subagents |
| **19C** | AC11 — the live dogfood, covering both parts at once | **orchestrator + operator, supervised** |

19A and 19B are independent (no shared code, no ordering dependency) and get **one commit
each after their own sign-off**. They are split rather than run as one pass because Part A
changes `ApplyClaim`'s architecture while Part B is mechanical — separate review surfaces,
and a Part A halt does not entangle Part B.

**19C cannot be delegated to a subagent.** AC1–AC10 are fully automated and are the entire
subagent scope; AC11 needs the real platform, a real Claude Code session, and a human
eyeball. When 19B is signed off, the automated loop **stops and hands back to the operator**
rather than attempting AC11. This is the phase-12/13/14/18 pattern, and phase 18's
orchestration note (`progress.md`, phase-18 section) records the constraints that apply
verbatim here: **never** touch `~/.claude/settings.json` or this repo's
`.claude/settings.local.json`, never launch a real `claude`/`claude -p` session from a
subagent, never fire a real hook, exact-PID-only process handling, and no raw Ctrl+C. Those
last two are not hygiene — both mechanisms have crashed a Claude Code session in this project
before.

---

# Part A — Route a carded subagent's `activity` to its child card

## Root cause (found live, phase-18 AC5 dogfood, 2026-07-14/15)

`integrations/claude-code/plugins/atv-integration/translate.ps1` already does the right
thing: on a subagent's own `PreToolUse`/`PostToolUse` it calls `atv activity <session>
--kind <k> --label - --agent <agentId> [--name <n>]` — exactly the call shape
`docs/integration-api.md`'s verb table documents, and already covered by a passing test
(`ClaudeCodeTranslatorTests.PreToolUse_SubagentScoped_CarriesAgentAndNameFlags`). **The
information is already being passed correctly.**

But `SemanticEngine.ClaimActivity` (`src/Atv/Semantics/SemanticEngine.cs:151-159`) only ever
uses `agentId` for one thing — clearing that locus's pending block (`RemoveLocus(ctx.Memory.
BlockedLoci, agentId)`, §5.1's same-locus-clearing rule). It never checks whether `agentId`
is already a carded child (`ctx.Memory.CardedAgentLoci.Contains(agentId)`) to redirect the
content claim to that child's own card. Every subagent tool-call activity line lands on the
**parent's** `executing` step, regardless of which agent produced it.

**Not a translator bug.** `translate.ps1` is deliberately stateless (prior decision — no
local bookkeeping in the translator), and it does not need to be: `EngineMemory.
CardedAgentLoci` already lives on the parent handle's sidecar entry and already answers "does
this agent id have a minted child card right now?" precisely. Duplicating that bookkeeping in
a translator-owned state file would drift against the engine's own source of truth. The fix
belongs entirely in `atv`'s claim-processing path.

**Why the translator cannot just address the child handle itself** (as §5's prose suggests a
translator "should"): the child handle is deterministic, but whether the card *exists* is
not — minting happens at the 2nd concurrent `agent-started`, engine-side. A stateless
translator cannot know whether a given `agentId` is carded yet, so it must address the parent
with `--agent` and let the engine route. That is what the shipped plugin does, and §5's prose
needs to say so.

## Observed symptom (live)

A real 2-subagent fan-out (`26ccd262-...` parent, two `general-purpose` children): both child
cards sat at `"Not started yet."` for their entire life — every one of their real tool calls
(`Bash`/`Glob`/`Read`, confirmed via a `claude --debug-file` capture) rendered as the
**parent's** activity line, including one capture where the leaked text was recognizably an
internal task-completion-notification payload (its `task-id` matched a live child agent id).
Full timeline (mint/retire correct, only content-routing wrong) is in `progress.md`'s
phase-18 write-up.

## Why the tests never caught it (the coverage gap this phase closes)

`SemanticEngineFanOutTests` has 18 tests and **zero occurrences of `Activity`**. The suite
covers mint, retire, cascade, and refusal exhaustively — the card's *lifecycle* — but never
asserts that any content ever lands on a child card after creation. Every one of the 18 tests
passes while a child sits at `"Not started yet."` forever, which is precisely the live
symptom.

This is a **seam** gap, not an oversight on one side. The translator suite proves the right
call shape goes out; the engine suite proves the child card gets created. Each half was
tested to its own boundary, and the defect lived in the space between: what the engine does
with the shape the translator is proven to send. Part A's test obligations therefore split
into *regression* tests (the redirect) and *baseline* tests (child-card content at all —
coverage that should have existed since phase 15 and never did).

## Decisions implemented

ERGO-31 §5 ("The v2 semantic verb contract") and its §5.1 already decide the behavior; this
phase implements it. The four points the original phase file flagged as open resolve from the
decided text, ratified by the operator 2026-07-15:

1. **Only `activity` redirects.** Per the verb table, only `activity` and `blocked` accept
   `--agent`. `blocked` is parent-targeted by design (§5's "Working/Completed only" — a child
   can never legally be `NeedsAttention`), so `blocked --agent` keeps its current
   locus-bookkeeping-only behavior.
2. **Redirection is full substitution of content.** §5 says a carded subagent's activity
   should land on the child "not the parent." Had the translator addressed
   `<session>#<agentId>` directly, as §5's prose describes, the parent's content would be
   untouched — so the engine redirect must make the parent-addressed form **exactly
   equivalent to the child-addressed call**. The parent's own `executing`/`completed` steps
   are left alone.
3. **The parent-side locus bookkeeping still runs on the parent**, regardless of where the
   content lands. It is orthogonal: §5.1 clears a block on "that locus's own next `activity`
   call," which is a statement about attribution, not about which card renders the line. A
   carded agent's activity must still clear that agent's pending block on the parent.
4. **An uncarded `agentId` falls back to the addressed handle** (the parent) — a lone
   not-yet-2nd-concurrent worker, an already-retired agent, or a translator-side unknown id.
   This extends §5's existing "degraded fallback" language to the uncarded case. A late
   activity from a retired agent must never resurrect its child card.
5. **A redirected claim goes through the exact same `ApplyClaim` path** a direct `atv activity
   <child-handle>` call would, so every child-card invariant holds unchanged: the
   byte-identical icon-URI-reuse rule (§5's glom key), the Working/Completed-only reachable
   range, and the child's own `ready`-decay/removal lifecycle (phase 15B). A content-only
   update must never re-mint or re-place an icon.

**No new question filed.** ERGO-31 is DECIDED and permanently stamped `phase-15`; this is
remediation of an incompletely-implemented decision (a `progress.md` concern), not new scope.

## Design note for the implementer

This redirects a claim to a different handle than the one it was invoked against — new
territory for `ApplyClaim`, which has always resolved exactly one handle per call. The
redirect must resolve the child's own live view and sidecar entry (its own `EngineMemory`,
its own step history), not compute against the parent's `ClaimContext` and write elsewhere.
The parent's memory still needs its `BlockedLoci` update written (point 3), so a redirected
`activity` touches **two** sidecar entries under the one write mutex (invariant #5 — the
mutex is already held across the whole read-modify-write, so this is a shaping problem, not
a locking one).

---

# Part C — a premature `ready` mid-fan-out (found during AC11's own live dogfood)

## Root cause (found live, phase-19 AC11 dogfood, 2026-07-15)

Claude Code's own top-level turn ends (`Stop` fires) as soon as it dispatches Task-tool
subagent calls — it does NOT wait for the subagents to finish. Confirmed directly from a
`claude --debug-file` capture: `[engine] turn 2 end (turns=3 ... stop=end_turn)` at
`16:51:18.742`, only ~10s after the Task dispatch and 3–24s *before* either child's own
`SubagentStop`. `translate.ps1`'s mapping (`Stop -> ready <sid> --summary -`) is unconditional,
so the PARENT card is claimed into `Completed` while its children are still demonstrably
running.

**Why this was invisible before Part A, and why AC11's dogfood is what surfaced it:**
`ProjectAfterLocusChange` (`SemanticEngine.cs:1042-1055`) makes ANY `activity` claim that lands
on a card unconditionally re-project it to `Running`, regardless of the card's prior state —
by design, this is how a card recovers out of Blocked. Before Part A, an uncarded/lone
subagent's activity (and, pre-Part-A, even a CARDED subagent's activity) kept landing on the
parent, so the premature `Completed` state was masked by a flicker: `ready` fires, then the
very next tool-call activity line flips it straight back to `Running`, often within a second.
Confirmed live in a single-subagent trial (`atv-dogfood-p19-2-debug.log`): `Stop` fires at
`17:12:12.400`, ~4s after dispatch, but the lone (uncarded) subagent's own Glob/Read activity
keeps landing on the parent every 1-3s and self-heals it back to `Running` every time — the
operator never observed a stuck `Completed` state in that trial. **Part A's own redirect (by
design) removes that accidental self-healing**: once concurrency hits 2 and both children are
carded, EVERY subsequent activity claim is redirected to the children (Part A's whole point),
so nothing is left to land on the parent and flip it back. The premature `Completed` state now
sticks for the entire fan-out — confirmed live via `atv list --json`
(`atv-dogfood-p19-debug.log` trial): parent `state:"completed"` while both children show
`state:"running"` with real, distinct content.

This is a regression Part A's own change exposes at the parent-card level, even though every
one of Part A's own AC1-7 tests pass (none of them modeled a `ready` claim arriving mid-fanout —
a timing-dependent interaction only a live dogfood surfaces). Ratified in-session
2026-07-15 (operator correction: "All of these issues are phase-19 scope" — the LIFE-24
"separate deferred concern" framing this was initially given was wrong; AC11's own text,
"the parent shows only its own activity," is what this violates).

## Decided fix (ratified 2026-07-15)

`ClaimReady` (`SemanticEngine.cs:301-318`, called from `Ready`, `SemanticEngine.cs:298-299`)
structurally refuses the `Completed` transition when the addressed handle currently has any
active agent locus — `ctx.Memory.ActiveAgentLoci.Count > 0` (the FULL active set, not
`CardedAgentLoci` — a lone, not-yet-carded subagent's activity is *designed* to land on the
parent per Part A's own decision point 4, so the parent legitimately still has real, delegated
work outstanding even below the 2-concurrent carding threshold). A refused claim is a true
no-op: live state/content untouched entirely, mirroring the existing `refuseIfChild` structural
pattern already used by `Blocked`/`Broken` in `ApplyClaimCore` (`SemanticEngine.cs:594-614`) —
add a parallel `refuseIfActiveChildren` condition alongside it (checked against the ADDRESSED
handle's OWN `EngineMemory.ActiveAgentLoci`, resolved from `entry` exactly like the existing
check resolves `entry?.EngineMemory?.ParentHandle`), not a bespoke check duplicated elsewhere.

**Scope: `Ready` only.** `Broken`/`StopFailure` is architecturally similar (also a turn-end
event, per its own doc comment, also clears every pending blocked locus) but was never observed
hitting this in either dogfood trial. Operator decision (2026-07-15): leave `Broken` untouched —
do not speculatively extend the refusal there.

**Why this does not lose the turn summary (verified against the live capture, not assumed):**
`Stop` fires once per top-level turn, and Claude Code's own engine produces a NEW top-level turn
once the parent actually processes the subagents' results — confirmed directly in the capture:
turn 3 starts at `16:51:47.125`, right after both children's `SubagentStop`
(`16:51:21.890`/`16:51:42.524`), ending in its own `Stop` at `16:51:52.242`; turn 4 ends with
another `Stop` at `16:52:10.612` carrying a substantial synthesis (`resultLen=1556`). By the time
turn 4's `Stop` fires, `ActiveAgentLoci` is genuinely empty, so under this fix that claim
succeeds normally with the real, accurate summary — the premature turn-2 `Stop` is the only one
refused. The lifecycle becomes: Working throughout the fan-out, then genuinely `Completed` with
an accurate summary once the parent has actually finished synthesizing the results — not merely
harmless, but the *correct* sequence.

## Files affected (Part C)

```
src/Atv/Semantics/SemanticEngine.cs   # ApplyClaimCore: new refuseIfActiveChildren condition
                                       # Ready/ClaimReady: pass it, mirroring refuseIfChild
tests/Atv.LogicTests/Semantics/SemanticEngineFanOutTests.cs  # or a sibling file, matching 19A's
                                                               # SemanticEngineActivityRedirectTests.cs precedent
```

## Acceptance criteria (Part C, automated)

12. **Refusal while active:** with `agent-started` registered for at least one agent (single,
    uncarded worker OR ≥2 carded fan-out), a `ready` claim (bare or with `--summary`) against the
    parent is refused — the card's live state AND content are byte-unchanged, matching the
    existing `refuseIfChild` refusal shape (`OutcomeKind.RefusedInvalidArgument`, or whatever
    outcome kind the existing structural refusals use — match it, don't invent a new one).
13. **Recovery once clear:** once every active agent has `agent-stopped` (`ActiveAgentLoci`
    empty again), a subsequent `ready` claim succeeds normally — Completed, with whatever
    summary/content it carries.
14. **Single-worker case covered:** the refusal fires for a lone, never-carded worker too (gated
    on `ActiveAgentLoci`, not `CardedAgentLoci`) — a regression test for the exact single-agent
    live-dogfood scenario (`ready` arriving while one uncarded subagent is still active).
15. **`Broken` untouched:** a locking test confirms `ClaimBroken`/`Broken()` does NOT gain the
    same refusal — out of scope by the ratified decision above.
16. **No regression:** `blocked`/`agent-started`/`agent-stopped`/`activity` behavior (all of
    Part A's AC1-7, and the ordinary non-fan-out `ready` path) unchanged.

AC11 (the live dogfood) must be RE-RUN after this fix lands, covering both the single- and
multi-subagent cases this finding came from, before phase 19 can be signed off as complete.

---

# Part B — ERGO-33: the never-blank title/subtitle chain

## Decisions implemented

ERGO-33 ("The card title/subtitle chain, ending in a never-blank default"), DECIDED
2026-07-14. It adds **no new machinery** — it terminates ERGO-26's existing precedence chain
with a real default instead of `""`.

### Engine half — the built-in default

- **Slot:** `SemanticEngine.ApplyRepoDefaults` (`src/Atv/Semantics/SemanticEngine.cs:673`),
  where `titleRaw is null` currently yields `""` (line 683-684). Create-branch only, like
  ERGO-30's discovery — no hot-path cost.
- **Everything needed is already computed.** `RepoDiscoveryResult`
  (`src/Atv/Config/RepoSettings.cs:39`) already carries `AnchorPath`, `RepoRootDir`,
  `RepoName`, and `Branch`. No new filesystem walk, no new git read.
- **Default title = `<anchor-folder>`**, or **`<anchor-folder> (<repo-folder>)`** when the
  anchor sits below the discovered `.git` root. The parenthetical is suppressed when the two
  names are equal, so a card is never titled `AppTaskInfoCli (AppTaskInfoCli)`:

  | anchor | `.git` root | title |
  |---|---|---|
  | `C:\Source\AppTaskInfoCli` | same | `AppTaskInfoCli` |
  | `C:\src\monorepo\packages\web` | `C:\src\monorepo` | `web (monorepo)` |
  | `C:\Users\dhopt\Downloads` | none | `Downloads` |

- **Default subtitle = `{branch}`** when a `.git` root resolves, else empty.
- **Floor:** an anchor with no last path segment (a drive root, `C:\`) falls back to the
  brand name from `Branding` (invariant #2 — derive it, never re-literal it).
- **Empty expansion falls through:** a layer that supplies a template expanding to empty
  (e.g. `.atv.json` sets `"{repo}"` in a non-git directory, ERGO-30's token-drop rule yields
  `""`) lands on the built-in default. Empty is never a final title.
- **Unchanged:** `{repo}` still means the git root's folder name; an explicit caller `--title`
  is still verbatim and never templated.

### Translator half — the chain's top

`translate.ps1` forwards Claude Code's **`session_title`** as `--title` **only when
present**, on `UserPromptSubmit` alone (the event that creates the card, so no translator
state is needed). It is the **user-assigned** name only — absent unless the user explicitly
named the session — which is why forwarding it does not re-open
`integrations/claude-code/README.md`'s "Deliberately no identity flags" concern: that section
argues against a hard-coded **constant**, and a value present only on explicit user intent is
exactly what the chain's first stop is for. Follow the existing `Get-CwdArgs` helper pattern.
No `hooks.json` change — the translator already parses the whole payload from stdin.

**Verification posture (ERGO-33, operator 2026-07-14):** the `session_title` field was read
off the shipped binary (v2.1.210), not a live capture. The build is explicitly **not** gated
on a re-capture — assume it is correct and prove it in AC11's dogfood. The risk is low by
construction: if `session_title` never arrives, no `--title` is passed and the chain falls
through to the repo/folder default, which is the decided behavior anyway.

### Two boundaries ERGO-33's text does not mention (ratified 2026-07-15)

- **Child cards are out of the chain, structurally.** `MintChildCard`
  (`src/Atv/Semantics/SemanticEngine.cs:360`) calls `_store.Create` directly with `title =
  name ?? agentId` and subtitle `""` — it never routes through `ApplyRepoDefaults`. Children
  therefore already satisfy "never blank" via their agent name, which is a **better** child
  title than the anchor folder would be (`general-purpose`, not `AppTaskInfoCli`). Leave this
  alone, and lock it with a test so a future implementer does not "helpfully" route children
  through the default chain. Their subtitle stays `""` for the same reason — a stated choice,
  not an omission.
- **The no-anchor path stays as-is.** `_discoverRepo is null` returns `""` today
  (`NoDiscoverRepoWired_DegradesToPrePhase17Behavior`). `CompositionRoot` wires it
  unconditionally (`src/Atv/Cli/CompositionRoot.cs:69`, falling back to
  `Environment.CurrentDirectory`), so this path is unreachable in the shipped CLI and "never
  blank" holds in production. The existing degradation test stands unchanged.

---

## Files affected

```
src/Atv/Semantics/SemanticEngine.cs            # A: ClaimActivity redirect + ApplyClaim shape
                                               # B: built-in title/subtitle default in ApplyRepoDefaults
src/Atv/Config/RepoSettings.cs                 # B: anchor-folder + parenthetical composition (if not engine-local)
tests/Atv.LogicTests/Semantics/SemanticEngineFanOutTests.cs        # A: regression + the missing baseline
tests/Atv.LogicTests/Semantics/SemanticEngineRepoDefaultsTests.cs  # B: default, floor, precedence terminus
tests/Atv.LogicTests/Integrations/ClaudeCodeTranslatorTests.cs     # B: session_title forwarding
integrations/claude-code/plugins/atv-integration/translate.ps1     # B: conditional --title on UserPromptSubmit
docs/integration-api.md                        # A: §5 states the engine-side redirect explicitly
integrations/claude-code/README.md             # B: correct the "empty-titled card by design" paragraph
docs/configuration.md                          # B: defaults table
```

**Not touched:** `translate.ps1`'s activity path — it already sends the correct call shape,
and Part A requires no translator change. (Part B *does* touch the file, on a different
event; this supersedes the original phase file's blanket "translator not touched" note.)

## Acceptance criteria (written first)

AC1–AC10 are automated and human-free: the logic suite plus the existing
`ClaudeCodeTranslatorHarness`, which already drives the real `translate.ps1` as a separate
`powershell.exe -File` process against a compiled stub `atv`. AC11 is the single supervised
step, following the phase-18 pattern.

### Part A — regression (the redirect)

1. **Redirect:** `activity <parent> --agent <carded-id>` lands its line on the **child's**
   `executing` step. The parent's own `executing`/`completed` steps are byte-unchanged.
2. **Equivalence:** the redirected call and a direct `activity <parent>#<agent-id>` call
   produce an identical child card — the decided rule's actual content (decision point 2).
3. **Locus bookkeeping survives the redirect:** with that agent's block pending on the
   parent, its `activity` still clears the parent's locus (the card re-projects to Working if
   it was the last one; stays Blocked showing the next-latest question if not) *while* the
   line lands on the child.
4. **Sibling isolation:** agent A's activity touches A's card only — B's card and the parent
   are untouched.
5. **Fallbacks:** an uncarded `agentId` (never carded, or already retired via
   `agent-stopped`) lands on the addressed parent handle, and a retired agent's late activity
   never resurrects its child card; `activity` with no `--agent` is unchanged; a name-only
   registration (`--name`, no `--agent`) still renders on the parent's stream.
6. **Invariants under redirect:** a redirected activity re-uses the child's icon URI
   byte-for-byte (ERGO-13 glom intact), never calls `IconService.Place` (counting/spy fake,
   the pattern already used in `SemanticEngineRepoDefaultsTests`), and passes the same
   validator path a direct child call takes.

### Part A — the missing baseline (should have existed since phase 15)

7. **Child-card content coverage**, absent from the fan-out suite entirely: a freshly minted
   child's content is the bare `"Not started yet."` baseline; a **direct** `activity
   <parent>#<agent-id>` call lands on that child (§5's own literal prescription, never
   tested); a child reaches Ready via its own `ready <child>` and returns to Working on
   further activity — its full Working/Completed range exercised with real content, not just
   state.

### Part B — the chain

8. **Built-in default, engine:** all three table rows above (anchor == repo → bare name;
   anchor below root → `web (monorepo)`; no `.git` → plain folder name); the equal-names
   parenthetical suppression; the `C:\` drive-root brand floor; a template expanding to empty
   falls through to the default; default subtitle is the branch with a git root and empty
   without; `{repo}` semantics and verbatim-`--title` behavior both unchanged.
9. **Precedence terminus:** the existing five-layer matrix extended — `--title > env > repo
   template > user file > built-in default` — proving the default is reached only when every
   layer above is absent, and never overrides a layer above it. Create-only gating re-proven:
   the default resolves on the create branch and the update path performs zero additional
   discovery (extend the existing counting-spy test). Child cards keep `name ?? agentId` as
   their title and `""` as their subtitle — the default chain never reaches them.
10. **Translator, offline:** `UserPromptSubmit` **with** `session_title` forwards `--title
    <value>`; **without** it, no `--title` token appears in argv at all; other events
    (`PreToolUse`, `Stop`, `SessionEnd`) never pass `--title`; a `session_title` carrying
    non-ASCII/quotes/newlines reaches the stub byte-intact (the existing UTF-8 torture
    pattern).

### Live

11. **LIVE dogfood (operator-supervised — the phase-18 pattern; not subagent-able):** one
    real session in a repo with **no** `.atv.json` drives both parts at once: a ≥2-subagent
    fan-out renders each child's own tool calls on its own child card while the parent shows
    only its own activity (Part A, against the real platform), and the card renders a
    non-blank title from the anchor folder plus the branch subtitle (Part B). Then, with the
    session explicitly named by the user, a **new** card takes `session_title` as its title —
    the one thing ERGO-33 deliberately left unproven offline. Suites green; NativeAOT publish
    clean.

## Out of scope

- Any `blocked --agent` redirect (decided parent-only, decision point 1).
- Translator-side fan-out bookkeeping (the whole point of Part A is that the engine already
  has the state).
- Extending the title/subtitle default chain to child cards (ratified out, above).
- Re-capturing Claude Code's event corpus to confirm `session_title` (explicitly not a build
  gate per ERGO-33's verification posture — AC11 covers it live).

---

# Part D — a cancelled subagent (`TaskStop`) never retires its card or unblocks the parent

## Root cause (found live, phase-19 AC11 re-run dogfood, 2026-07-15)

Operator scenario: ask for 3 parallel subagents each running a 10s-sleep script, then ask
Claude to terminate them before they finish. Two symptoms: one child card was orphaned
(never cleaned up), and the parent never reached `Completed`.

Confirmed directly from a `claude --debug-file` capture plus the matching session transcript
(`tool_use` entries): the operator's cancellation goes through Claude Code's real `TaskStop`
tool, `tool_input: {"task_id": "<agent-id>"}` — the same id format used everywhere else as
`agent_id`. Of the 3 targeted agents, 2 had already completed naturally by the time `TaskStop`
reached them (`"Task X is not running (status: completed)"`, a harmless validation no-op); the
third was genuinely cancelled (Claude Code's own internal log: `agent_completion ...
exitPath=cancelled`).

The decisive finding: **Claude Code never fires a `SubagentStop` hook for a cancelled agent** —
only for one that completes naturally. Confirmed by direct comparison in the same capture: the
naturally-completing sibling got its `SubagentStop` hook registered within 1ms of its own
completion event; the cancelled agent got no `SubagentStop` hook registered anywhere in the
rest of the session (an explicit enumeration of the full hook registry at that instant confirms
its absence, not just a missed grep).

`translate.ps1` only ever calls `atv agent-stopped` from the `SubagentStop` case, and
`TaskStop` wasn't in `map.json`'s `suppressedTools` or given any special handling, so it fell
through to the generic tool handler (noise: an `activity --kind tool --name TaskStop --label
<task_id>` claim landing on the parent, since `TaskStop` is invoked by the parent agent itself
— no top-level `agent_id` on the payload to redirect with). Two structural consequences,
both confirmed live:

1. **Orphaned child card** — only `agent-stopped` retires a child card; it was never called for
   the cancelled agent.
2. **Parent stuck forever, not just prematurely** — Part C's `refuseIfActiveChildren` guard
   (this same phase) refuses `ready` while the addressed handle's `ActiveAgentLoci` is
   non-empty. That's correct for the premature-mid-fanout case Part C targets, but it turns
   into a permanent deadlock here: the cancelled agent's locus never leaves `ActiveAgentLoci`
   because nothing ever calls `agent-stopped` for it, so `ready` is refused indefinitely, not
   just until the fan-out naturally finishes.

## Decided fix (ratified 2026-07-15)

Translator-only — no engine change. `ClaimAgentStopped`'s retire path and `ActiveAgentLoci`
bookkeeping are already correct and idempotent (confirmed by reading `SemanticEngine.cs`
directly): a redundant `agent-stopped` call for an already-retired or never-carded agent is a
documented clean no-op, and any `agent-stopped` call unconditionally removes that id from
`ActiveAgentLoci` regardless of carding state — exactly what both symptoms need.

`map.json` gains `taskStopTool: "TaskStop"` (mirrors the existing `planTool` precedent — a
single named-tool lookup, not a new sub-language). `translate.ps1`'s `PreToolUse`/`PostToolUse`
handler gains a new branch: on `PreToolUse` only, read `task_id` from `tool_input` and call
`atv agent-stopped <sid> --agent <task_id>` (mirroring the existing `SubagentStop` call shape
exactly); `PostToolUse:TaskStop` and an absent/empty `task_id` are both deliberate no-ops.
`TaskStop` never falls through to the generic tool-summary path either way. Pre-only was chosen
specifically so this never needs to parse `tool_response`/success shape — the stop intent alone
is sufficient justification, and the idempotent retire path already makes a redundant call
(the already-completed-naturally case, observed live) safe.

## Files affected (Part D)

```
integrations/claude-code/plugins/atv-integration/map.json       # new taskStopTool field
integrations/claude-code/plugins/atv-integration/translate.ps1  # new TaskStop branch, PreToolUse-only
tests/Atv.LogicTests/Integrations/ClaudeCodeTranslatorTests.cs  # 4 new tests
```

## Acceptance criteria (Part D, automated)

17. **Cancellation maps to agent-stopped:** `PreToolUse:TaskStop` with `tool_input.task_id`
    present produces exactly one `atv` call, `agent-stopped <sid> --agent <task_id>` (plus
    `--cwd` when a project dir is set), no stdin — never the generic activity path.
18. **No-ops:** `PostToolUse:TaskStop` (regardless of `tool_response`) and `PreToolUse:TaskStop`
    with an absent/empty `task_id` both produce zero `atv` calls.
19. **Never generic:** a `TaskStop` call never produces an `activity`/`--name TaskStop` line
    under any circumstance.

AC11 (the live dogfood) must be RE-RUN once more, this time including a subagent-cancellation
scenario (parallel subagents, cancelled before completion), before phase 19 can be signed off.
- ERGO-32's raw card-control tier; the deferred Copilot CLI/Codex legs (INFRA-31).
