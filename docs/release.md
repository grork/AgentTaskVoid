# Release runbook

Covers producing a signed full-MSIX release build for both architectures, the
dev-cert-vs-real-cert distinction, and the supervised install/upgrade/uninstall
verification steps. Real certificate acquisition and winget submission remain
deferred (DIST-2); this doc notes where each would happen without performing
them.

## 1. Build + sign (no admin required)

```
dotnet build src\Atv\Atv.csproj -t:AtvRelease
```

Run from the repo root. Publishes NativeAOT `atv.exe` for both `win-x64` and
`win-arm64`, packages each against its stamped `obj\` manifest
(`build/Atv.Package.targets`, explicit `--manifest`, never auto-detected — see
[`build/Atv.Release.targets`](../build/Atv.Release.targets)), and signs both with a throwaway self-signed dev
certificate. Re-running with nothing changed is a no-op (MSBuild's Inputs/Outputs
skip-detection on the two per-arch targets).

Artifacts (gitignored):

```
artifacts\release\cert\devcert.pfx / .cer   -- self-signed, CN=AppTaskInfoCli
artifacts\release\publish\win-x64\atv.exe
artifacts\release\publish\win-arm64\atv.exe
artifacts\release\msix\Codevoid.AgentTaskVoid_<version>_x64.msix     -- signed
artifacts\release\msix\Codevoid.AgentTaskVoid_<version>_arm64.msix   -- signed
```

This stamps the release identity (`Identity/@Name = Codevoid.AgentTaskVoid`,
`AppExecutionAlias = atv`) into both packages. Section 2 installs this msix
on a dev box as the daily driver; section 3 uses a third, throwaway
`-reltest` identity instead, so upgrade/uninstall verification churn never
touches the daily cards.

### The `-reltest` throwaway smoke variant

```
dotnet build src\Atv\Atv.csproj -t:AtvRelease -p:AtvVerifyIdentity=true
```

Adds one property to the same pipeline, producing distinctly-named
artifacts that never collide with (or up-to-date-skip against) the real
release msix above:

```
artifacts\release\msix\Codevoid.AgentTaskVoid_<version>_x64_reltest.msix     -- signed
artifacts\release\msix\Codevoid.AgentTaskVoid_<version>_arm64_reltest.msix   -- signed
```

stamped with `Identity/@Name = Codevoid.AgentTaskVoid-reltest`,
`AppExecutionAlias = atv-reltest` — a package that installs and launches
independently of both the real release identity and the dev-interactive one.
Section 3 installs this artifact.

Prerequisite: both publishes need the matching Visual Studio VC build tools
component installed — `Microsoft.VisualStudio.Component.VC.Tools.x86.x64` for
`win-x64`, `Microsoft.VisualStudio.Component.VC.Tools.ARM64` for `win-arm64`.
See [`CLAUDE.md`](../CLAUDE.md)'s NativeAOT section for the check and the
failure symptom.

To bump the version for a new build (e.g. to exercise an upgrade-in-place),
make any commit — NBGV's `$(BuildVersion)` is git-height-derived, so height
advances automatically; there is no version file to hand-edit.

### Dev cert vs. real cert

`AtvReleaseCert` (inside `build/Atv.Release.targets`) runs `winapp cert
generate --if-exists Skip`, producing a throwaway, self-signed certificate
(`CN=AppTaskInfoCli`, matching `Package\AppxManifest.template.xml`'s static
`Identity/@Publisher` exactly — required for the package to be installable
once the cert is trusted). This certificate:

- Confirms the pack/sign/install/alias-launch/`AppTaskInfo` path works end
  to end.
- Is not suitable for distribution. A self-signed cert requires per-machine
  admin trust-store installation, a worse ask than Windows Developer Mode;
  DIST-2 rejected it as a distribution mechanism for that reason.
- Build automation never installs it into any machine trust store.
  Trusting it (`winapp cert install` / `Import-Certificate`) is the one step
  in this runbook that needs elevation; a human does it, in section 3.

**Ship-time step, still deferred (DIST-2):** replacing the dev cert with a
real one, wiring `AtvReleaseCert`'s `--cert`/`--publisher` to it, and
updating `Package\AppxManifest.template.xml`'s `Identity/@Publisher` to
match — it's currently the placeholder `CN=AppTaskInfoCli`, and a real
cert's subject will very likely differ. This changes the Package Family
Name (see the isolation note in section 2). DIST-2 evaluates the
certificate options.

## 2. Daily-driver install: the retail msix on this dev box

A Package Family Name is `<Name>_<PublisherId>`, where `PublisherId` is a
deterministic hash of the manifest's declared `Identity/@Publisher` string,
independent of which certificate signs the package. Dev-interactive, release,
and `-reltest` each stamp a distinct `Identity/@Name`
(`build/Atv.Package.targets`' `AtvStampAppxManifest` is build-kind-aware —
see [`CLAUDE.md`](../CLAUDE.md)'s "Package identity" section and DIST-3), so all three
produce structurally different PFNs and can coexist on one machine.

Dev-interactive claims the `atv-dev` alias; the real release build claims
`atv` (DIST-12) — the identity behind the operator's own daily taskbar card
use, installed on this dev box the same way an end user installs it. Install
the plain release msix (not `-reltest`) as the daily driver:

```
Add-AppxPackage -Path "artifacts\release\msix\Codevoid.AgentTaskVoid_<version>_<arch>.msix"
```

Trust the dev cert first if this dev box hasn't already (section 3.1's
elevation step — the same certificate signs every dev-cert msix, release or
`-reltest`). From a freshly-spawned shell, `atv doctor` should report the
Release identity (Name = the bare brand, unmarked — no `(dev)`), coexisting
with `atv-dev doctor`'s dev-interactive identity, with no alias contention.
This is the interim retail install until DIST-2 swaps in a real signing
certificate, which changes the Publisher — and with it the PFN, so state
starts fresh at that point.

`-reltest` keeps its own `atv-reltest` alias and its throwaway-smoke role;
section 3 below installs that variant instead, for supervised
upgrade/uninstall verification rather than daily use.

## 3. Supervised install / upgrade / uninstall verification

This is the command sequence a human runs, supervised. One step needs
elevation (trusting the dev cert); the rest is per-user, no admin.

### Architecture note

Verification below targets whichever architecture is native to the machine
running it (e.g. these steps were last run against native arm64; x64
builds clean and is Authenticode-signed, but nobody has functionally
verified it on real x64 hardware — none was available at authoring time).

### 3.1 Trust the dev cert (elevation required, once per cert)

```
Import-Certificate -FilePath "artifacts\release\cert\devcert.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

