# DIST-10: Getting `atv` onto the machine when a host plugin/integration is published
**Status:** OPEN

## Question
When we publish the host integrations (the Claude Code hook config, later Copilot CLI / Codex)
as a distributable "plugin," the user still needs the `atv` binary **present and registered**
on their machine for the hooks to do anything. Asking them to *separately* install a program
(the MSIX) may be too high a bar and defeats the "paste a hook config" simplicity. How does
`atv` get onto the machine as part of — or triggered by — adopting the host integration?

## Why this surfaced
Operator, 2026-07-10, thinking ahead from the phase-13 Claude Code leg to publishing it. The
hook config is trivial to adopt (paste JSON), but it silently no-ops without `atv` installed
(by design — every hook guards on `Get-Command atv`). The gap between "added the hooks" and
"has the tool" is exactly where adoption will leak.

## What makes it non-trivial (constraints)
- **`atv` cannot be a loose exe dropped next to a plugin.** It needs **package identity** (a
  full MSIX) to call `AppTaskInfo` at all (the whole DIST-1 "end-user distribution vehicle"
  decision) — so "bundle a portable exe with the plugin" does not work; it must be a
  *registered package*. This is the crux that forces a real install and raises the bar.
- **The self-signed-cert trust step is the current sharp edge.** Today installing the signed
  MSIX needs the dev cert trusted first — the one elevation we hit in the phase-12 smoke. A
  real code-signing cert (DIST-2, "Signing / certificate acquisition", DEFERRED) removes that
  per-machine trust elevation and materially lowers the bar; until then, sideloading is
  admin-gated.
- **winget publication is deferred** (DIST-2; `docs/release.md` §4/§5): `winget install
  Agentaskvoid.Atv` is the intended low-friction path but the manifest isn't submitted yet, so
  it doesn't work for end users today.
- **Plugin/marketplace mechanics vary per host** and move — a Claude Code plugin marketplace
  may or may not support declaring an external dependency or running an install step; Copilot
  CLI / Codex differ again. Anything host-specific must stay in the per-host artifact, not in
  `atv` (LIFE-10/11).
- **Graceful degradation already exists**: hooks no-op cleanly if `atv` is absent, and
  `doctor` prints the `winget install Agentaskvoid.Atv` remedy — so the failure mode is
  "nothing happens," not breakage. The question is how to convert that into "it just works."

## Options to explore later (NOT deciding now)
1. Depend on winget: the plugin's install/readme is simply `winget install Agentaskvoid.Atv`
   (blocked on DIST-2 publication). Lowest bar once it exists; cross-host uniform.
2. Plugin bundles the signed MSIX + a bootstrap that runs `Add-AppxPackage` (needs a *trusted*
   cert → DIST-2 real cert to avoid the elevation).
3. A host plugin-marketplace dependency/hook mechanism, where available, that installs/points
   at `atv` on adoption.
4. Accept the two-step (install `atv`, then adopt hooks) but make the first step one command and
   lean on the no-op-if-absent guard + `doctor` remedy line to guide stragglers.
5. Store-signed distribution (Microsoft Store) so install + trust are handled by the Store —
   weigh against the winget path.

## Scope note
Filed OPEN (operator, 2026-07-10); does not change the current build (integration + release
plumbing already shipped). Strongly coupled to DIST-2 (real cert acquisition — its resolution
removes the trust-elevation bar) and DIST-1 (the MSIX/identity requirement that creates the
bar). Related: DIST-4 (zero-pre-install-script posture), LIFE-11 (shipping per-host artifacts).
