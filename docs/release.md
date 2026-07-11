# Release runbook

Phase 12 ("Release packaging & distribution verification"). Covers: producing a
signed full-MSIX release build for both architectures, the dev-cert-vs-real-cert
distinction, and the supervised install/upgrade/uninstall verification (DIST-8's
joined release leg). The two remaining ship-time steps -- real certificate
acquisition and winget submission -- stay DEFERRED (DIST-2); this doc says so
explicitly at the point each would happen, it does not perform them.

## 1. Build + sign (this machine can do this unsupervised, no admin)

```
dotnet build src\Atv\Atv.csproj -t:AtvRelease
```

One command, from repo root. Publishes NativeAOT `atv.exe` for both `win-x64`
and `win-arm64`, packages each against the phase-01 stamped `obj\` manifest
(`build/Atv.Package.targets`, explicit `--manifest`, never auto-detected -- see
`build/Atv.Release.targets` for the full mechanism and why), and signs both
with a throwaway self-signed dev certificate. Re-running with nothing changed
is a no-op (MSBuild's own Inputs/Outputs skip-detection on the two per-arch
targets -- proven 2026-07-10: first run ~43s, immediate re-run ~1s, all three
targets logged "Skipping target ... because all output files are up-to-date").

Artifacts (gitignored, never checked in):

```
artifacts\release\cert\devcert.pfx / .cer   -- self-signed, CN=AppTaskInfoCli
artifacts\release\publish\win-x64\atv.exe
artifacts\release\publish\win-arm64\atv.exe
artifacts\release\msix\Agentaskvoid_<version>_x64.msix     -- signed
artifacts\release\msix\Agentaskvoid_<version>_arm64.msix   -- signed
```

This stamps the clean **release** identity (`Identity/@Name = Agentaskvoid`, `AppExecutionAlias = atv`) into both packages -- see section 2 below for why that Name is now distinct from the dev-interactive identity, and why section 3's supervised smoke nonetheless installs a THIRD, throwaway identity rather than these two artifacts directly.

### The `-reltest` throwaway smoke variant

```
dotnet build src\Atv\Atv.csproj -t:AtvRelease -p:AtvVerifyIdentity=true
```

Same pipeline, one extra property. Produces distinctly-named artifacts that never collide with (or up-to-date-skip against) the real release msix above:

```
artifacts\release\msix\Agentaskvoid_<version>_x64_reltest.msix     -- signed
artifacts\release\msix\Agentaskvoid_<version>_arm64_reltest.msix   -- signed
```

stamped with `Identity/@Name = Agentaskvoid-reltest`, `AppExecutionAlias = atv-reltest` -- a package that installs and launches independently of both the real release identity and the dev-interactive one. This is the artifact section 3 actually installs.

Prerequisite specific to this ARM64 machine: `win-x64` is a cross-AOT publish,
and NativeAOT's cross-linking step shells out to `vswhere.exe`, which is not on
PATH by default. Add it for the session before running the command above:

```
$env:PATH += ";C:\Program Files (x86)\Microsoft Visual Studio\Installer"
```

To bump the version for a new build (e.g. to exercise AC4's upgrade-in-place),
make any commit -- NBGV's `$(BuildVersion)` is git-height-derived, so height
advances automatically; there is no version file to hand-edit.

### Dev cert vs. real cert

`AtvReleaseCert` (inside `build/Atv.Release.targets`) runs `winapp cert
generate --if-exists Skip`, producing a **throwaway, self-signed** certificate
(`CN=AppTaskInfoCli`, matching `Package\AppxManifest.template.xml`'s static
`Identity/@Publisher` exactly -- required for the package to be installable
once the cert is trusted). This certificate:

- Proves the pipeline mechanically (pack -> sign -> install -> alias-launch ->
  drive `AppTaskInfo`, DIST-8's "joined release leg").
- Is NOT suitable for distribution. A self-signed cert requires per-machine
  admin trust-store installation, which is a worse ask than Windows Developer
  Mode and was explicitly rejected as a distribution mechanism (DIST-2).
- Is never installed into any machine trust store by any build-half
  automation. Trusting it (`winapp cert install` / `Import-Certificate`) is
  the one step in this whole runbook that needs elevation, and is done by a
  human, deliberately, in section 3 below.

**Ship-time step, still deferred (DIST-2):** replacing the dev cert with a real
one -- Azure Trusted Signing (re-confirm individual-developer eligibility at
that time), a standard OV code-signing cert, or Store-signing -- and wiring
`AtvReleaseCert`'s `--cert`/`--publisher` to it instead. `Package\AppxManifest.template.xml`'s
`Identity/@Publisher` is currently the placeholder `CN=AppTaskInfoCli`; a real
cert's subject will very likely differ, and that file's Publisher must be
updated to match (breaking the PFN -- see the isolation warning in section 2).

## 2. Why installing the real release msix on this dev box still isn't done here

**Superseded 2026-07-10 (DIST-3 amendment, phase 12) -- the collision this
section originally warned about is fixed structurally. Kept here as the
record of why, and of what still needs care.**

A Package Family Name is `<Name>_<PublisherId>`, and `PublisherId` is a
deterministic hash of nothing but the manifest's declared `Identity/@Publisher`
string (Microsoft Learn, "An overview of Package Identity in Windows apps":
"Publisher Id ... Derived from Publisher" -- it does not depend on which
certificate actually signs the package, only on the string declared in the
manifest).

**Original finding (2026-07-10, this pipeline's first real run):** dev-interactive
and the dev-cert release were both stamped from the literal same
`Identity/@Name` (`Agentaskvoid-bbbb1168` in this worktree) and the same static
`Identity/@Publisher` (`CN=AppTaskInfoCli`) -- computing the PublisherId hash
confirmed their Package Family Names were IDENTICAL. DIST-3's three-pool
isolation was not structural; it leaned entirely on the still-deferred DIST-2
real-cert Publisher edit.

**Fix (ratified, see `questions/distribution/DIST-3-dev-vs-release-identity-pfn-divergence.md`):**
`build/Atv.Package.targets`' `AtvStampAppxManifest` is now build-kind-aware --
dev-interactive keeps `Agentaskvoid-bbbb1168` (unchanged, still owns the `atv`
alias); the real release build (section 1) stamps a clean, pathhash-free
`Agentaskvoid` (also alias `atv`); the `-reltest` variant (section 1) stamps a
third, distinct `Agentaskvoid-reltest` (alias `atv-reltest`). Different Names
now produce structurally different PFNs for dev vs. release vs. reltest,
independent of the still-static Publisher.

**The remaining reason section 3 installs the `-reltest` artifact, not the real
release msix, even though their PFNs now differ:** dev-interactive and the real
release identity both declare the SAME `AppExecutionAlias` (`atv`) -- by
design (DIST-3: "on a dev box the primary `atv` = the working copy --
convenient and correct" for dev-interactive; "release... owns the bare `atv`
alias" for a real end-user machine, where dev-interactive is never installed).
Installing the real release msix on THIS dev box, alongside the dev-interactive
registration, would put two packages in alias contention over `atv` -- an
artificial collision that only exists in this smoke-test scenario, not in any
real deployment (a dev box runs dev; a user's box runs release; never both).
The `-reltest` variant's distinct `atv-reltest` alias sidesteps that contention
entirely, so the DIST-8 pack -> sign -> install -> alias-launch -> drive-
`AppTaskInfo` leg can be exercised on this same dev box without disturbing the
live `atv` = dev-interactive binding.

## 3. Supervised install / upgrade / uninstall verification (DIST-8, AC2-AC5)

**Not run by the build-half pass that produced this document.** This section is
the exact command sequence for the orchestrator + operator to run together,
supervised, on this ARM64 machine. One step needs elevation (trusting the dev
cert); everything else is per-user, no-admin.

### Machine-specific deviation from the phase file

`plan/phase-12-release-packaging.md` AC2 says "on this machine (x64)". This
machine is ARM64. Per the same native-substitution precedent phase 01 used
(x64-under-emulation gave an inconclusive `AppTaskInfo.IsSupported()` result;
native arm64 round-tripped cleanly): the functional DIST-8 verification target
here is the **native arm64** signed package. The **x64** artifact builds clean
and is Authenticode-signed (confirmed 2026-07-10 via
`Get-AuthenticodeSignature`), but functional verification on x64 stays
deferred -- no x64 device available.

### 3.1 Trust the dev cert (elevation required, once per cert)

```
Import-Certificate -FilePath "artifacts\release\cert\devcert.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

