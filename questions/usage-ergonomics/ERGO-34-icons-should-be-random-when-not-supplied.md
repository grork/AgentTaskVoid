# ERGO-34: Icons should be randomly picked when they are not explicitily supplied.
**Status:** DECIDED (2026-07-19)
**Plan:** phase-22
**Decision:** When no icon is supplied anywhere in the precedence chain, the built-in default
is no longer the fixed `Robot` glyph but a **deterministic-per-repo pick** from a **combined
pool of the curated Segoe glyphs + a curated emoji set** — so a wall of cards reads as a spread
of distinct icons keyed to which repo each belongs to, not a sea of robots.

- **Fires only at the bottom of the chain.** This is the resolution-time default that today
  yields `IconTokens.Default` (Robot) when `--icon`, env, the `.atv.json` icon (ERGO-30, "A
  repo-scoped defaults file the tool auto-discovers"), and the user-config icon (ERGO-26, "Config
  file location and format") are all absent. Any explicit icon at any layer still wins untouched.
  Resolves on the upsert **create** branch only (`SemanticEngine.ApplyRepoDefaults`), reusing the
  anchor ERGO-30/33 already resolved there — no hot-path cost, no new discovery.

- **Deterministic, keyed on the repo root.** The key is the discovered `.git` **repo root path**,
  falling back to the resolved **anchor directory path** (`--cwd`, else process cwd) when no
  `.git` boundary is found. So every card in a repo — including monorepo subdirectories — shares
  **one repo icon**, and they are told apart by ERGO-33's ("The card title/subtitle chain")
  repo/anchor-derived **title** (`web (monorepo)` vs `api (monorepo)`). Icon = repo identity,
  title = which part of the repo: the same anchor split ERGO-33 already draws, so `{repo}` and
  the icon track together while the title tracks the anchor. Deliberately **not** truly random
  and **not** per-invocation: clear + recreate a card must keep the same icon, or it reads as the
  card losing its identity. (Per-handle/per-session keying was the rejected alternative — it
  scatters one repo's sessions across icons and throws away "this repo = this icon" muscle memory;
  chosen against because the sea being disambiguated is primarily *many repos*, and intra-repo
  cards are already separated by title and by being distinct taskbar entries.)

- **Pool = curated Segoe glyphs ∪ curated emoji.** The union of both existing ERGO-20 ("Icon
  representation") vocabularies: the ~30 curated Segoe Fluent glyphs (`IconTokens.CuratedSegoe`,
  white-on-accent tiles per ERGO-28, "Theme-awareness of the provided icon") **and** a curated
  emoji set (bare full-color, theme-safe — ERGO-28's emoji path). Emoji carry most of the visual
  spread (colorful, varied — easiest to tell apart at taskbar size, where the monochrome Segoe
  tiles all share the accent tint and differ only by shape); the Segoe glyphs are included per
  operator (2026-07-19) so the pool spans both families. **Full-range emoji was rejected** —
  thousands of obscure, near-duplicate, or small-size-illegible glyphs; the pool is a hand-picked
  set instead.

- **Stable hash, not `GetHashCode`.** The path → pool-index map must be stable across processes
  and machines, so it uses **SHA-256 over the normalized path** — `Path.GetFullPath(...)` then
  `ToUpperInvariant()`, the exact normalization the translators already use
  (`translate.ps1`) — mod the pool size. Never `String.GetHashCode` (per-process randomized in
  .NET) or MSBuild's `StableStringHash` (build-time only). AOT-safe (`System.Security.Cryptography.SHA256`).

- **Robot stays as the render-failure floor.** This changes only the *resolution-time* default
  (what token we pick when none is supplied). The *render-time* fallback inside `IconService`
  (a chosen glyph that fails to render → `IconTokens.Default` Robot → drawn shape) is unchanged —
  Robot remains the safety net when a picked glyph can't be rendered, and the brand/drawn-shape
  floor still applies if literally no anchor path resolves (a drive root with no path to hash,
  mirroring ERGO-33's floor). Supersedes ERGO-12's ("Defaults for parameters that are secretly
  required") "always Robot" only for the no-icon-supplied case.

### Build-time details (not decision blockers)
- The exact curated emoji list (visually distinct, common, legible at taskbar size — same bar as
  the curated Segoe list). Lives as data beside `IconTokens.CuratedSegoe` (plan/README.md
  standing invariant #8: empirical/curated platform data in one place).
- Combined-pool ordering, and the fact that **editing the pool can reassign a repo's icon on
  upgrade** (hash mod a changed pool size). Accepted as low-stakes: icons are stable within a
  build; pool edits are rare, and the title (ERGO-33) carries the durable identity regardless.
- Reuse of the anchor/repo-root resolution ERGO-30/33 already compute; no second walk.
- `doctor`/`--verbose` may surface the picked icon + that it came from the repo-hash default
  (mirroring ERGO-30's anti-"silent sea of robots" observability), so a surprising icon is a
  one-look diagnosis. Build-time call.

## Post-review note (2026-07-19) — TESTED: display persists, file IS rewritten (do the tidy-fix)
An earlier draft called this a blocker ("icon reverts to Robot on the first update"). That framing
was wrong, and a repro test (fake-backed logic harness: real `IconService` + `SemanticEngine`)
settled exactly what happens. Both things are true, on different layers:

- **The icon a user SEES persists across updates** — matches the operator's own testing. The
  taskbar loads a card's icon from its file path once and caches it; that path never changes for a
  card, and the test confirmed the icon URL was identical before and after all updates
  (`updUri == createUri`), so the taskbar has no signal to reload.
- **The on-disk PNG IS rewritten to Robot** — confirmed by hashing. Create rendered the rocket
  (3736 bytes); after 8 no-icon `activity` updates the same file was 1029 bytes, **byte-identical
  to the Robot default reference**. The dispatcher's per-verb `_icons.Place(handle, token)`
  (`Dispatcher.cs:156/181`), with `token` = Robot on a no-icon update, overwrites the file; the
  engine update path never re-resolves it.

**Severity: not a blocker, but a real should-fix for THIS feature.** Fine in-session (display is
cached). The Robot file surfaces only when the shell reloads the icon from disk — taskbar rebuild,
logon, reboot / boot-recovery re-create, or ERGO-13 grouping-reuse (which reads the owner handle's
bytes, `SemanticEngine.cs:1019`). Since ERGO-34's whole point is a per-repo icon that *stays*
recognizable, those reload cases (especially boot-recovery and grouping) matter enough to fix it
properly rather than leave a file that lies.

**Fix — move the icon file-write into the engine.** The rule matches title/subtitle and the
deep-link (ERGO-35): the icon is placed at **create**, or when the caller passes an explicit
`--icon`; a plain update that passes neither leaves the existing file untouched.

Today that can't happen, because the file-write lives in the **dispatcher**
(`Dispatcher.cs:156/181`, `_icons.Place(handle, token)`) — it runs on every verb and cannot tell a
create from an update; only the engine knows that, so the dispatcher stomps the file on every
update. Move the placement out of the dispatcher and into the engine:

- **create** → place the icon (the repo-hash default, using the ERGO-30 anchor already resolved
  there; or the repo/env/user override; or an explicit `--icon`);
- **explicit `--icon` on an update** → place that;
- **plain update, no `--icon`** → place nothing; keep the live card's existing file.

The `iconExplicit` flag already carries the "did the caller pass one?" signal — it just has to gate
the write, exactly as `ApplyIdentityIfClaimed` gates title/subtitle and the ERGO-35
`deepLinkExplicit` gate governs the deep-link. Do not re-derive the repo-hash on update (that
re-walks the anchor and reintroduces hot-path cost) — the create-time file already holds it.

**Also fold in (verified minors):**
- **Collisions are routine, not rare — size the pool against repo count.** ~30 curated Segoe + a
  curated emoji set. Birthday math: with a pool of ~60, a dogfooder with ~10 repos has a
  better-than-even chance of two repos sharing an icon; icon-level disambiguation then degrades to
  the ERGO-33 title. Curate the emoji set large enough (target ≥100 total pool) that collisions
  stay rare for a realistic "sea of robots" repo count, and say so rather than overselling "a
  spread of distinct icons."
- **Normalization was mis-attributed and drops a step.** `translate.ps1` does **no** path
  normalization — it forwards raw `--cwd ${CLAUDE_PROJECT_DIR}`. The intended `GetFullPath` +
  `ToUpperInvariant` recipe must **also trim trailing separators** before uppercasing, or
  `C:\r\repo` and `C:\r\repo\` (a trailing slash is live in the non-git anchor-fallback case) hash
  to different icons.
- **Cross-machine "same repo = same icon" is illusory.** SHA-256 of the absolute path is stable
  across processes on one machine, but two clones at different paths (`C:\dev\x` vs `D:\src\x`)
  get different icons. The muscle-memory claim holds per-machine only; state it honestly.
- **Index-determinism ≠ rendered-icon determinism.** A curated emoji present on a newer Segoe UI
  Emoji build but absent on the min-supported Win 11 26100 → `GlyphProbe` miss → Robot fallback,
  so the same repo can show its emoji on one box and Robot on another. Curate the emoji pool to a
  **conservative floor known present on 26100**, not merely "legible."

## Question
In dogfooding, it's become a challenge to disambiguate a sea of 'robot' icons on the taskbar.
While it's possible for .atv.json files to be configured to directly control it, not all repos
will have these setup, so by default we should select a random icon when not provided by any
of the other mechanisms we have for setting an icon.

It's unclear if this should be derived through some deterministic approach that for a given repo
(path?) it's predictable, or if it should be truely random. It's also not clear what the full range
of icons that should be selected from. Maybe full range of emojis would be the right choice?

## Scope note
Filed OPEN (operator, dogfooding); DECIDED 2026-07-19. Related: ERGO-12 ("Defaults for parameters
that are secretly required" — the `Robot` default this supersedes for the no-icon case), ERGO-20
("Icon representation — specifying an icon without image files" — the two vocabularies this pools),
ERGO-28 ("Theme-awareness of the provided icon" — Segoe-tile vs bare-emoji render paths), ERGO-30
("A repo-scoped defaults file the tool auto-discovers" — the anchor/repo resolution and precedence
chain this bottoms out), ERGO-33 ("The card title/subtitle chain" — the repo/anchor title that
distinguishes cards sharing one repo icon).
