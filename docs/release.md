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
artifacts\release\msix\Agentaskvoid_<version>_x64.msix     -- signed
artifacts\release\msix\Agentaskvoid_<version>_arm64.msix   -- signed
```

This stamps the release identity (`Identity/@Name = Agentaskvoid`,
`AppExecutionAlias = atv`) into both packages. Section 2 covers why a
supervised smoke test installs a third, throwaway identity instead of these
two artifacts directly.

### The `-reltest` throwaway smoke variant

```
dotnet build src\Atv\Atv.csproj -t:AtvRelease -p:AtvVerifyIdentity=true
```

Adds one property to the same pipeline, producing distinctly-named
artifacts that never collide with (or up-to-date-skip against) the real
release msix above:

```
artifacts\release\msix\Agentaskvoid_<version>_x64_reltest.msix     -- signed
artifacts\release\msix\Agentaskvoid_<version>_arm64_reltest.msix   -- signed
```

stamped with `Identity/@Name = Agentaskvoid-reltest`,
`AppExecutionAlias = atv-reltest` — a package that installs and launches
independently of both the real release identity and the dev-interactive one.
Section 3 installs this artifact.

Prerequisite on an ARM64 machine: `win-x64` is a cross-AOT publish, and
NativeAOT's cross-linking step shells out to `vswhere.exe`, which is not on
PATH by default. Add it for the session before running the command above:

```
$env:PATH += ";C:\Program Files (x86)\Microsoft Visual Studio\Installer"
```

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

## 2. Why the real release msix isn't installed on this dev box

A Package Family Name is `<Name>_<PublisherId>`, where `PublisherId` is a
deterministic hash of the manifest's declared `Identity/@Publisher` string,
independent of which certificate signs the package. Dev-interactive, release,
and `-reltest` each stamp a distinct `Identity/@Name`
(`build/Atv.Package.targets`' `AtvStampAppxManifest` is build-kind-aware —
see [`CLAUDE.md`](../CLAUDE.md)'s "Package identity" section and DIST-3), so all three
produce structurally different PFNs and can coexist on one machine.

Dev-interactive and the real release build do declare the same
`AppExecutionAlias` (`atv`): a dev box's primary `atv` is the working copy,
and a real end-user machine (which never has dev-interactive installed) gets
`atv` as the release binary. Installing the real release msix on this dev
box, alongside the dev-interactive registration, would put two packages in
alias contention over `atv`. The `-reltest` variant's distinct
`atv-reltest` alias avoids that, so you can run the
install/alias-launch/`AppTaskInfo` sequence below on a dev box without
disturbing the live `atv` = dev-interactive binding.

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
disturb a dev-interactive `atv` binding already registered on the box.

```
Add-AppxPackage -Path "artifacts\release\msix\Agentaskvoid_<version>_<arch>_reltest.msix"
```

### 3.3 Identity, API, alias-on-PATH — from a freshly-spawned `cmd.exe`

A brand-new `cmd.exe` (not a shell that's been running throughout the
session) is required so the `%LOCALAPPDATA%\Microsoft\WindowsApps` alias
resolves without manually reopening a terminal. The command name is
`atv-reltest`, not `atv`:

```
cmd.exe /c "atv-reltest doctor"
```

Confirm the printed identity line ends `Agentaskvoid-reltest` (structurally
distinct from the dev-interactive PFN), with a trailing `(dev)` marker. That
marker is expected: `BuildKindResolver` classifies `-reltest` as dev-pool
(its Name is neither the bare brand nor `<brand>.Test.*`); the only unmarked
identity is the real release build (Name = bare `Agentaskvoid`). Also
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
2. `Add-AppxPackage -Path "artifacts\release\msix\Agentaskvoid_<new-version>_<arch>_reltest.msix"`
   over the running watchdog.
3. Expected: install succeeds; if the watchdog holds `atv.exe` open, Windows
   may defer registration (`ERROR_PACKAGES_IN_USE` fallback) rather than fail
   outright — DIST-6's accepted default behavior. The old watchdog keeps
   supervising; the next invocation of `atv-reltest` runs the new version once
   the old watchdog self-exits (empty supervised set) and Windows completes
   the deferred registration.
4. Confirm via `cmd.exe /c "atv-reltest doctor"` (or `Get-AppxPackage
   *Agentaskvoid-reltest* | Select Version`) that the new version becomes
   active, with no corruption of existing card/sidecar state.

### 3.5 Uninstall with live cards + a running watchdog

Filter specifically on the `-reltest` Name. A bare `*Agentaskvoid*` wildcard
would also match (and remove) the dev-interactive `Agentaskvoid-<hash>`
registration and, if also installed on this box, the real release
`Agentaskvoid` package. Never use the bare wildcard on a dev box.

```
Get-AppxPackage -Name "*Agentaskvoid-reltest*" | Remove-AppxPackage
```

Expected: the taskbar card(s) disappear immediately (the Shell drops them on
uninstall — DIST-9); the package's app-data tree is gone; the watchdog
process self-exits on its next empty-set poll (give it a few seconds, then
`Get-Process atv-reltest -ErrorAction SilentlyContinue` should come back
empty). Confirm the dev-interactive `atv` binding (`cmd.exe /c "atv doctor"`,
a fresh terminal) is untouched throughout.

### 3.6 Cleanup

```
Get-AppxPackage -Name "*Agentaskvoid-reltest*" | Remove-AppxPackage   # if 3.5 wasn't already run
Remove-Item -Path "Cert:\LocalMachine\TrustedPeople\<thumbprint from devcert.cer>" -Confirm:$false
```

Confirm the smoke install is fully gone: no `*Agentaskvoid-reltest*` package
(`Get-AppxPackage`), no leftover
`%LOCALAPPDATA%\Packages\Agentaskvoid-reltest_*` folder, no `StartupTask`
entry for it (Settings > Apps > Startup, or `Get-StartupTask`), no stray
`atv-reltest.exe` process. The dev-interactive `Agentaskvoid-<hash>` package
should still be present (`Get-AppxPackage -Name "*Agentaskvoid-<hash>*"`) —
this cleanup must never touch it.

## 4. winget manifest set

`build/winget/manifests/a/Agentaskvoid/Atv/<version>/` — the standard
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

`Agentaskvoid.Atv` — `Atv.Diagnostics.DoctorChecks.WingetPackageId`
(`src/Atv/Diagnostics/DoctorChecks.cs`), brand-derived
(`{Branding.Name}.{PascalCase(Branding.Command)}` — never re-literaled).
`doctor`'s not-installed remedy line and `Atv.Diagnostics.Capability.Check`'s
identity-absent failure reason both print this exact string.
`build/winget/manifests/.../Agentaskvoid.Atv*.yaml`'s `PackageIdentifier`
matches it exactly; there is no automated cross-check between the two files,
since the manifest lives outside the compiled product.
