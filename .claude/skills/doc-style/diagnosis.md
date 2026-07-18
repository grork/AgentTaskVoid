# Why these rules exist

Written 2026-07-18, after four operator review rounds on `docs/release.md`
and `integrations/claude-code/README.md` kept finding new prose problems
that the previous round's rule hadn't covered.

The four rounds were one problem. The docs were born as phase completion
reports addressed to an evaluator — release.md's section 3 was headed
"(DIST-8, AC2-AC5)", and the Claude Code README had a "Capture staleness
(AC3)" section addressed "to the orchestrator" and a section titled
"Verified live vs. verified-against-docs vs. verified-offline". A report to
an evaluator exhibits evidence (round 1's verification tiers), narrates
what changed (round 1's supersession history), sells and defends choices
(round 2's punchlines and "not a bug"), proves every implication and stands
self-contained (round 3's tautologies and inlined lists), and sounds
rigorous (round 4's vocabulary and passive voice). Each round found one
face of that stance.

The operator's hypothesis — performing competence for a skeptical reader
instead of informing a trusting one — was the core, with two refinements.
The skeptical reader was real (the phase process's evaluator), and the
generator is not only in the inherited text: reporting-to-a-supervisor is a
model session's default voice, so fresh sentences reintroduce it. That is
why a pattern list keeps growing — each round bans surface forms, the
stance keeps emitting new ones (bold was banned; requirements.md did the
same thing in CAPS). Passive voice and duplicate enumeration are the same
habit: committee-report voice, and the report's need to be self-contained
where a manual can point.

A fifth round proved the stance can hide inside the rules themselves: rule
4's original keep-list protected "established project vocabulary", and that
exception sheltered exactly the worst offenders — "locus", "Projection
legality", "altitude" — invented academic coinages that were established
only in the sense that the repo used them everywhere. The fix was to split
vocabulary into standard engineering terms (protected) and project
coinages (fail the speech check regardless of consistency).

Memory-note reconciliation (2026-07-18): the guidance that lived in the
`avoid-ai-tell-prose-in-docs` and `human-facing-docs-vs-history-of-record`
memory notes collapsed into this skill. The file-classification map
(which repo files are history-of-record vs. reference) stays in
`human-facing-docs-vs-history-of-record`.