(Run from an elevated PowerShell. `winapp cert install artifacts\release\cert\devcert.pfx`
is the equivalent single-command form if preferred — also needs elevation.)

### 3.2 Install the signed `-reltest` package (per-user, no admin once trusted)

Install the `-reltest` artifact, not the plain release one. Per section 2,
it has its own non-colliding `atv-reltest` alias, so this install cannot
disturb the `atv-dev` binding of a dev package already registered on the box,
or the retail install holding `atv`.

```
Add-AppxPackage -Path "artifacts\release\msix\Codevoid.AgentTaskVoid_<version>_<arch>_reltest.msix"
```

### 3.3 Identity, API, alias-on-PATH — from a freshly-spawned `cmd.exe`

A brand-new `cmd.exe` (not a shell that's been running throughout the
session) is required so the `%LOCALAPPDATA%\Microsoft\WindowsApps` alias
resolves without manually reopening a terminal. The command name is
`atv-reltest`, not `atv`:

```
cmd.exe /c "atv-reltest doctor"
```

Confirm the printed identity line ends `Codevoid.AgentTaskVoid-reltest` (structurally
distinct from the dev-interactive PFN), with a trailing `(dev)` marker. That
marker is expected: `BuildKindResolver` classifies `-reltest` as dev-pool
(its Name is neither the bare brand nor `<brand>.Test.*`); the only unmarked
identity is the real release build (Name = bare `Codevoid.AgentTaskVoid`). Also
confirm `api: AppTaskInfo.IsSupported() -> true`, and that `config file:` /
`app-data folder:` / `sidecar dir:` / `log file:` all resolve under this
package's own `LocalState`, distinct from both the dev worktree's and the
real release identity's.

```
cmd.exe /c "atv-reltest start s1 --title Hi"
cmd.exe /c "atv-reltest list --json"
```

Confirm a card renders on the taskbar and `list --json` reports it. Confirm
the watchdog spawns (`cmd.exe /c "atv-reltest doctor"` again — `watchdog:
running`, or check `Get-Process atv-reltest`).

### 3.4 Upgrade-in-place with a live watchdog

1. With the watchdog from 3.3 still running (a live card keeps it up), commit
   any trivial change (NBGV's `$(BuildVersion)` is git-height-derived — any
   commit bumps it) and re-run section 1's `-p:AtvVerifyIdentity=true` build
   command to produce v(N+1) of the `-reltest` artifact.
2. `Add-AppxPackage -Path "artifacts\release\msix\Codevoid.AgentTaskVoid_<new-version>_<arch>_reltest.msix"`
   over the running watchdog.
3. Expected: install succeeds; if the watchdog holds `atv.exe` open, Windows
   may defer registration (`ERROR_PACKAGES_IN_USE` fallback) rather than fail
   outright — DIST-6's accepted default behavior. The old watchdog keeps
   supervising; the next invocation of `atv-reltest` runs the new version once
   the old watchdog self-exits (empty supervised set) and Windows completes
   the deferred registration.
4. Confirm via `cmd.exe /c "atv-reltest doctor"` (or `Get-AppxPackage
   *Codevoid.AgentTaskVoid-reltest* | Select Version`) that the new version becomes
   active, with no corruption of existing card/sidecar state.

### 3.5 Uninstall with live cards + a running watchdog

Filter specifically on the `-reltest` Name. A bare `*Codevoid.AgentTaskVoid*` wildcard
would also match (and remove) the dev-interactive `Codevoid.AgentTaskVoid-<hash>`
registration and, if also installed on this box, the real release
`Codevoid.AgentTaskVoid` package. Never use the bare wildcard on a dev box.

