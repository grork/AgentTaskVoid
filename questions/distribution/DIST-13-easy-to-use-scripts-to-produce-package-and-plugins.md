# DIST-13: Easy-to-use scripts that produce package and plugins for sharing & dogfooding
**Status:** DECIDED (2026-07-19)
**Plan:** unplanned
**Decision:** Build a one-command **dogfood distribution kit** — the pre-publication hand-off
path. It is the interim sibling to DIST-10 ("Getting `atv` onto the machine…") and DIST-11 ("How
the per-host integration artifact is delivered…"), whose published channels (winget, host
marketplaces) are both gated on DIST-2 ("Signing / certificate acquisition") and so don't exist
yet. This kit lets you hand an **unreleased** build to someone for end-to-end feedback today. It
does **not** supersede DIST-10/11 — when the real cert lands, those become the real adoption story
and this stays the pre-publication tool.

It reuses what already ships: `-t:AtvRelease` (`build/Atv.Release.targets`) already produces the
per-arch signed `.msix` + the throwaway dev cert, and `docs/release.md` §3 is already the manual,
supervised version of the install flow this automates.

### Producer — one build target (parallels `-t:AtvRelease`)
Emits a single shareable output folder (e.g. `artifacts/dogfood/`, gitignored) containing:
1. **One dev-signed `.msixbundle`** spanning x64 + arm64 (`makeappx bundle` over the two per-arch
   release `.msix`, then signed with the same throwaway dev cert) — a single file that installs on
   either architecture. Stamped with the **retail identity** (Name = `Agentaskvoid`, alias `atv`),
   **not** `-reltest`: the plugins invoke bare `atv` (DIST-12, "Daily driver on the retail
   identity…"), so the alias must be `atv`, and dogfooders should exercise the literal release
   artifact.
2. **The dev cert's public `.cer`** (never the `.pfx` — trusting the signer needs only the public
   cert).
3. **One plugin zip per implemented integration** — today `claude-code` and `copilot-cli` (each
   the `integrations/<host>/` tree). More integrations join this set automatically as their legs
   land; no per-host special-casing.
4. **Minimal, factual READMEs** — just enough to bootstrap (what to run, in what order), no product
   pitch (the doc-style skill governs the prose).
5. **The install/uninstall scripts** themselves (PowerShell — repo convention).

### Installer — ships in the bundle, run by the recipient
- **Trust the dev cert**, preceded by a clear plain-language explanation of *why* elevation is
  requested (the operator's emphasis) — this is the one elevation, once per cert
  (`Import-Certificate` into `LocalMachine\TrustedPeople`, per `docs/release.md` §3.1).
- **Install the `.msixbundle`** (`Add-AppxPackage`) — per-user, no admin once the cert is trusted.
- **For each implemented integration in the bundle whose host is detected present, prompt then
  wire its plugin** via that host's own local install mechanism (Claude Code: local-marketplace
  add + `plugin install atv-integration@agentaskvoid`, `integrations/claude-code/README.md`
  Option B; Copilot CLI: its `.github/plugin/marketplace.json` equivalent). Prompted/consented
  because it writes host-security-relevant config (DIST-11's trust-surface note). A host that
  isn't installed is skipped; its README covers the manual path. (Operator reframe, 2026-07-19:
  this is not a separate decision and not a one-host special case — "for each available
  integration, install it"; Copilot CLI is implemented alongside Claude Code.)

### Uninstaller — ships in the bundle
- Removes the wired plugins (each host's own uninstall) and the `atv` package
  (`Remove-AppxPackage`, filtered to the retail Name — never a bare `*Agentaskvoid*` wildcard,
  per `docs/release.md` §3.5).
- **Prompts** whether to also remove the trusted dev cert.

### Consequences / notes
- **Uninstall before the real release.** Because the eventual real cert (DIST-2) changes the
  Publisher → the Package Family Name changes, a dogfooder must uninstall this kit before
  installing the future real-cert release. The uninstall script is exactly that step.
- **Targets external dogfooders.** The kit's retail identity (Name = `Agentaskvoid`, alias `atv`)
  is the SAME PFN + alias as DIST-12's daily driver install. For an external dogfooder (no prior
  install) that's clean. On the operator's own box, installing the kit would replace/upgrade the
  daily driver in place (same PFN) — not what the kit is for; it targets other people's machines.
  It does NOT contend with the dev-interactive pool, which DIST-12 moved to alias `atv-dev` (so
  the stale "dev-interactive owns `atv`" framing in `docs/release.md` §2 no longer applies).

### Build-time details (not decision blockers)
- The producer target's name; output-folder path and zip naming; README wording. All downstream
  of `-t:AtvRelease`'s existing per-arch outputs. (The bundle invocation and the per-host wiring
  commands are pinned in the correction below — they were *not* the open build details this line
  originally implied.)

