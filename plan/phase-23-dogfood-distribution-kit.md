# Phase 23: Dogfood distribution kit

**Depends on:** phase 12 (`-t:AtvRelease` — the per-arch NativeAOT publishes, signed
`.msix`es, and throwaway dev cert this reuses), phase 18 (the Claude Code plugin tree;
the in-tree Copilot plugin the zips also package has **no numbered phase of its own** —
it is an existing repo artifact, per phase 20's framing), phase 20 (hard sequencing: the
zips must carry the `atv-command.txt` override tier and must be built so a gitignored
override can never leak into them; `docs/release.md` §2's rewritten identity story is
what the kit's docs build on). Phase 22 is **soft sequencing** — build the kit after it
so dogfooders receive the per-repo icons and anchor deep-link they are being asked to
evaluate.
**Unblocks:** phase 24 (the Copilot auto-wiring leg, split out of this phase — see the
scope note under Decisions). Opens the pre-publication feedback loop: handing an
**unreleased** build + plugins to another person's machine in one folder.

## Goal

One command produces a shareable folder (`artifacts/dogfood/`, gitignored) containing a
dev-signed dual-arch `.msixbundle`, the dev cert's public `.cer`, one plugin zip per
implemented integration, minimal factual READMEs, and install/uninstall scripts that do
the whole recipient-side flow — cert trust (one explained elevation), bundle install,
prompted per-host plugin wiring — and undo it symmetrically. This is the interim sibling
to DIST-10/11's published channels (both gated on DIST-2's real cert); when the real cert
lands, those become the adoption story and this stays the pre-publication tool.

## Decisions implemented

### DIST-13 ("Easy-to-use scripts that produce package and plugins for sharing & dogfooding")

DIST-13 is DECIDED (2026-07-19) and is the arbiter; its post-review correction pinned the
two mechanisms an earlier review had wrongly flagged:

- **The bundle builds in one command.** `winapp package <win-x64 publish dir>
  <win-arm64 publish dir> --output <name>.msixbundle` emits the dual-arch bundle
  directly — no `makeappx`, no manifest change; winapp derives each package's
  architecture from the RID-specific publish folder (operator-verified: a hand-built
  bundle installs and arch-selects correctly on both x64 and arm64). Sign it with the
  same dev cert `-t:AtvRelease` already generates.
- **Both hosts wire persistently from a local dir — no per-host asymmetry.** Per detected
  host: Claude Code → `claude plugin marketplace add <dir>` + `claude plugin install
  atv-integration@agent-task-void`; Copilot CLI → `copilot plugin marketplace add <dir>`
  + `copilot plugin install atv-integration@<marketplace>`. (`copilot --plugin-dir` is a
  launch-time dev flag, not an install mechanism — irrelevant here.)

**Scope split (operator direction, 2026-07-19): the Copilot auto-wiring leg executes in
phase 24, not here.** The record's Copilot commands are Learn-docs-sourced, unverified
against the shipped `copilot` CLI, and in tension with `integrations/copilot-cli/README.md`
(which documents a GitHub-slug install and no local-marketplace flow) — and the operator
currently has no Copilot access to verify with. Rather than leaving this phase
part-complete, THIS phase ships the kit with **Claude Code as the only auto-wired host**
(the Copilot plugin zip still ships — zip enumeration is host-agnostic — with its kit
README noting auto-wiring is pending), and **phase 24** verifies the real Copilot
commands and adds that leg when access exists. DIST-13 remains the arbiter for both
phases.

Key identity facts: the bundle is stamped the **retail identity** (Name =
`Codevoid.AgentTaskVoid`, alias `atv`) — the plugins invoke bare `atv` (DIST-12), and
dogfooders should exercise the literal release artifact. **Not** `-reltest`. On an
external dogfooder's machine that is clean; on the operator's own box the kit would
upgrade the daily driver in place (same PFN) — the kit targets other people's machines.

## Part 1 — Producer: one build target

A new `build/Atv.Dogfood.targets` with a target invoked as
`dotnet build src\Atv\Atv.csproj -t:AtvDogfood` (name is a build detail; parallel
`-t:AtvRelease`), chained on the existing per-arch release targets so it never re-derives
what `-t:AtvRelease` already produces. It emits `artifacts/dogfood/` (gitignored —
`artifacts/` already is) containing:

1. **`<brand>_<ver>_x64_arm64.msixbundle`** — `winapp package` over the two release
   publish folders (`artifacts/release/publish/win-{x64,arm64}`), signed with
   `artifacts/release/cert/devcert.pfx`. **The retail identity does not travel in the
   publish folders** — they contain no `AppxManifest.xml`; it lives in the
   release-stamped RID-qualified manifests under `obj/`, which the single-arch release
   targets pass with an explicit `--manifest` (DIST-7: manifests are explicit, never
   auto-detected). The producer must supply each architecture's release-stamped manifest
   the same way; how two manifests map onto the one bundle invocation is resolved
   empirically against `winapp package --help` at execution (candidates: a per-input
   manifest form, or bundling the two already-built release `.msix`es, which embed
   them). Never let the bundle pick up a dev-stamped manifest; prove the identity by
   offline inspection (AC2) before any live step.
2. **`devcert.cer`** — the public cert only. The `.pfx` must never enter the kit.
3. **One `atv-plugin-<host>.zip` per `integrations/<host>/` directory**, enumerated
   automatically (no per-host special-casing; future legs join by existing). Contents =
   the working tree's **git-tracked** files for that subtree (e.g. via `git ls-files`) —
   uncommitted edits ship (it's a dogfood of the current state) but gitignored files
   cannot, which is what keeps a working-tree `atv-command.txt` override (phase 20) out
   of a recipient's plugin.
4. **READMEs** — one kit-level plus whatever per-piece minimum is needed: factual
   bootstrap only (what to run, in what order), no product pitch. Doc-style skill
   governs the prose. **All instructions must be zip-relative** — a recipient holds only
   the expanded kit, so the shipped `integrations/<host>/README.md` files (whose install
   commands assume a cloned repo: repo-relative paths, GitHub slugs) are NOT the manual
   fallback; the kit README carries its own standalone commands. The Copilot piece's
   README states that auto-wiring is pending verification (phase 24) and what exists
   today.
5. **`install.ps1` / `uninstall.ps1`** — authored in-repo (e.g. `build/dogfood/`) as
   templates and **stamped at kit-build time from the brand/command MSBuild properties**
   (`$(AtvBrandName)` / `$(AtvCommandName)`), so no identity or command string is
   hand-typed twice (standing invariant #2 — the scripts run standalone on machines with
   no repo, so stamping at build is the only way to derive rather than re-literal).

Re-running with nothing changed is a no-op (MSBuild Inputs/Outputs, same discipline as
the release targets).

## Part 2 — Installer (runs on the recipient's machine)

Sequenced, with plain-language output at each step:

1. **Explain, then trust the dev cert.** Before any elevation prompt, print *why*: the
   build is signed with a temporary development certificate, trusting it needs one admin
   action, and it is removable later (the uninstaller offers it). Then
   `Import-Certificate` the `.cer` into `LocalMachine\TrustedPeople` (per
   `docs/release.md` §3.1) — the one elevation, once per cert.
2. **Install the bundle** — `Add-AppxPackage` on the `.msixbundle`; per-user, no admin
   once the cert is trusted. Verify with a fresh `atv doctor` read-back (registered
   retail identity), not just the cmdlet succeeding.
3. **For each integration with a VERIFIED wiring recipe whose host is detected — in this
   phase, Claude Code only** (`Get-Command claude`): **prompt, then wire** via
   `claude plugin marketplace add <dir>` + `claude plugin install
   atv-integration@agent-task-void`, from the expanded plugin zip's local dir. Prompted
   because it writes host-security-relevant config (DIST-11's trust-surface note). Host
   absent or declined → skipped, pointing at the **kit's own zip-relative** README for
   the manual path. Structure the recipe set as per-host data so phase 24's Copilot
   entry is additive, not a rewrite; an integration without a verified recipe (Copilot,
   for now) is never auto-wired, only shipped.

## Part 3 — Uninstaller (symmetric)

1. Remove wired plugins via each host's own uninstall (marketplace remove + plugin
   uninstall), for hosts in the installer's verified recipe set that are detected
   present (Claude Code in this phase; Copilot joins with phase 24).
2. Remove the package: `Get-AppxPackage -Name "<brand>"` — the **exact retail Name**,
   never a bare `*<brand>*` wildcard (a wildcard would also sweep dev/test/reltest pools
   on a developer's box; `docs/release.md` §3.5 precedent). Removal drops the taskbar
   cards and app-data (DIST-9).
3. **Prompt** whether to also remove the trusted dev cert (elevation again if yes);
   default is leave it.

The kit README states the standing consequence: the future real-cert release (DIST-2)
changes the Publisher → PFN, so dogfooders must run this uninstall before installing the
eventual real release.

## Files affected

```
build/Atv.Dogfood.targets            # new: -t:AtvDogfood producer (bundle, cer, zips, readmes, script stamping)
build/dogfood/install.ps1            # new: installer template (brand-stamped at build)
build/dogfood/uninstall.ps1          # new: uninstaller template (brand-stamped at build)
build/dogfood/README-template(s).md  # new: kit-level bootstrap readme(s)
docs/release.md                      # + a short section: the dogfood kit vs the release pipeline, when to use which
CLAUDE.md                            # + one line in the release-build area pointing at the kit target
tests (see AC3)                      # producer-output verification, where automatable
```

Exact template layout/naming under `build/dogfood/` and zip naming are build details.

## Acceptance criteria (written first)

Automated unless marked **LIVE**. Live conduct follows phase 21's INFRA-33 rules.

1. **One command, complete kit.** `-t:AtvDogfood` from a clean tree yields
   `artifacts/dogfood/` containing exactly: the signed dual-arch `.msixbundle`, the
   `.cer`, one zip per `integrations/*` directory, the README(s), and both scripts.
   Re-run with no changes = no-op.
2. **Bundle correctness — proven offline, before any live step.** Unpack/inspect the
   bundle: it contains both architecture packages, and **each** package's manifest
   carries the retail Name with alias `atv` (never a dev/pathhash or `-reltest` stamp —
   this is the check that catches the manifest-plumbing hazard in Part 1 item 1);
   `Get-AuthenticodeSignature` shows it signed by the dev cert. The LIVE `doctor`
   read-back later corroborates but never substitutes. **No `.pfx` anywhere under
   `artifacts/dogfood/`.**
3. **Leak-proof zips.** Each zip's file list equals the git-tracked file list for its
   subtree. Regression proof: with a decoy gitignored
   `integrations/claude-code/plugins/atv-integration/atv-command.txt` present in the
   working tree, the produced zip does **not** contain it.
4. **Brand derivation.** The checked-in script/README templates contain no literal brand,
   identity, or alias strings (grep-proof against the `Branding.cs` constants); the
   stamped outputs in the kit do. Uninstall's package filter is the exact-Name form.
5. **Script safety review.** Installer prompts before any plugin wiring and prints the
   elevation rationale before the cert step; uninstaller prompts before cert removal;
   neither script contains a bare-wildcard package operation. (Script logic beyond what
   AC3/AC4 automate is verified by review + AC6 — the repo has no PowerShell test
   harness, and building one is out of scope.)
6. **LIVE — supervised end-to-end smoke** (operator-supervised; preferably a VM or
   secondary machine — on the operator's own box the bundle upgrades the daily driver in
   place, same PFN, which is explicitly not the kit's target scenario): run `install.ps1`
   → one explained elevation, bundle installs, fresh-shell `atv doctor` reports the
   retail identity, the **Claude Code** host prompts and wires, a card renders from a
   host session or manual `atv` call. Then `uninstall.ps1` → plugin unwired, package
   gone, cards vanish, cert prompt honored. On a dev-pool machine: dev/test/reltest
   packages verified untouched. (No Copilot leg in this smoke — phase 24.)
7. **Docs.** `docs/release.md` and the kit README(s) follow the doc-style skill; every
   kit-README instruction is zip-relative (no repo paths, no GitHub slugs); the
   uninstall-before-real-release consequence is stated; the Copilot piece carries its
   pending-auto-wiring note; suites + NativeAOT publish stay green (the producer target
   must not perturb the normal build).

## Out of scope

- DIST-2 (real cert), DIST-10/11's published channels (winget, host marketplaces) — this
  kit precedes them and does not supersede them.
- The operator's own daily-driver install (phase 20's migration checklist owns that).
- Any auto-update, telemetry, or feedback-collection mechanism — the kit is hand-off
  collateral only.
- A PowerShell unit-test harness for the scripts (review + live smoke per AC5/AC6).
- **The Copilot CLI auto-wiring leg and its verification — phase 24** (split out
  2026-07-19, operator direction: no current Copilot access; keeping it separate leaves
  this phase's completion state legible instead of part-done).
- Codex or other not-yet-implemented host legs (INFRA-31 — they join the zip enumeration
  automatically when their `integrations/<host>/` tree lands; auto-wiring additionally
  requires a verified recipe, per the Part 2 structure).
