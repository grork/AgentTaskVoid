# ERGO-33: A built-in default title (folder/repo name) when no `.atv.json` provides one
**Status:** OPEN
**Plan:** phase-18 (surfaced during the phase-18 live dogfood, 2026-07-14)

## Question
When no `.atv.json` supplies `title-template` (and no caller `--title`), should the engine's
own built-in default fall back to something derived from the resolved `--cwd`/repo anchor --
e.g. the last path segment of the anchor directory -- instead of the empty title it produces
today?

## Why this surfaced
Operator, 2026-07-14, live-dogfooding phase 18's Claude Code plugin (AC5): every card created
in a repo with no `.atv.json` renders with an empty title (documented, deliberate phase-17/18
trade-off -- "a repo with no `.atv.json` therefore gets an empty-titled card by design", per
`integrations/claude-code/README.md`). Live, this reads as unpolished -- a blank-titled taskbar
card is a worse default than deriving *something* recognizable from the working directory the
session is anchored to.

## What makes it non-trivial (constraints)
- This is the ENGINE's built-in default (the last stop in `--flag > env > repo > user >
  default`), not a repo-config value -- it would apply to every caller with no title anywhere
  in the chain, not just Claude-Code-plugin-originated cards.
- Only meaningful when a `--cwd` (or process cwd) anchor actually resolves to something --
  direct human CLI use with no repo context has nothing to derive a folder name from beyond the
  bare process cwd, which may not be a repo at all (e.g. running atv from `C:\Users\you\Downloads`).
- Interacts with ERGO-30's `{repo}` token -- `{repo}` already means "the discovered repo
  directory's name." A built-in default might reuse that exact resolution (last path segment at
  the `.git` boundary, or nearest anchor if no `.git`) rather than inventing a second
  folder-name rule.
- Needs a decision on scope: literally the last path segment of the anchor dir (no
  git-awareness), or specifically the git-repo-root folder name (falling back to the plain
  anchor folder name if no `.git` is found)?

## Options to explore later (NOT deciding now)
1. Built-in default title = last path segment of the resolved anchor (`--cwd` if present, else
   process cwd) -- always available, simplest, no git dependency.
2. Built-in default title = the `{repo}` token's own resolution (git-root folder name), empty
   if no `.git` boundary is found -- consistent with ERGO-30's existing template semantics, but
   doesn't help the no-`.git` case.
3. Status quo: empty title without a `.atv.json`; polish stays opt-in per repo.

## Scope note
Filed OPEN (operator, 2026-07-14, phase-18 live dogfood). Does not change the current build.
Related: ERGO-30 (repo-scoped defaults / `{repo}` token), ERGO-26 (config precedence chain
terminates in a built-in default).
