# DIST-14: AppTaskProvider extension Id must be build-kind-aware too
**Status:** DECIDED (2026-07-20)
**Plan:** phase-20

**Decision:** Stamp the `com.microsoft.apptaskprovider` `uap3:AppExtension`'s `Id` to the
same per-build-kind value as `Identity/@Name` (dev `<brand>-<pathhash>`, release
`<brand>`, `-reltest` `<brand>-reltest`, per-worktree test `<brand>.Test.<hash>`),
instead of the static literal each manifest template previously hard-coded. Both
`src/Atv/Package/AppxManifest.template.xml` and its `tests/Atv.AdapterTests` sibling
reuse the existing `{IdentityName}` token — no new MSBuild property, no second
computation (standing invariant #2).

**Evidence, established live, 2026-07-20, in this order:**
1. Only a dev-interactive package registered, static extension `Id`: its cards render
   on the taskbar.
2. A second (retail) package registered alongside it, sharing that same static `Id`:
   the dev package's cards stopped rendering immediately. Its `AppTaskInfo.Create`/
   `Update` calls kept succeeding, its own `SystemAppData\AppTasks\tasks.json` kept
   filling in, and `FindAll()` kept returning its entries — the taskbar simply never
   drew them.
3. Both packages re-registered with **distinct** extension `Id`s: both rendered their
   taskbar icons at the same time — two icons, simultaneously.
4. The execution alias used to launch a package (`atv.exe` / `atv-dev.exe` /
   `atv-test.exe`) made no difference at any step — whichever package was the only one
   registered rendered fine regardless of alias, ruling the alias out and isolating the
   extension `Id` as the mechanism.

The `AppTaskInfo` host resolves its provider registration by the `uap3:AppExtension`'s
`Id` alone, machine-wide, not per-package. This diverges from the documented contract:
Microsoft's docs describe `uap3:AppExtension`'s `Id` as an entry-point discriminator
within one app ("The entry point by which the host app accesses the extension category
instance, if there are multiple entry points"), and `AppExtensionCatalog` exposes each
extension alongside its owning `Package` — i.e. the documented identity model is
(package, id), not id alone. Microsoft Learn states package-wide uniqueness explicitly
where it means it — `uap11:Id`, a different attribute used on other extension
categories, is documented as "must be unique for all extensions in a package." Nothing
documented requires machine-global uniqueness for `uap3:AppExtension`'s `Id`; this
experimental `Windows.UI.Shell.Tasks` provider host resolves it that way regardless. See
`docs/windows-ui-shell-tasks/README.md`'s "The AppExtension Id is the provider's real
registration key, machine-wide" section for the full writeup.

**Constraint:** `uap3:AppExtension`'s `Id` has a hard 2–39 character platform limit
(Microsoft Learn). The four stamped values are 22 (release), 31 (dev), 30 (`-reltest`),
36 (test) characters — all comfortably under it — but `Identity/@Name` itself has no
such cap, so a future brand change could silently blow past 39 once it also becomes the
extension Id. `build/Atv.Package.targets` and `build/Atv.TestIdentity.targets` each
guard this with a build-time `Error` on `_Atv(Test)IdentityName.Length`.

**Amends:** the "structural isolation" claims in `CLAUDE.md`'s "Package identity"
section and `plan/README.md`'s standing invariant #3, both corrected alongside this
record. PFN divergence isolates package *state* (app-data, `tasks.json`, the write
mutex); it does not by itself isolate the `AppTaskInfo` provider registration, a separate
mechanism this decision fixes.

## Question

Why did the dev-interactive package's taskbar cards stop rendering the moment the
retail package was installed alongside it, and how should the fix be scoped?

## Why this surfaced

Operator, 2026-07-20, mid-phase-20 live verification (AC7/AC8): installed the retail
msix alongside the already-registered dev-interactive package and found dev's cards had
vanished from the taskbar.