```
Get-AppxPackage -Name "*Codevoid.AgentTaskVoid-reltest*" | Remove-AppxPackage
```

Expected: the taskbar card(s) disappear immediately (the Shell drops them on
uninstall — DIST-9); the package's app-data tree is gone; the watchdog
process self-exits on its next empty-set poll (give it a few seconds, then
`Get-Process atv-reltest -ErrorAction SilentlyContinue` should come back
empty). Confirm the dev-interactive binding (`cmd.exe /c "atv-dev doctor"`,
a fresh terminal) is untouched throughout.

### 3.6 Cleanup

```
Get-AppxPackage -Name "*Codevoid.AgentTaskVoid-reltest*" | Remove-AppxPackage   # if 3.5 wasn't already run
Remove-Item -Path "Cert:\LocalMachine\TrustedPeople\<thumbprint from devcert.cer>" -Confirm:$false
```

Confirm the smoke install is fully gone: no `*Codevoid.AgentTaskVoid-reltest*` package
(`Get-AppxPackage`), no leftover
`%LOCALAPPDATA%\Packages\Codevoid.AgentTaskVoid-reltest_*` folder, no `StartupTask`
entry for it (Settings > Apps > Startup, or `Get-StartupTask`), no stray
`atv-reltest.exe` process. The dev-interactive `Codevoid.AgentTaskVoid-<hash>` package
should still be present (`Get-AppxPackage -Name "*Codevoid.AgentTaskVoid-<hash>*"`) —
this cleanup must never touch it.

## 4. winget manifest set

`build/winget/manifests/c/Codevoid/AgentTaskVoid/<version>/` — the standard
community `winget-pkgs` multi-file layout (version / installer /
`locale.en-US` manifests), authored against the artifacts this pipeline
produces. `winget validate` reports "Manifest validation succeeded." against
the current snapshot.

`InstallerSha256` values are real hashes of the dev-cert-signed `.msix`
files. `InstallerUrl` points at this repo's GitHub Releases URL convention
(`github.com/grork/AppTaskInfoCli/releases/download/v<version>/...`) for a
tag that does not exist yet — a snapshot manifest, not a submission.
Regenerate the three files under a new version folder for each real release
rather than hand-editing an old snapshot back into currency.

**Ship-time step, still deferred (DIST-2):** cutting a real GitHub release
with the real-cert-signed artifacts at the URLs above, then `winget submit`
(or a PR against `microsoft/winget-pkgs`) using this manifest set as the
starting point.

## 5. The finalized winget package id

`Codevoid.AgentTaskVoid` — `Codevoid.AgentTaskVoid.Diagnostics.DoctorChecks.WingetPackageId`
(`src/Atv/Diagnostics/DoctorChecks.cs`), brand-derived
(`{Branding.IdentityName}.{PascalCase(Branding.Command)}` — never re-literaled).
`doctor`'s not-installed remedy line and `Codevoid.AgentTaskVoid.Diagnostics.Capability.Check`'s
identity-absent failure reason both print this exact string.
`build/winget/manifests/.../Codevoid.AgentTaskVoid*.yaml`'s `PackageIdentifier`
matches it exactly; there is no automated cross-check between the two files,
since the manifest lives outside the compiled product.

## 6. The dogfood kit: handing this CLI to another machine

```
dotnet build src\Atv\Atv.csproj -t:AtvDogfood
```

Produces a per-version kit folder `artifacts\dogfood\<version>\` (gitignored):
a signed dual-arch `.msixbundle` stamped with the retail identity, one
`atv-plugin-<host>.zip` per `integrations\<host>\` directory (the working
tree's git-tracked files only, so a gitignored override like `atv-command.txt`
never ships), `install.cmd`, `install.ps1`, `uninstall.cmd`, `uninstall.ps1`,
and a README — everything a recipient needs, with no clone of this repository.
It chains onto section 1's per-arch publish/pack/sign work instead of repeating
it, and signs with the same throwaway development certificate.

The version is in the folder name, so a kit from an earlier build is never
mistaken for the current one; hand off the folder whose version matches the
build you just ran, and delete old version folders when you no longer need
them.

The kit ships no certificate file. `install.ps1` reads the signer from the
bundle's own Authenticode signature and asks before trusting it — one
elevation, explained before it happens. A recipient runs `install.ps1` /
`uninstall.ps1` directly, or the matching `.cmd` to launch it with the right
execution policy when running from a file share or by double-click.

Use this kit to hand the build to someone else's machine — a VM, a
secondary machine, another person's PC. Use section 2's plain release msix
for this dev box's own daily driver, and section 3's `-reltest` variant for
upgrade/uninstall verification here. Installing the kit's bundle on this
dev box would upgrade the daily driver in place (same PFN as section 2), so
don't run `install.ps1` on this machine.

Run `uninstall.ps1` before installing a future real-cert release (DIST-2): a
different signing certificate means a different package identity, so an old
dogfood install and the eventual real-cert install can't upgrade in place.
