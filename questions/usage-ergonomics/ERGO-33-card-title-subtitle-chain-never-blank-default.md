# ERGO-33: The card title/subtitle chain, ending in a never-blank default
**Status:** DECIDED (2026-07-14)
**Plan:** unplanned
**Decision:** A card never renders a blank title again. Title and subtitle resolve through
ERGO-26's ("Config file location and format") EXISTING precedence chain -- no new machinery --
terminating in a built-in default derived from the anchor the caller already passes:

```
--title  >  env  >  .atv.json title-template  >  user config  >  built-in default
   ^                                                                    ^
   host session name, forwarded by the                     anchor folder name
   translator only when present                            (+ repo in parens)
```

- **Built-in default title = `<anchor-folder>`**, or **`<anchor-folder> (<repo-folder>)`** when
  the anchor sits BELOW the discovered `.git` root. The parenthetical is suppressed when the two
  names are equal (the common case), so a card is never titled `AppTaskInfoCli (AppTaskInfoCli)`:

  | anchor | `.git` root | title |
  |---|---|---|
  | `C:\Source\AppTaskInfoCli` | same | `AppTaskInfoCli` |
  | `C:\src\monorepo\packages\web` | `C:\src\monorepo` | `web (monorepo)` |
  | `C:\Users\dhopt\Downloads` | none | `Downloads` |

  **Why anchor-first with the repo as a parenthetical:** when a caller points `--cwd` at a
  directory, honoring the pointer beats inferring upward from it, and it is the better
  discriminator for ERGO-30's ("A repo-scoped defaults file the tool auto-discovers") stated
  value of telling apart a sea of robot icons -- two sessions in one monorepo become `web` and
  `api` rather than `monorepo` twice. The parenthetical restores the repo context a plain anchor
  name would lose. Rejected in a phrase: repo-name-first (loses monorepo-sibling
  distinguishability); a static engine-level suffix (LIFE-10 forbids naming the host, and the
  icon already says which tool it is); a session ordinal like `(2)` (stateful and racy).
- **Built-in default subtitle = `{branch}`** when a `.git` root resolves, else empty. Free -- the
  phase-17 walk already reads `.git/HEAD`. Two consequences the operator accepted explicitly
  (2026-07-14): when content has `SetQuestion` set (our Blocked state), the platform gives the
  question text the bold subtitle slot and does not render `AppTaskInfo.Subtitle` at all
  (`docs/windows-ui-shell-tasks/README.md`), so the branch is hidden while a card asks
  permission -- the question outranks the branch; and everyone working on `main` sees `main`.
