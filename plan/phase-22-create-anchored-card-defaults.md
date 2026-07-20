# Phase 22: Create-anchored card defaults — per-repo icon + anchor deep-link

**Depends on:** phase 15 (the `SemanticEngine` / `ApplyClaim` architecture both changes
gate into), phase 16 (the emoji render path + `GlyphProbe` the icon pool leans on),
phase 17 (the ERGO-30 anchor/`RepoDiscoveryResult`, `ApplyRepoDefaults` /
`ResolveCreateTimeIcon` create branch, and the doctor surfacing this extends), phase 19
(the redirected-activity / fan-out child-mint paths these edits must not regress).
**Unblocks:** phase 23 by sequencing only (the dogfood kit should ship these fixes —
they are the dogfood pain points that motivated it), no build coupling.

## Goal

Two defaults that today point at the tool now point at the work. When a card is created
with no icon supplied anywhere, it gets a **deterministic per-repo icon** from a combined
Segoe-glyph + emoji pool instead of the fixed Robot — a wall of cards reads as a spread
keyed to which repo each belongs to. When a card is created with no `--deep-link`, its
click target is the **resolved anchor directory** (the folder the session works in)
instead of the tool's app-data folder. Both are create-time defaults that must then
*survive updates*: the shared structural fix is that unclaimed icon/deep-link fields stop
being re-written on every update.

## Decisions implemented

- **ERGO-34 ("Icons should be randomly picked when they are not explicitly supplied")** —
  the deterministic per-repo pick, its combined pool, and its post-review fix: move the
  icon file-write out of the dispatcher and into the engine so a plain update stops
  rewriting the icon PNG to Robot.
