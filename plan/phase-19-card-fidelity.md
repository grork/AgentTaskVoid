# Phase 19: Card fidelity — subagent activity routing + the never-blank title chain

**Status:** NOT STARTED — filed 2026-07-14/15 from the phase-18 live dogfood; widened
2026-07-15 to carry ERGO-33 as well.
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
- ERGO-32's raw card-control tier; the deferred Copilot CLI/Codex legs (INFRA-31).
