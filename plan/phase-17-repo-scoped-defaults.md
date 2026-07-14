# Phase 17: Repo-scoped presentation defaults (`.atv.json`) + the `--cwd` anchor

**Depends on:** phase 06 (SettingsLoader/precedence machinery), phase 10 (doctor),
phase 15 (the upsert create branch the discovery gates on; the v2 verb surface the
`--cwd` flag lands on), phase 16 (the `icon-file` allowlist key).
**Unblocks:** phase 18 (the translator forwards `--cwd ${CLAUDE_PROJECT_DIR}`).

## Goal

Let each repo brand its own cards with zero edits to the shared hook: the engine
auto-discovers a repo-local `.atv.json` supplying **presentation defaults** (title
templates, subtitle, icon, grouping intent), anchored by a caller-supplied `--cwd`
so hook-spawned invocations resolve the right repo. The value is distinguishability
— telling apart a sea of identical robot cards — not new behavior surface.

## Decisions implemented

### Discovery + anchor (ERGO-30, "A repo-scoped defaults file the tool auto-discovers")

- **Discovery:** walk UP from the anchor directory; **first `.atv.json` wins**
  (nearest-wins, monorepo-friendly); stop at a `.git` boundary or filesystem root. A
  handful of `File.Exists` stats — negligible for a short-lived process. Lives in the
  engine, so any caller benefits.
- **Anchor:** atv takes **`--cwd <path>`** and never trusts its own process cwd in
  the hook case (hooks spawn from arbitrary directories). atv stays host-agnostic —
  it only ever sees `--cwd`, never reads a host env var itself (the Claude Code
  conduit passes `--cwd ${CLAUDE_PROJECT_DIR}`; that wiring is phase 18). Direct
  human use (no `--cwd`) falls back to process cwd. Where a host provides no usable
  anchor, repo config simply doesn't engage (documented degradation), never
  mis-anchors.
- **Gated to card creation:** discovery runs only on the upsert **create** branch (no
  active handle); skipped when updating a live card. Repo defaults bake in at
  creation — editing `.atv.json` mid-session affects only new cards. (Off the hot
  path: `activity` fires often, create once.)

### The allowlist IS the trust mechanism (ERGO-30)

- **Repo-settable keys (presentation only):** title (+ `{repo}`/`{branch}`
  templates), subtitle, icon token, icon-file (ERGO-29), and glomming/grouping
  intent. Nothing repo-settable does more than change how a card *looks* → no
  per-repo trust prompt.
- **Grouping intent** is ERGO-14's deferred `--group`-style glom arriving repo-scoped
  (ERGO-14 pinned it as "addable later, purely additive"). Mechanism follows ERGO-13
  physics: sessions glom only by sharing one exact `IconUri` string, so a repo
  glom key means a repo-keyed (not per-handle) icon placement — the same shared-URI
  path phase 15 built for fan-out children. Key name/shape is a build detail.
- **Excluded:** `deep-link` (a *launch action* — a checked-out repo must not decide
  what your card opens) and ALL operational knobs (idle periods LIFE-22, watchdog
  interval, log rotation — user/machine only). Non-allowlisted keys in a repo file
  are ignored and logged, never applied.
- **Identity stays global (ERGO-16):** repo config changes presentation, not the
  shared task namespace; `list`/`clear` stay identity-global.

### Precedence + observability (ERGO-30)

- **Per-key precedence:** `--flag > env > repo-file > user-file (ERGO-26) > built-in
  default`. The repo layer slots into the existing phase-06 loader chain; same flat
  string→string shape as `atv-config.json`, restricted to the allowlist.
- **Observability (anti-"silent sea of robots", FAIL-3 posture):** `doctor` /
  `--verbose` surfaces the resolved anchor + its source (`--cwd` vs process cwd), the
  `.atv.json` found (or "none, searched up to `<root>`"), and its parse status — a
  misconfigured hook is a one-look diagnosis.
- **Build details, not re-decisions:** title-template tokenization (`{repo}` = repo
  dir name; `{branch}` read cheaply, e.g. `.git/HEAD`, never shelling out per
  invocation); malformed-file handling follows the non-disruptive posture (ignore +
  durable log + doctor-visible, exit 0).

## Files affected

```
src/Atv/Config/RepoSettings.cs (new)      # discovery walk, allowlist filter, parse
src/Atv/Config/SettingsLoader.cs          # + repo layer in the precedence chain
src/Atv/Cli/CommandLine.cs, Dispatcher.cs # --cwd flag (upserting verbs + doctor)
src/Atv/Cli/CompositionRoot.cs            # anchor resolution wiring
src/Atv/Operations/*                      # create-branch-only application of repo defaults
src/Atv/Diagnostics/DoctorChecks.cs       # anchor/source/file/parse-status surfacing
tests/Atv.LogicTests/Config/*, Cli/*, Diagnostics/*   # TDD suites (temp-dir repos)
docs/configuration.md, README.md          # the repo layer, precedence, allowlist, degradation
```

## Acceptance criteria (written first)

1. **Discovery:** nearest `.atv.json` wins over an ancestor's; the walk stops at a
   `.git` boundary and at the filesystem root; no file → clean no-op; malformed JSON
   → ignored, durable log entry, exit 0.
2. **Anchor:** `--cwd` beats process cwd; absent `--cwd` falls back to process cwd;
   a structural test proves the engine reads no host env var for anchoring.
3. **Create-only gating:** with a live card, an edited `.atv.json` changes nothing on
   subsequent updates; the next NEW card picks it up. The update path performs no
   discovery probing (counting/temp-dir proof).
4. **Allowlist enforcement:** each allowlisted key applies from the repo file;
   `deep-link` and operational keys in a repo file are ignored + logged; the
   grouping-intent key yields two sessions in the same repo sharing one exact
   `IconUri` (glom) while other repos' cards stay separate.
5. **Precedence matrix:** for a representative key, all five layers proven in order
   (`flag > env > repo > user > default`), including repo-beats-user and
   env-beats-repo.
6. **Templates:** `{repo}` and `{branch}` expand in titles; missing git info
   degrades gracefully (token dropped or left literal — pick one, test it).
7. **Doctor:** output shows anchor + source, found file (or "none, searched up to
   `<root>`"), and parse status; a deliberately malformed repo file is a one-look
   diagnosis in `doctor` output.
8. **Suites + AOT:** logic suite green; NativeAOT publish clean; invariants #2/#4
   re-verified (brand-derived names for any new env keys; posture on all failure
   paths).

## Out of scope

The translator-side `--cwd ${CLAUDE_PROJECT_DIR}` forwarding (phase 18); per-host
anchor capability beyond Claude Code (capture-gated, LIFE-24 rule 7 / INFRA-31); any
trust prompt (the allowlist is the mechanism); repo-scoped operational knobs
(excluded by decision); a general `--config <path>` override (ERGO-30 option 2, not
chosen).