- **ERGO-35 ("Card URI opening the ATV folder is confusing… it should open the repo
  folder")** — the anchor-dir deep-link default, its floors, and its post-review fix: a
  `deepLinkExplicit` gate so updates preserve the live card's deep-link. Amends ERGO-24
  ("The default deepLink URI value") — app-data demotes from default to floor.

Both records carry code-verified corrections (2026-07-19) pinning the current defect
mechanics; the line references below come from them.

## Part 1 — Explicit-flag gating: unclaimed fields survive updates (shared architecture)

The two defects share one cause: the dispatcher resolves icon and deep-link on **every**
verb and the engine applies them on **every** update, so create-time values are stomped.

- **Deep-link:** `Dispatcher.TryResolveDeepLink` (`src/Atv/Cli/Dispatcher.cs:391`)
  substitutes `_defaultDeepLink` whenever `--deep-link` is absent, and
  `SemanticEngine.ApplyClaimCore` calls `_store.UpdateDeepLink(entry.Id, deepLink)`
  unconditionally on the live-card path (`src/Atv/Semantics/SemanticEngine.cs:700`). No
  translator ever passes `--deep-link`, so every `activity` reverts the card to the
  default.
- **Icon:** every upserting verb body calls `_icons.Place(handle, token)` in the
  dispatcher (`Dispatcher.cs:156`, `:181`, `:207`, …) before invoking the engine, with
  `token` = the Robot default on a no-icon call — the on-disk PNG is rewritten to Robot on
  every update (proven byte-identical in ERGO-34's repro), even though the taskbar's
  in-memory cache hides it until a reload (reboot, boot-recovery re-create, ERGO-13
  grouping-reuse reading the owner's bytes).

**The fix, mirroring the existing `iconExplicit` / `ApplyIdentityIfClaimed` pattern:**

1. Thread a **`deepLinkExplicit`** flag from `TryResolveDeepLink` (flag present and valid
   → `true`) through all seven upserting engine verbs, exactly as `iconExplicit` is
   threaded today. An explicit-but-invalid `--deep-link` still errors (unchanged).
2. On the **update** path, when `deepLinkExplicit` is false, **skip `UpdateDeepLink`
   entirely** — the live card keeps its existing deep-link (`AppTaskView.DeepLink` is
   readable; no re-discovery needed). Only an explicit `--deep-link` writes on update.
3. Move the icon **file placement out of the dispatcher and into the engine**, which is
   the only layer that knows create from update. The seven verb bodies stop calling
   `_icons.Place`; the engine places:
   - on **create** — via `ResolveCreateTimeIcon` (which already places for the
     override/grouping cases; extend it to place for the explicit-token and default cases
     too, so create always produces the file);
   - on an **update with `iconExplicit`** — place the caller's token;
   - on a **plain update** — no placement call at all; the existing file is untouched.
   Do **not** re-derive the repo-hash on update — the create-time file already holds it
   (no anchor re-walk, no hot-path cost).
4. **The plain-update icon comparison is pinned behavior, not a signature detail.**
   `ApplyClaimCore` gates a history-losing forced recreate on `live.IconUri != iconUri`
   (`SemanticEngine.cs:684`). A non-`iconExplicit` update must treat the icon as
   **unchanged** — compare against (or substitute) `live.IconUri`; no `Place`, no forced
   recreate. Getting this wrong either wipes step history on every plain update or
   silently fails to preserve the icon.
5. **Child mint and redirect source the PARENT'S LIVE values, not the passed-through
   arguments.** `MintChildCard` (`SemanticEngine.cs:515`, invoked from `AgentStarted`'s
   afterWrite at `:399`) and the redirected-activity path reuse the dispatcher-passed
   `iconUri`/`deepLink` — today those equal the parent's real values only because the
   dispatcher resolves them on every call. Once it stops, both paths must read the
   parent's **live card** (`parentLive.IconUri` / `parentLive.DeepLink`) instead, or
   fan-out children break the hard shared-`IconUri` glomming invariant (requirements.md)
   and get the floor deep-link instead of the parent's anchor. Reuse stays byte-for-byte
   with no `Place` call for a child (ERGO-13 physics).
6. **`run` routes through the engine — adopt it, don't exempt it.** `list`/`clear`/
   `doctor` use `TaskOperations` and are untouched, but `RunVerb`/`RunOrchestrator` call
   `engine.Working/Ready/Broken` directly (`RunVerb.cs:198/237/239`) after pre-placing
   their own icon (`RunVerb.cs:104`) and omitting the token/flag params — i.e. they
   implicitly pass `iconExplicit: true` with an empty token, which under the new
   placement rules would stomp `run --icon` to Robot at the terminal transition. Fix by
   adoption: delete run's pre-place; the creating `working` call threads run's real
   token with `iconExplicit` = (`--icon` was passed); the terminal `ready`/`broken`
   calls pass `iconExplicit: false` and `deepLinkExplicit: false`. `run` has no
   `--deep-link` flag, so its create resolves the anchor default (process cwd — where
   the wrapped command runs) like any other card: run cards get both new defaults for
   free. `RunDeps` keeps its `DefaultDeepLink` member for now even though the engine
   owns the floor.
7. **Compatibility pins.** `deepLinkExplicit` is an optional engine parameter defaulting
   to `true` (mirroring `iconExplicit`), so the ~38 existing engine call sites compile
   and keep today's semantics until each is deliberately flipped. The engine's
   null-`_icons` degradation (logic tests that wire no icon service) must keep working.
   `CompositionRoot` keeps injecting the app-data URI into the **dispatcher** (run's
   deps consume it) *and* supplies it to the engine as the floor — "re-plumbed" means
   added, not moved. Update the now-stale resurrection comment at
   `SemanticEngine.cs:753–757` ("already placed by the caller") when placement moves.

Remaining signature mechanics (whether the engine keeps taking a fallback `Uri` or takes
token+flags only) are build details; the behaviors above are the contract.

## Part 2 — Deep-link default: the resolved anchor directory (ERGO-35)

On the **create** branch only, when `deepLinkExplicit` is false, resolve the default
deep-link from the ERGO-30 anchor already in hand (`RepoDiscoveryResult.AnchorPath` —
`--cwd`, else process cwd; no second discovery):

- **Value:** a `file:` URI to the **anchor directory** — not the repo root. For a
  monorepo subproject it lands at the subproject; in the common single-repo case the two
  are identical. No `.git` boundary is required (this deliberately diverges from Part 3's
  icon key, which wants shared repo identity; the deep-link is about *where you land*).
- **Floors to ERGO-24's app-data `LocalState` URI** (the old default, still always
  valid) in every degenerate case: no anchor resolves; the anchor directory does not
  exist on disk at create time; or the path can't be represented cleanly as a URI (build
  the `Uri` under try/catch and round-trip-check `LocalPath` against the path — trailing
  spaces, `#`, etc. floor rather than producing a mangled target). A card always has a
  valid, benign `file:` target (FAIL-1 spirit).
- **Boundaries unchanged:** a repo `.atv.json` still cannot set the deep-link (ERGO-30's
  allowlist is untouched — the precedence for deep-link stays two-layer: `--deep-link`
  flag > built-in default). INTER-4 (click behavior redesign) stays deferred; this is a
  value-only change. Fan-out child cards do NOT re-resolve this default — they inherit
  the parent's live deep-link (Part 1 item 5), keeping the whole glommed group landing
  in one place.

## Part 3 — Icon default: deterministic per-repo pick from a combined pool (ERGO-34)

Fires **only at the bottom of the chain**, on the create branch, exactly where the code
today falls through to the Robot default: `iconExplicit` false **and** no env/repo/user
icon or icon-file override resolved (the `ResolveCreateTimeIcon` fall-through). Any
explicit icon at any layer wins untouched.

- **Key: the repo root, falling back to the anchor.** `RepoDiscoveryResult.RepoRootDir`
  when a `.git` boundary was found, else `AnchorPath`. Every card in a repo — including
  monorepo subdirectories — shares one repo icon; ERGO-33's repo/anchor-derived titles
  tell them apart. If literally no path resolves, the pick is skipped and the Robot
  default stands (the floor, mirroring ERGO-33's).
- **Pool = `CuratedSegoe` ∪ a new curated emoji set**, as data beside
  `IconTokens.CuratedSegoe` (standing invariant #8: curated platform data in one place).
  Curation bar for the emoji: visually distinct at taskbar size, common, a
  **conservative floor known present in Windows 11 26100's Segoe UI Emoji** (prefer
  long-established Unicode emoji; a pool entry missing on a target box degrades to Robot
  via the render fallback, which defeats the feature there), and — hard constraint —
  each a **single Unicode scalar**: `IconToken.Emoji`/`GlyphProbe` work on one codepoint
  (`IconTokens.cs:164`), so ZWJ sequences, flag (regional-indicator) pairs, keycaps, and
  skin-tone/variation-selector forms are all ineligible. **Target ≥100 combined
  entries** so collisions stay rare at realistic repo counts (birthday math: ~60 gives a
  ~10-repo dogfooder better-than-even odds of a shared icon). Collisions are still
  possible and accepted — the title carries the durable identity; do not oversell
  distinctness in docs.
- **Pick: SHA-256, not `GetHashCode`.** Normalize the key path — `Path.GetFullPath`,
  **trim trailing directory separators**, `ToUpperInvariant` — SHA-256 the UTF-8 bytes,
  take the first 8 bytes as a big-endian unsigned integer, mod the pool count. (The exact
  recipe is pinned so tests can precompute expected picks; any change to it or to the
  pool membership/order can reassign icons on upgrade — accepted, documented.) Never
  `String.GetHashCode` (per-process randomized). Stability claim is **per-machine**: two
  clones at different paths get different icons; say so honestly in docs.
- **The pick resolves the *effective token*, upstream of the grouping branch.** In
  `ResolveCreateTimeIcon`, when nothing explicit resolves, the repo-hash pick becomes the
  effective token **before** the grouping-intent logic runs — so a `group=true` repo's
  owner card is placed with the repo icon too (not Robot), and every glommed card in that
  repo shares it. The two features key on the same repo root, so this composes rather
  than conflicts; the grouping mechanics themselves (owner registry, byte-for-byte
  reuse, self-healing transfer) are unchanged.
- **Robot's remaining roles:** render-time fallback inside `IconService` (picked glyph
  fails to probe/render → Robot → drawn shape) — unchanged; and the no-path floor above.
  ERGO-12's "always Robot" is superseded only for the no-icon-supplied case.
- **Determinism guarantee that matters:** clear + recreate a card in the same repo (same
  build) yields the same icon — the card never "loses its identity".

## Part 4 — Observability + docs

- **`doctor`** surfaces the icon the repo-hash default *would* pick for the resolved
  anchor ("default icon: `<token>` — repo-hash default for `<key path>`"), rendered
  beside phase 17's anchor/repo-config lines — which are **unconditional**, so follow
  that precedent rather than building new `--verbose` plumbing (`DoctorVerb` has no
  verbosity gate today; ERGO-34 left the exact surface a build-time call). Mechanically:
  a new `DoctorReport` field, computed in `DoctorChecks` from the same discovery/pick
  code, rendered in `DoctorVerb`. A surprising icon is a one-look diagnosis (same
  anti-"silent sea of robots" posture as ERGO-30).
- **Docs** (`docs/configuration.md`, `README.md` — doc-style skill applies): the
  per-repo default icon (per-machine stability, collision honesty, pool-edit caveat) and
  the anchor deep-link default + its app-data floor.

## Files affected

```
src/Atv/Icons/IconTokens.cs                  # curated emoji pool + combined default pool data (+ pick helper)
src/Atv/Semantics/SemanticEngine.cs          # create-time placement; repo-hash default; anchor deep-link default + floors; skip UpdateDeepLink/Place on unclaimed updates
src/Atv/Cli/Dispatcher.cs                    # remove per-verb _icons.Place; thread deepLinkExplicit
src/Atv/Cli/Verbs/RunVerb.cs                 # drop pre-place; thread run's real token/flags (Part 1 item 6)
src/Atv/Cli/CompositionRoot.cs               # app-data deep-link value ALSO supplied to the engine as the floor (dispatcher keeps its copy)
src/Atv/Diagnostics/DoctorChecks.cs          # + would-pick icon surfacing (--verbose)
src/Atv/Cli/Verbs/DoctorVerb.cs              # render the new line
tests/Atv.LogicTests/Semantics/*             # preservation, defaults, floors, child/grouping regressions
tests/Atv.LogicTests/Icons/*                 # pool data, pick determinism, normalization
tests/Atv.LogicTests/Cli/*                   # dispatcher no longer places; deepLinkExplicit threading
tests/Atv.LogicTests/Run/*                   # run icon/deep-link regression coverage (AC10)
tests/Atv.LogicTests/Diagnostics/*           # doctor line
docs/configuration.md, README.md             # both defaults documented
```

## Acceptance criteria (written first)

All fake-backed/automated except AC12. Live conduct follows phase 21's INFRA-33 rules.

1. **Icon file survives plain updates (the ERGO-34 repro, inverted).** Create with an
   explicit distinctive icon; run a burst of no-icon updates (`activity` etc.); the
   on-disk PNG is **byte-identical** to the create-time bytes, the card's `IconUri`
   unchanged, and **no forced recreate fires** (step history preserved — the
   `live.IconUri` comparison rule, Part 1 item 4). An update passing explicit
   `--icon`/`--icon-file` *does* rewrite it.
2. **Deep-link survives plain updates.** Create (default or explicit deep-link); no-flag
   updates leave `AppTaskView.DeepLink` unchanged (structurally: no `UpdateDeepLink`
   store call on an unclaimed update — counting-fake proof); an explicit `--deep-link`
   update writes; an invalid one still errors.
3. **Anchor deep-link default.** Create with no `--deep-link` under a temp-dir anchor →
   `DeepLink` is the `file:` URI of the anchor directory (`--cwd` beats process cwd,
   phase-17 anchor semantics). Monorepo case: anchor ≠ repo root → deep-link tracks the
   **anchor**, icon key tracks the **repo root**.
4. **Deep-link floors.** Nonexistent anchor dir, unrepresentable path, and no-anchor each
   floor to the app-data URI; the result is always a valid absolute `file:` URI.
5. **Deterministic pick.** For pinned key paths, the picked pool index matches the
   precomputed SHA-256 recipe; equal keys modulo normalization (trailing separator, case,
   relative form) pick identically; clear + recreate picks the same icon; distinct pinned
   repo paths hit the distinct expected indices.
6. **Chain position.** The repo-hash default fires only when nothing explicit resolves
   anywhere (flag/env/repo-file/user-file each suppress it — reuse the phase-17
   precedence matrix pattern); no-path floor yields Robot; render-failure fallback to
   Robot is unchanged.
7. **Pool integrity.** Combined pool ≥100 entries, no duplicates; every emoji entry is a
   single Unicode scalar that round-trips `IconTokens.TryParse` and passes `GlyphProbe`
   on the build machine (necessary-condition check; the 26100 curation bar is a review
   obligation, not a test).
8. **No regressions in shared-URI paths — and children track the parent's live values.**
   Fan-out child cards reuse the parent's **live** `IconUri` byte-for-byte with zero
   child `Place` calls, and a child's `DeepLink` equals the parent's live deep-link (not
   the floor); grouping-intent owner-reuse mechanics unchanged (the owner now carries
   the repo-hash icon per Part 3); existing Semantics/fan-out suites green unmodified
   (except where they asserted the old stomping behavior — those flip deliberately and
   are called out).
9. **Structural: the dispatcher no longer writes icon files.** No `IconService.Place`
   call originates from the seven verb bodies (counting-fake or code-shape proof);
   engine-null-icons degradation intact; suites + NativeAOT publish clean; invariants
   #2/#4 re-verified.
10. **`run` regression (the Part 1 item 6 adoption).** `run --icon <tok>` keeps that
    icon through the terminal `ready`/`broken` transition (file bytes + `IconUri`
    stable); `run` without `--icon` gets the repo-hash default for its cwd; run's card
    deep-link is the anchor (process cwd) at create and survives the terminal update;
    exit-code passthrough and the rest of the existing Run suite green.
11. **Doctor + docs.** `doctor` under a temp-dir anchor shows the would-pick default
    icon line with the key path; `docs/configuration.md`/`README.md` document both
    defaults with the honesty notes (per-machine stability, collision odds, pool-edit
    reassignment caveat), per the doc-style skill.
12. **LIVE — dogfood eyeball (operator-supervised, INFRA-33 conduct: throwaway identity
    or `atv-dev`, never bare `atv`).** In a repo with no icon config: the new card shows
    a non-Robot pool icon; clicking the card opens Explorer at the anchor folder; after
    `clear` + recreate the same icon returns. Two different repos show different icons
    (or a collision is confirmed as the documented degradation, not a bug).

## Out of scope

- Truly-random or per-handle/per-session icon keying (rejected in ERGO-34); full-range
  emoji (rejected); cross-machine icon stability (explicitly not promised).
- Any repo-file `deep-link` key (still excluded by ERGO-30's allowlist) or env/user
  layers for deep-link (the chain stays flag > default).
- INTER-4's click-behavior design (deferred); the interaction round-trip.
- `TaskOperations` paths (`list`/`clear`, resurrection, watchdog) — they don't route
  through the engine's claim path. (`run` **does** — it is in scope via Part 1 item 6.)
- Migrating existing live cards' icons/deep-links (cards are ephemeral; new defaults
  apply from the next create).