## Post-review correction (2026-07-19) — two flagged concerns were false; mechanisms pinned
An independent review raised two objections to this decision; both were checked against the
actual tools and **dismissed** — they were static/incomplete inferences, corrected here so the
record doesn't carry them forward.

- **The `.msixbundle` builds in one command — no `makeappx`, no manifest change.** `winapp
  package` accepts **multiple input folders and emits a `.msixbundle`** directly: `winapp package
  <win-x64 publish> <win-arm64 publish> --output …_x64_arm64.msixbundle` (verified via `winapp
  package --help`; its default bundle output name embeds `<arch1>_<arch2>`). winapp **derives each
  package's architecture from the RID-specific publish folder** at pack time — these are
  arch-specific NativeAOT binaries — so the absence of a `ProcessorArchitecture` attribute in
  `AppxManifest.template.xml` is irrelevant. The review's "both `.msix` are architecture-neutral,
  can't be bundled" claim read only the template and never inspected a packed `.msix`; empirically
  a hand-built bundle installs and arch-selects correctly on both x64 and arm64 (operator,
  2026-07-19). So the producer just runs `winapp package` over the two publish folders `-t:AtvRelease`
  already emits, then signs the bundle.
- **Copilot CLI has a local, persistent install — the hosts ARE symmetric.** `copilot plugin
  marketplace add <local dir>` + `copilot plugin install atv-integration@<marketplace>` installs
  persistently from a local path (confirmed in Microsoft Learn's Copilot-CLI plugin docs, which
  clone a marketplace repo locally and `marketplace add` it — the same `.github/` + `plugins/`
  layout as `integrations/copilot-cli/`). This is directly analogous to Claude Code's
  local-marketplace Option B, so "for each implemented integration, install it" holds with **no
  per-host capability asymmetry**. (`copilot --plugin-dir <path>` is a *launch-time dev flag* that
  loads a plugin folder for one session bypassing the installed-plugin cache — not an install
  mechanism, irrelevant to the kit.) The review's "no local persistent install" claim, and this
  file's original "Copilot's exact command a build detail," both came from the integration
  README's incomplete Install section.

**Net wiring commands the installer runs, per detected host (both persistent, both from the
bundled local plugin dir):**
- Claude Code: `claude plugin marketplace add <dir>` + `claude plugin install atv-integration@agentaskvoid`.
- Copilot CLI: `copilot plugin marketplace add <dir>` + `copilot plugin install atv-integration@<marketplace>`.

The two ERGO BLOCKERs the same review raised (icon/deep-link reverted on update) were **confirmed
in code and stand** — see ERGO-34/ERGO-35's own post-review corrections; they don't touch DIST-13.

## Question
While seeking feedback on our current feature set, sharing with people is somewhat difficult due to
the current dev-signed packages. But it's also clear that to have people evaluate our current
functionality end to end, they also need a plugin for their agent harness. Given 'please install
from a marketplace' requires the full stack to be published, this is a challenge.

Ideally, we would have a script that would do the following:
1. Produce a dev-signed, but-release focused msixbundle for arm64 & amd64
2. Generated discrete zip files for the different plugins
3. Include simple-and-easy-to-follow readme.md's that contained *just enough information* for
   for people to bootstrap (E.g. it's factual about the minimum they need to do; not selling them
   or trying to explain the product/feautures
4. Bundle all the collateral into an output folder that could be shared with people easily

Additionally, we should include a script that takes on the work of installing a bundled dev
cert, and then installing the msixbundle for them. This should be 'simple', and provide enough
output so the users can understand why they're being asked to approve an elevation request.

If possible -- e.g. in addition to the above -- a script that could detect what harnesses were
installed, and auto-install the plugins to them. This should be a prompt request on the install
script so that the user can decide if they want to follow that.

There should be an uninstall script that would seamlessly remove the plugins, and the bundle.
They should be prompted if they want to remove the dev cert.

This is all in the aim of someone wanting to dogfood an **unreleased** version of the app + plugins.

## Scope note
Filed OPEN (operator, dogfooding); DECIDED 2026-07-19. In scope: directly serves the current
pre-publication feedback phase. Related: DIST-10 ("Getting `atv` onto the machine…" — the published
engine path this precedes), DIST-11 ("How the per-host integration artifact is delivered…" — the
published plugin path; its trust-surface + local-fallback notes), DIST-12 ("Daily driver on the
retail identity…" — why the bundle uses the retail `atv` alias), DIST-2 ("Signing / certificate
acquisition" — the deferred real cert whose arrival ends this kit's role), DIST-9 ("Uninstall
behavior…" — uninstall symmetry). Primary reference: `docs/release.md` (the `-t:AtvRelease`
pipeline + the manual supervised install/uninstall this automates).
