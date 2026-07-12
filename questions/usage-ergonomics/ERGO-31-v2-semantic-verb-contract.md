# ERGO-31: The v2 semantic verb contract (the engine's public integration API)
**Status:** OPEN

## Question
Produce the concrete contract for the v2 semantic verbs that LIFE-24 ("The host-event →
task-state integration semantics") made the load-bearing artifact: the verb set, every
argument and flag, the canonical kind vocabulary, the reason vocabularies, the stdin
text-passing rules, and the engine-side claim semantics — precisely enough that a
translator author (first- or third-party) can target it from the contract doc alone.
Operator expectation (2026-07-11): when answered, this **supersedes ERGO-27** ("The
consolidated v1 command surface") — the supersession terms are part of the answer.

## Fixed inputs (ratified in LIFE-24, 2026-07-11 — not up for re-litigation)
- The five-state model and its AppTaskInfo projections; verbs are idempotent claims
  (an absent flag makes no claim; re-asserting a held state never restarts clocks); the
  engine owns projection legality (e.g. `activity` against Blocked drops the question
  and re-enters Working).
- The verb sketch: `working --goal`, `activity --kind <canonical> --label <raw>
  [--agent <id> --name <n>]`, `blocked --question [--agent <id>]`, `ready --summary`,
  `broken --reason`, `agent-started`/`agent-stopped`, `session-ended --reason`.
- No session-start verb: the first semantic verb upserts the card; every upserting verb
  accepts the identity flags (`--title`/`--subtitle`/`--icon`), which the stateless
  translator passes on every call.
- Blocked clears by same-locus attribution (degraded any-activity fallback; concurrent
  blocks: the latest question displays, others surface as their loci progress).
- Kinds name the mechanism, never the purpose; the kind→verb-word rendering table lives
  in the engine; unmapped tools fall back to `--kind tool --label <tool_name>` with
  engine-side prettification of MCP's `mcp__<server>__<tool>` pattern.
- Text passing: at most ONE free-text value per call, via a `-` flag value read from
  stdin (UTF-8, to EOF, trailing whitespace trimmed); short host-constrained tokens
  (handles, dir-leaf subtitles, kind/reason tokens) ride argv. One shared normalizer for
  every single-line rendering: collapse whitespace → strip light markdown decoration
  (`**`, backticks, `#`) → truncate with ellipsis per field budget.

## To decide
1. The exact per-verb signature table (positional `<handle>` per ERGO-6/ERGO-27 C3,
   flags, which flag may take `-`, defaults) and each verb's claim as a normative
   transition table: from-state × verb → to-state, plus clock effects (Ready decay
   start, presence gating).
2. The canonical kind list (closed, roughly a dozen mechanisms) and each kind's
   rendered verb word.
3. The reason vocabularies: `session-ended --reason` (host value-maps project onto it)
   and `broken --reason`.
4. The supersession terms for ERGO-27: which v1 lifecycle verbs survive, alias, or
   retire (`start`/`step`/`state`/`attention`/`done`/`fail`/`remove`); the data/util
   verbs (`list`/`run`/`doctor`/`clear`) and global flags presumably carry forward;
   what the translator-facing contract doc is and where it lives.
5. Fan-out interplay: engine-minted child-card handles vs `list`/`remove` addressing;
   `agent-started` degraded forms for name-only hosts (LIFE-24 mapping rule 5).

Spawned from LIFE-24's conduit/translator drill-down (2026-07-11). The per-host
translator mapping tables and the open empirical items stay with LIFE-24 / INFRA-23
("The host-event behavior recorder").
