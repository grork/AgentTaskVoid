# ERGO-30: A repo-scoped defaults file the tool auto-discovers (icons, titles, glomming)
**Status:** OPEN

## Question
Given how **basic** each hook invocation is (a hook line calls `atv start <id> --title … --icon
…` with hard-coded strings), should `atv` auto-discover a **project/repo-local config file**
that supplies defaults — icon, title (or a title template), grouping/glomming behaviour — so
that the hook line stays minimal and each repo automatically gets its own branding/behaviour
without editing the shared hook config per project?

## Why this surfaced
Operator, 2026-07-10, from the phase-13 Claude Code integration: the shipped hook is one
generic settings fragment installed once (often user-wide), so every repo currently produces
an identically-titled/iconed "Claude Code" card. A per-repo defaults file the tool picks up
from the working directory would let each project brand its own cards (icon, title, whether its
sessions glom together) with zero change to the installed hook — the hook passes the session id;
the repo supplies the rest.

## What makes it non-trivial (constraints)
- **This is a DIFFERENT config scope than the existing one.** ERGO-26 ("Config file location
  and format") put the tool's config as a flat string→string `atv-config.json` in the package
  `LocalState` (user/machine scope); ERGO-17 ("Configuration surface for recurring defaults")
  is that same recurring-defaults surface. A repo-scoped file is a NEW layer that must slot
  into the precedence chain (today `--flag > env > user-file > built-in default`) — presumably
  `--flag > env > repo-file > user-file > default`, decided per key.
- **Discovery mechanics:** walk up from the working directory to a repo root / a `.atv`
  marker? Hooks run with a known cwd (the host passes `cwd` in its payload), so cwd-anchored
  discovery is viable — but it must be cheap (every invocation is a fresh short-lived process)
  and well-defined (which ancestor wins, symlinks, monorepos).
- **Which keys are repo-appropriate vs must stay user/machine.** Presentation defaults (icon,
  title template, glomming/grouping intent per ERGO-14/15) make sense repo-scoped; operational
  knobs (idle periods LIFE-22, log rotation, watchdog interval) probably must NOT be settable
  by an arbitrary checked-out repo.
- **Trust surface.** Auto-reading config from any directory you happen to run in is a (mild)
  trust concern — a repo-supplied config that sets, say, an arbitrary `RawPath`/exe icon
  (ERGO-29) or a deep-link is executing repo-authored intent. Mirrors the folder-trust prompt
  we hit driving the phase-13 dogfood. May need an allowlist of repo-settable keys and/or a
  trust gate.
- **Identity is still global** (ERGO-16): a repo config changes presentation, not the shared
  task namespace — `list`/`clear` stay identity-global regardless.

## Options to explore later (NOT deciding now)
1. Discover a repo-local file (e.g. `.atv.json` / `atv.config.json`) by walking up from cwd,
   layered directly above the user config; restrict it to a presentation-only key allowlist
   (title/icon/glom).
2. No auto-discovery; an explicit `--config <path>` (or `ATV_CONFIG` env) the hook/wrapper
   passes — safer, but pushes per-repo wiring back onto the caller.
3. A title/icon *template* mechanism (e.g. `--title "{repo}: {branch}"`) sourced from the repo
   file, so cards self-describe per project.
4. Status quo: single user/global config (ERGO-26); repos differentiate only via explicit hook
   flags.

## Scope note
Filed OPEN (operator, 2026-07-10); does not change the current build. Related: ERGO-17/ERGO-26
(existing config surface + location/format), ERGO-14/15 (grouping intent/defaults), ERGO-16
(identity-global consumers), ERGO-29 (a repo could supply the brand icon), ERGO-7 (CLI
persistent state). Also feeds the "basic hook" concern behind LIFE-25.
