# DIST-11: How the per-host integration artifact is delivered, placed on disk, and wired into the host's config
**Status:** DECIDED (2026-07-13)
**Plan:** phase-18
**Decision:** Ship each integration as the **host's own native plugin/extension mechanism** —
it solves delivery + placement + wiring in one, and keeps the translator artifact out of the
`atv` MSIX entirely. This yields a clean **two-vehicle split**:
- **MSIX = just the `atv` engine** (binary + `atv` alias on PATH). That's DIST-10.
- **Plugin = the per-host integration artifact** (`translate.ps1` + `map.json` + the hook
  declarations). Never rides in the MSIX.

**Claude Code exemplar (grounded in the hooks docs, 2026-07-13):** a Claude Code *plugin*
bundles the translator files and declares its own hooks — so installing the plugin **wires**
the hooks (no hand-edit of `settings.json`) and **delivers** the files. The hook lines
reference **`${CLAUDE_PLUGIN_ROOT}`**, which Claude Code substitutes to the plugin's install
dir and *refreshes on every plugin update* — so `${CLAUDE_PLUGIN_ROOT}/translate.ps1`
**resolves stably and dodges the opaque versioned-MSIX-path landmine** that motivated this
question (the translator files aren't in the package at all). `atv` stays host-agnostic — the
plugin's hooks just invoke it by alias.

**Per-host:** pi uses its in-process extension mechanism (its conduit already IS an extension,
LIFE-24); Copilot/Codex use their own plugin/extension mechanism, authored with their deferred
legs (INFRA-31, recipe INFRA-32). A host with **no** plugin mechanism falls back to a documented
manual wire, or a future `atv install-hooks <host>` (the fallback, no longer the primary — the
native plugin supersedes the install-hooks idea floated in DIST-10 for hosts that have plugins).

**Not committed now (build/publish work, Claude Code first):** the concrete per-host plugin
*manifest authoring* and the *marketplace publication* specifics ("install the plugin however
the host provides" — marketplace mechanics vary per host and move, so they're not pinned here).

**Relationship to DIST-10:** the two adoption legs — DIST-10 gets the *engine* onto the machine
(cert-gated, DIST-2), DIST-11 gets the *artifact* on and wired (plugin-native, **not**
cert-gated). Independent vehicles; a user installs the `atv` MSIX once and adds the plugin per
host.

## Question
LIFE-11 ("Whether we ship per-host integration artifacts") committed us to shipping ready-made
per-host integrations, and LIFE-24 / ERGO-31 fixed their *shape* (`integrations/<host>/` =
`hooks.settings.json` conduit + `translate.ps1` translator + `map.json` extraction table, plus a
normative `docs/integration-api.md`). DIST-10 ("Getting `atv` onto the machine alongside the host
plugin") owns getting the **binary** registered. Nothing owns the lifecycle of the **integration
artifact itself**. Three unanswered steps sit between "user wants the integration" and "hooks
actually fire":

1. **Delivery** — what channel puts `integrations/<host>/` on a real user's machine? Bundled
   *inside* the `atv` MSIX, a host plugin/marketplace entry, a repo clone, a separate download?
2. **On-disk placement** — the conduit line needs a resolvable path to `translate.ps1` (and the
   script needs `map.json` beside it). Where do these live post-install, and does that location
   survive an `atv` upgrade and stay user-editable?
3. **Wiring** — the hook *entry* has to land in the host's own config (`~/.claude/settings.json`
   and the Copilot / Codex / pi equivalents). Who performs that merge — a hand-edit, an
   `atv install-hooks <host>` verb, or a host marketplace that merges settings?

## Why this surfaced
Operator + session, 2026-07-13, while detailing DIST-10 / DIST-2 during answer-session ordering.
DIST-10 reads as if delivery/placement/wiring are already solved — its text presupposes "a
distributable 'plugin'" exists and only asks how the binary rides along. It does not own how that
plugin is produced, delivered, placed, or wired. The phase-13 dogfood simply pasted a fragment
into `settings.json` pointing at a repo path; that is not a shipped-product adoption story, and
it is exactly the class of implicit wiring that has produced plausible-but-wrong configs before.

## What makes it non-trivial (constraints)
- **The opaque, versioned MSIX install path is a landmine.** If the translator files ride inside
  the `atv` MSIX (DIST-1's full-package model), they land under
  `…\WindowsApps\Agentaskvoid_<ver>_<arch>_<pubhash>\…` — a path that is read-only and **changes
  on every version bump**. A hard-coded path in the host's `settings.json` would break on the
  next `atv` upgrade. So placement can't naively be "wherever the package unpacked."
- **`map.json` is meant to be user-editable** (LIFE-24: users add rows for their own MCP tools
  without touching code). A read-only package location defeats that; a user-writable copied
  location can drift from the engine's expected contract across upgrades. Delivery has to pick a
  side of that tension (ship read-only + a separate user override layer? copy-on-install to a
  user dir? — undecided).
- **Wiring is per-host and structurally divergent.** Claude Code / Copilot / Codex take a JSON
  hook entry merged into a settings file; **pi has no hook config to install at all** — its
  conduit *is* a TypeScript extension file dropped in `~/.pi/agent/extensions/` (LIFE-24). So a
  single "install the hook config" mechanism does not generalize; the per-host wiring mechanics
  defer to each host's own config model (LIFE-10 / LIFE-11), while delivery + placement can stay
  host-agnostic.
- **Upgrade + uninstall symmetry.** On `atv` upgrade the translator files should track the engine
  contract; on `atv` uninstall the host-config entries should ideally be removed (mirrors DIST-9,
  "Uninstall behavior with live tasks and a running watchdog") — otherwise the host is left
  invoking a `translate.ps1` that no longer exists (which merely no-ops via the `Get-Command atv`
  guard, but still leaves dead config behind).
- **Trust surface.** A mechanism that writes into a host's settings file, or drops an executable
  script the host will invoke, is editing security-relevant config on the user's behalf — needs
  to be explicit and consented, not silent.

## Options to explore later (NOT deciding now)
1. **An `atv install-hooks <host>` verb** that (a) resolves its own co-packaged translator files
   (shipped inside the MSIX, so version-matched to the engine) or copies them to a stable
   user-writable location, and (b) merges the correct hook entry into the host's config —
   idempotently, with an `uninstall-hooks` inverse. Folds cleanly with DIST-10: adoption becomes
   "install `atv`, then `atv install-hooks claude-code`." Strong candidate; solves the
   opaque-path problem by never exposing the path to the user. (pi's variant drops/points at the
   TS extension instead of editing JSON.)
2. **Host plugin/marketplace packaging** — where a host offers a plugin format that carries files
   *and* declares hook wiring (and possibly the `atv` dependency, overlapping DIST-10 option 3),
   ship that. Per-host, moving targets, not uniform.
3. **Bundle inside the MSIX + document a manual wire-up** — the package drops the files at a known
   per-user path; the user pastes a one-line hook entry referencing a stable, non-versioned
   location `atv` guarantees. Lowest build cost, highest user friction, most error-prone.
4. **Ship the `integrations/` tree as a standalone repo/download** the user clones and wires
   themselves — closest to today's dogfood, worst adoption.

## Scope note
Filed OPEN (2026-07-13, answer-session discovery). In scope: LIFE-11 already committed us to
shipping these artifacts, so their delivery is a real product surface, not a hypothetical. Sibling
to DIST-10 (both close the adoption gap; DIST-10 = binary, DIST-11 = integration artifact); DIST-10
should hand this seam off explicitly rather than absorb it. Related: LIFE-24 / ERGO-31 (artifact
shape + `docs/integration-api.md`), LIFE-25 (conduit line form), LIFE-10 / LIFE-11 (host-agnostic
CLI + per-host wiring), DIST-1 (MSIX/identity — the opaque-path source), DIST-2 (real cert — a
marketplace/winget delivery leg is gated on it, like DIST-10), DIST-9 (uninstall symmetry), DIST-4
/ FAIL-1 / FAIL-3 (graceful no-op + `doctor` remedy when nothing is wired), ERGO-30 (repo-scoped
defaults — a per-repo layer above whatever this installs).