- **Floor:** an anchor with no last path segment (a drive root, `C:\`) falls back to the brand
  name from `Branding` (ERGO-18, "The shipped command name"), so "never blank" holds literally.
- **Empty expansion falls through too:** when a layer supplies a template that expands to empty
  -- e.g. `.atv.json` sets `"{repo}"` in a non-git directory and ERGO-30's token-drop rule yields
  `""` -- the built-in default catches it. Empty is never a final title.
- **`{repo}` semantics unchanged:** `{repo}` remains the git root's folder name; the built-in
  default is the anchor's own name plus the parenthetical. Identical unless anchored below the
  repo root.
- **Gated to card creation**, like ERGO-30's discovery: the default resolves on the upsert
  **create** branch (`SemanticEngine.ApplyRepoDefaults`) only. No hot-path cost.

### The chain's top: the host's own session name (translator half)

`translate.ps1` forwards Claude Code's **`session_title`** as `--title` **only when present**.
This is translator scope; atv stays host-agnostic (LIFE-10) and only ever sees a `--title`.

- **It is the USER-ASSIGNED name only.** `qT()` returns `currentSessionTitle`; the AI-generated
  title behind the `/resume` list is a separate `currentSessionAiTitle` (via `l2e()`/`TNe()`)
  that the hook path never calls. So it is absent unless the user explicitly named the session --
  which is why forwarding it does not re-open the concern in
  `integrations/claude-code/README.md`'s "Deliberately no identity flags" section. That section
  argues against hard-coding a **constant** (`--title "Claude Code"` on every call would
  permanently block repo-branding). A conditional value present only when the user named this
  session is explicit user intent, which is precisely what the chain's first stop is for.
- **It reaches the event that creates the card.** `session_title` rides `SessionStart` and
  `UserPromptSubmit`; the latter is where `translate.ps1` creates the card (`working --goal`), so
  no translator state is needed -- nothing has to be stashed at `SessionStart` and replayed. It is
  NOT in the shared base payload (`km()`), so it is scoped to those two events; other events pass
  no `--title` and `ApplyIdentityIfClaimed` leaves the live card's title alone.
- **No `hooks.json` change** -- `translate.ps1` already parses the whole payload from stdin.
- **Source and verification posture.** The above was read off the **shipped binary, v2.1.210**
  (2026-07-14) -- not published docs, and not a live capture. Operator decision (2026-07-14): do
  NOT gate the build on a re-capture; assume it is correct and **test at validation time**. The
  risk is low by construction -- if `session_title` never arrives, no `--title` is passed and the
  chain falls through to the repo/folder default, which is the behavior decided here.
- **Why the phase-14 captures never showed it** (worth knowing, not a blocker): `qT()` returns
  `undefined` for an unnamed session and `JSON.stringify` omits undefined-valued keys, so the key
  vanishes rather than appearing empty -- and no phase-14 scenario ever named a session. The
  generalizable lesson: capture-gating is blind to conditionally-present fields; a trace proves
  only what the exercised scenario produced. (A version gap may also contribute --
  `docs/host-events/claude-code.md` records **2.1.207** -- but the traces cannot distinguish the
  two causes.) Checked for broader drift and found none: every other event in the 2.1.210 binary
  (`PostToolUseFailure`, `PostToolBatch`, `UserPromptExpansion`, `PreCompact`) is already
  documented.

### Docs the build must correct
`integrations/claude-code/README.md`'s "Deliberately no identity flags
(`--title`/`--subtitle`/`--icon`)" section -- its "a repo with no `.atv.json` therefore gets an
empty-titled card by design" paragraph is now false, and the section must describe the
conditional `session_title` forwarding as the one identity flag the translator does pass -- and
`docs/configuration.md`'s defaults table.

## Question
When no `.atv.json` supplies `title-template` (and no caller `--title`), should the engine's
own built-in default fall back to something derived from the resolved `--cwd`/repo anchor --
e.g. the last path segment of the anchor directory -- instead of the empty title it produces
today?

Widened during its answer session (operator, 2026-07-14) to the full title/subtitle chain: the
question is not only what the built-in default is, but what resolves a card's title and subtitle
at every layer above it -- including whether the host's own session name feeds the top.

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
- The answer spans two layers with different build owners: the engine (default title/subtitle)
  and the translator (`session_title` forwarding). LIFE-10 keeps them separable -- atv sees only
  `--title`.

## Options explored
1. Built-in default title = last path segment of the resolved anchor (`--cwd` if present, else
   process cwd) -- always available, simplest, no git dependency.
2. Built-in default title = the `{repo}` token's own resolution (git-root folder name), empty
   if no `.git` boundary is found -- consistent with ERGO-30's existing template semantics, but
   doesn't help the no-`.git` case.
3. Status quo: empty title without a `.atv.json`; polish stays opt-in per repo.

Decided: option 1's anchor resolution, composed with option 2's repo name as a parenthetical
only when the two differ, under a chain topped by the host's session name -- see the Decision.

## Scope note
Filed OPEN (operator, 2026-07-14, phase-18 live dogfood); DECIDED the same day after widening
from "the built-in default" to the whole chain (hence the revised title; the file was previously
`ERGO-33-default-title-fallback-to-anchor-folder-name.md`). Related: ERGO-30 ("A repo-scoped
defaults file the tool auto-discovers" -- the `{repo}` token and the `--cwd` anchor this reuses),
ERGO-26 ("Config file location and format" -- the precedence chain this terminates), ERGO-18
("The shipped command name" -- the brand constant used as the floor), ERGO-31 ("The v2 semantic
verb contract" -- the verbs whose `--title` carries the host session name), LIFE-10 (no host
specifics in atv), LIFE-24 (translator disciplines).
