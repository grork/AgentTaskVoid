# DIST-7: The release full-package manifest
**Status:** DECIDED (Name/alias stamping made build-kind-aware, 2026-07-10 — see DIST-3)
**Plan:** phase-01
**Parent:** DIST-5

**Amendment 2026-07-10 (phase 12, ratified with [[DIST-3]]):** the stamp is NO LONGER
uniform across dev/release. `Identity/@Name` is now **build-kind-aware** — release stamps a
CLEAN pathhash-free `<brand>`; dev/test keep their suffixed Names — and the
`AppExecutionAlias` is tokenized so a coexisting variant (the phase-12 `atv-reltest` smoke)
can override it. This is what makes DIST-3's three-pool isolation STRUCTURAL rather than
dependent on the deferred DIST-2 publisher edit. Publisher stays static (`CN=AppTaskInfoCli`)
until the DIST-2 real cert. The "single template, only Name+Version stamped, every other
field static/uniform" description below is superseded on the Name/alias axis.

**Decision:** One brand-parameterized full-package `AppxManifest` template serves BOTH dev
loose-layout (`winapp run`) and release (`winapp package`). A single build-time MSBuild
target stamps only the Identity **Name** (brand + build-output-path hash) and **Version**
(NBGV `$(BuildVersion)`) into a RID-qualified `obj/` copy via content-conditional write;
winapp consumes that copy out-of-process. Verified end-to-end for the dev/build half by
spike (2026-07-05).

Manifest (invariant across dev/release):
- `AppExecutionAlias` -> `atv`; declared-but-disabled MSIX `StartupTask` (LIFE-20,
  "Logoff/reboot recovery"); `TargetDeviceFamily` min 26100; `runFullTrust`; NO protocol
  handler (INTER-1, "What receives Shell activations", is deferred).
- Display/identity fields derive from the ERGO-18 ("The shipped command name") brand
  constant -> feeds DIST-3 ("Dev vs release identity (PFN) divergence"). Publisher is
  static (not parameterized).

Stamping mechanism (only two fields vary at build time):
- Identity Name = brand + a hash of the build-output path (gives INFRA-16, "Test-time
  identity provisioning and deep isolation", its per-worktree isolation). Version = NBGV.
- One MSBuild target in an imported `.targets` reads the template, replaces
  `{IdentityName}`/`{Version}`, writes the RID-qualified `obj/` copy with
  `WriteOnlyWhenDifferent`, gated by `Inputs`/`Outputs`.
- Consumption is OUT-OF-PROCESS winapp -- there is no manifest-consuming MSBuild target to
  hook. An orchestrating target `Exec`s `winapp package --manifest <obj-copy>` (explicit,
  never auto-detect); dev `winapp run` reads the stamped manifest from the build output.
  Stamping runs during build, so it is launcher-invariant (VS / `dotnet build` / winapp
  share one build graph).

Implementation rules (settled, so build-phase does not relitigate):
- Version token = NBGV `$(BuildVersion)` (4-part numeric); `$(Version)` carries a
  semver+githash suffix that makeappx rejects against `Identity/@Version`.
- Compute the version token and the `obj/` path inside the target's execution-time
  `PropertyGroup`, never the file body -- at evaluation time NBGV's version is uncomputed
  and `$(IntermediateOutputPath)`'s RID-qualified value is unfinalized, so a file-body
  property silently captures empty strings (manifest lands in the project root, empty
  version, no error).
- `WriteOnlyWhenDifferent` + `Inputs`/`Outputs` is what prevents no-op repacks.

Optional in-proc VS/`dotnet build` packaging, if ever wanted, is a separate
`Microsoft.Windows.SDK.BuildTools.MSIX` reference hooking `BeforeGenerateAppxManifest` --
not needed while winapp is the only packager.

Scope: this settles the dev/build half. The pack -> sign -> install -> alias-launch ->
drive-`AppTaskInfo` leg is DIST-8 ("The joined release-leg spike"), now unblocked.

What replaces the dropped sparse scaffolding: a winapp-init-style full-package
AppxManifest -- the `AppExecutionAlias` extension putting `atv` on PATH, capabilities,
TargetDeviceFamily / min-version (26100) -- and whether ONE manifest source serves
both the dev loose-layout (INFRA-17, "Dogfood/run ergonomics without a load-bearing
script") and the release pack. Publisher/brand fields parameterize via the ERGO-18
("The shipped command name") constant and feed DIST-3 ("Dev vs release identity (PFN)
divergence").