(Run from an elevated PowerShell. `winapp cert install artifacts\release\cert\devcert.pfx`
is the equivalent single-command form if preferred -- also needs elevation.)

### 3.2 Install the signed arm64 `-reltest` package (per-user, no admin once trusted)

Per section 2: install the **`-reltest`** artifact, not the plain release one --
it is the variant with its own non-colliding `atv-reltest` alias, so this
install cannot disturb the dev-interactive `atv` binding already registered on
this box.

```
Add-AppxPackage -Path "artifacts\release\msix\Agentaskvoid_<version>_arm64_reltest.msix"
```

### 3.3 AC2/AC3: identity, API, alias-on-PATH -- from a FRESHLY-SPAWNED cmd.exe

A brand-new `cmd.exe` (not the shell that has been running throughout this
session) is required so the `%LOCALAPPDATA%\Microsoft\WindowsApps` alias
resolves without a human manually opening a terminal. Note the command name is
**`atv-reltest`**, not `atv` -- that's the whole point of section 2's variant:

```
cmd.exe /c "atv-reltest doctor"
```

Confirm the printed identity line ends `Agentaskvoid-reltest` (structurally
distinct from the dev-interactive `Agentaskvoid-bbbb1168_016qghrny08mj` PFN --
DIST-3's three-pool isolation) with a trailing `(dev)` marker. That marker is
CORRECT, not a bug: `BuildKindResolver` classifies `-reltest` as Dev-pool (its
Name is neither the bare brand nor `<brand>.Test.*`), so it prints `(dev)` --
the ONLY unmarked identity is the real release build (Name = bare
`Agentaskvoid`). Verified live 2026-07-10. Also confirm:
`api: AppTaskInfo.IsSupported() -> true`; `config file:` / `app-data folder:` /
`sidecar dir:` / `log file:` all resolve under THIS package's own `LocalState`,
distinct from both the dev worktree's and the real release identity's.

```
cmd.exe /c "atv-reltest start s1 --title Hi"
cmd.exe /c "atv-reltest list --json"
```

Confirm a card renders on the taskbar (eyeball, per the Checkpoint-C1
precedent) and `list --json` reports it. Confirm the watchdog spawns
(`cmd.exe /c "atv-reltest doctor"` again -- `watchdog: running`, or check
`Get-Process atv-reltest`).

### 3.4 AC4: upgrade-in-place with a live watchdog

1. With the watchdog from 3.3 still running (a card alive keeps it up), commit
   any trivial change (NBGV's `$(BuildVersion)` is git-height-derived -- any
   commit bumps it) and re-run section 1's `-p:AtvVerifyIdentity=true` build
   command to produce v(N+1) of the `-reltest` artifact.
2. `Add-AppxPackage -Path "artifacts\release\msix\Agentaskvoid_<new-version>_arm64_reltest.msix"`
   over the running watchdog.
3. Expected: install succeeds; if the watchdog holds `atv.exe` open, Windows
   may defer registration (`ERROR_PACKAGES_IN_USE` fallback) rather than fail
   outright -- this is DIST-6's already-accepted default behavior, not
   something this codebase handles specially. The old watchdog keeps
   supervising; the next invocation of `atv-reltest` runs the new version once
   the old watchdog naturally self-exits (empty supervised set) and Windows
   completes the deferred registration.
4. Confirm via `cmd.exe /c "atv-reltest doctor"` (or `Get-AppxPackage
   *Agentaskvoid-reltest* | Select Version`) that the new version becomes
   active, with no corruption of the existing card/sidecar state.

### 3.5 AC5: uninstall with live cards + a running watchdog

Filter specifically on the `-reltest` Name -- a bare `*Agentaskvoid*` wildcard
would ALSO match (and remove) the dev-interactive `Agentaskvoid-bbbb1168`
registration and, if it's ever also installed on this box, the real release
`Agentaskvoid` package. Never use the bare wildcard on a dev box.

```
Get-AppxPackage -Name "*Agentaskvoid-reltest*" | Remove-AppxPackage
```

Expected: the taskbar card(s) disappear immediately (Shell drops them on
uninstall -- DIST-9, already empirically confirmed once on the dev path,
2026-07-05; confirm once here on the packaged path); the package's app-data
tree is gone; the watchdog process is not left wedged (it self-exits on its
next empty-set poll -- give it a few seconds and check `Get-Process atv-reltest
-ErrorAction SilentlyContinue` comes back empty). Confirm the dev-interactive
`atv` binding (`cmd.exe /c "atv doctor"`, a fresh terminal) is untouched
throughout -- the whole point of the `-reltest` variant.

### 3.6 Cleanup

```
Get-AppxPackage -Name "*Agentaskvoid-reltest*" | Remove-AppxPackage   # if 3.5 wasn't already run
Remove-Item -Path "Cert:\LocalMachine\TrustedPeople\<thumbprint from devcert.cer>" -Confirm:$false
```

Confirm the smoke install is fully gone: no `*Agentaskvoid-reltest*` package
(`Get-AppxPackage`), no leftover
`%LOCALAPPDATA%\Packages\Agentaskvoid-reltest_*` folder, no `StartupTask` entry
for it (Settings > Apps > Startup, or `Get-StartupTask`), no stray
`atv-reltest.exe` process. The dev-interactive
`Agentaskvoid-bbbb1168_016qghrny08mj` package is EXPECTED to still be present
(`Get-AppxPackage -Name "*Agentaskvoid-bbbb1168*"`) -- this cleanup must never
touch it; that persistence is exactly what the `-reltest` variant exists to
guarantee.

## 4. winget manifest set

`build/winget/manifests/a/Agentaskvoid/Atv/<version>/` -- the standard
community `winget-pkgs` multi-file layout (version / installer /
`locale.en-US` manifests), authored directly against the artifacts this
pipeline produces. `winget validate` (v1.29.280, this machine) reports
**"Manifest validation succeeded."** against the current snapshot.

`InstallerSha256` values are real hashes of the dev-cert-signed `.msix` files
this build produced. `InstallerUrl` points at this repo's GitHub Releases URL
convention (`github.com/grork/AppTaskInfoCli/releases/download/v<version>/...`)
for a tag that does not exist yet -- this is a snapshot manifest, not a
submission. Regenerate the three files under a new version folder for each
real release rather than hand-editing an old snapshot back into currency.

**Ship-time step, still deferred (DIST-2):** cutting a real GitHub release with
the real-cert-signed artifacts at the URLs above, then `winget submit` (or a
PR against `microsoft/winget-pkgs`) using this manifest set as the starting
point.

## 5. The finalized winget package id

`Agentaskvoid.Atv` -- `Atv.Diagnostics.DoctorChecks.WingetPackageId`
(`src/Atv/Diagnostics/DoctorChecks.cs`), brand-derived
(`{Branding.Name}.{PascalCase(Branding.Command)}`, invariant #2 -- never
re-literaled). `doctor`'s not-installed remedy line and
`Atv.Diagnostics.Capability.Check`'s identity-absent failure reason both print
this exact string. `build/winget/manifests/.../Agentaskvoid.Atv*.yaml`'s
`PackageIdentifier` matches it exactly -- verified by eye at authoring time;
there is no automated cross-check between the two files, since the manifest
lives outside the compiled product.
