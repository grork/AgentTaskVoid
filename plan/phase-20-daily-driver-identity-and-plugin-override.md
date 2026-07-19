# Phase 20: Daily-driver retail identity + plugin command override

**Depends on:** phase 12 (the `-t:AtvRelease` release-identity target and the
build-kind-aware `AtvStampAppxManifest` this phase edits — `build/Atv.Package.targets`),
phase 18 (the Claude Code plugin + `translate.ps1` + the `IntegrationTranslatorProcess`
stub harness the precedence tests extend). Also touches the in-tree Copilot CLI plugin
(`integrations/copilot-cli/plugins/atv-integration/translate.ps1`), which exists in the
repo but has no numbered plan phase of its own — this phase edits it as an existing
artifact, not as a dependency on a numbered phase.
**Unblocks:** nothing downstream in this plan. It removes a standing dogfood hazard: the
exe backing the operator's real daily card use is no longer the same exe the inner loop
rebuilds and reaps.

## Goal

Split the operator's daily card use off the working copy. After this phase:

- **Bare `atv` is the installed retail build** — Name = brand exactly, alias `atv`,
  installed the way an end user installs it, with the host plugins installed from their
  official locations and invoking bare `atv` with zero configuration. This is the exe
  behind the operator's own live taskbar cards.
- **The working copy stamps alias `atv-dev`** — the dev-interactive package (per-worktree
  Name and state unchanged) claims `<command>-dev` instead of `atv`. Rebuild-and-reap
  (`build/Atv.DevReap.targets`) can churn `atv-dev` all it likes; it can no longer touch
  the install tracking the operator's own sessions.
- **The translators can be pointed at the working copy for a dev dogfood** without the
  installed plugins knowing anything about dev: each `translate.ps1` consults an optional
  hand-written `atv-command.txt` override, and dev-session events route to `atv-dev` (or a
  test stub) instead of leaking onto the daily retail cards.

## Decisions implemented

### DIST-12 ("Daily driver on the retail identity; dev vacates `atv`; plugin command override")

DIST-12 is DECIDED (2026-07-18) and is the arbiter here; it is not re-litigated. It
**amends DIST-3 ("Dev vs release identity (PFN) divergence")**: DIST-3's "dev-interactive
owns bare `atv`" clause and its rejection of a dev alias are superseded. Everything else in
DIST-3 stands — per-worktree Names, per-identity state isolation, the never-hardcode-a-PFN
invariant, and the structural three-way PFN isolation (now four pools).

The five points this phase turns into code and docs:

1. **Retail identity backs daily use** (DIST-12 §1). Until DIST-2 ("Signing / certificate
   acquisition") lands, "retail" means the `-t:AtvRelease` dev-cert msix installed locally:
   same Name (brand exactly), same alias (`atv`). When the real cert changes the Publisher
   string the PFN changes and state starts fresh — accepted, cards are ephemeral.
2. **Dev stamps `atv-dev`, not `atv`** (DIST-12 §2). One fixed alias across worktrees
   (last-registered worktree owns the shim, exactly as the single dev pool already behaves
   for `atv`); per-worktree Names and state isolation unchanged.
3. **Identity attaches only through packaged activation via the alias shim** (DIST-12 §3,
   verified live 2026-07-18). Invoking the package exe by full path runs naked. The
   per-alias shim `%LOCALAPPDATA%\Microsoft\WindowsApps\<alias>.exe` is a stable,
   version-independent full path that activates with identity. Consequence for this phase's
   verification: an alias/identity claim is proven only by a real registration + a shim
   invocation whose `doctor` identity line is read back — never by inspecting a build log or
   a stamped-manifest file.
4. **Translator command resolution**, identical in both `translate.ps1`s (DIST-12 §4):
   `ATV_TRANSLATOR_STUB_EXE` (test seam, absolute priority) → `atv-command.txt` if present
   (one trimmed line, the verbatim command to invoke — no env expansion) → the existing
   `Get-Command atv` guard → bare `atv`. A present-but-broken override **no-ops**; it never
   falls back to `atv`, so a dev session's events cannot leak onto the daily cards. The
   operator writes the file by hand; no verb or installer touches it.
5. **Two disciplines get written down** (DIST-12 §5): in-repo / agent work never *drives*
   bare `atv` (no claim or state-changing verb against the operator's live install, ever —
   use `atv-dev` or the stub; read-only `atv doctor` / `atv --version` to inspect the retail
   install are sanctioned); live plugin dogfooding disables the installed real integration
   first (the existing capture-harness rule, applied to dogfood).

`-reltest` is unchanged (DIST-12 §6): still the throwaway release-smoke pool with alias
`atv-reltest`. Final pool map: **retail/daily (`atv`)**, **dev (`atv-dev`)**, **reltest
(`atv-reltest`)**, **test (`<command>-test-<hash>`)**.

**`BuildKindResolver` is untouched** (`src/Atv/Diagnostics/BuildKind.cs`). It classifies
from `Package.Current.Id.Name` alone — Release when Name equals the brand exactly, Test when
Name starts with `<brand>.Test.`, Dev otherwise. The alias is not part of that computation,
so renaming the dev alias from `atv` to `atv-dev` does not change any build-kind marker. The
dev-interactive package still classifies `(dev)`; `-reltest` still classifies `(dev)`; the
retail install classifies Release (unmarked). No test or code in `BuildKind.cs` changes.

---

## Part 1 — Manifest stamping: dev alias becomes `atv-dev`

`build/Atv.Package.targets`' `AtvStampAppxManifest` already resolves `_AtvExecutionAlias`
by build kind, but only splits `verify` (`<command>-reltest.exe`) from everything-else
(`<command>.exe`). Add a third branch so the **dev** kind resolves to `<command>-dev.exe`
while **release** keeps `<command>.exe`:

- `verify` → `$(AtvCommandName)-reltest.exe` (unchanged)
- `dev` → `$(AtvCommandName)-dev.exe` (**new** — derived from the command constant, never
  the literal string `atv-dev`, per standing invariant #2)
- `release` → `$(AtvCommandName).exe` (unchanged)

The `_AtvIdentityName` branches (dev = `<brand>-<pathhash>`, release = `<brand>`, verify =
`<brand>-reltest`) are **unchanged** — only the alias moves. Different Names already produce
different PFNs, so the four-pool isolation stays structural and independent of the still-static
Publisher (`CN=AppTaskInfoCli`, pending DIST-2).

Update the two code comments that assert the old binding:

- The big DIST-3-amendment comment block in `build/Atv.Package.targets` (its "dev ... alias
  `atv`" line) — restate as dev → `atv-dev`, citing DIST-12's amendment of DIST-3.
- `src/Atv/Package/AppxManifest.template.xml`'s header comment ("dev (default) keeps
  `<brand>-<pathhash>` + alias `atv`") and the `windows.appExecutionAlias` inline comment
  ("`atv` for dev/release") — restate as `atv-dev` for dev, `atv` for release.

**Dev-loop interaction (no change needed, but state it so a fresh session does not chase a
phantom).** The dev loop (`dotnet run` / F5 / `winapp run`, via
`Microsoft.Windows.SDK.BuildTools.WinApp`) launches the working copy by **packaged
activation of the loose layout**, not by invoking the alias off PATH — so renaming the alias
does not break `dotnet run`. The only observable change is the on-PATH shim name: after a
rebuild + re-register, `%LOCALAPPDATA%\...\WindowsApps` carries `atv-dev.exe` instead of
`atv.exe`, and the operator invokes the working copy as `atv-dev`. `launchSettings.json` is
untouched — it sets `ATV_WATCHDOG_MODE`, unrelated to the alias.

**The `atv` shim is released only on re-registration.** A plain `dotnet build` restamps the
obj manifest, but the currently-registered dev package keeps its old `atv` shim until the
package is re-registered (the next `dotnet run` / `winapp run`, or an explicit
register). This is why the operator migration checklist below re-registers dev explicitly
before installing the retail msix — otherwise both would contend for `atv`.

---

## Part 2 — Translator command resolution (both `translate.ps1`)

Both translators currently resolve `atv` as: stub env var if set, else `Get-Command atv`
guard then bare `atv`. Insert one tier between the stub and the guard: a hand-written
`atv-command.txt` override.

**Resolution order, identical in both scripts (DIST-12 §4):**

1. `$env:ATV_TRANSLATOR_STUB_EXE` — the test seam, absolute priority. A present stub always
   wins, even over a present `atv-command.txt` (this keeps every existing translator test
   green with no change).
2. `atv-command.txt` if present and non-empty after trim — its single trimmed line is the
   **verbatim** command to invoke (typically the full `atv-dev` shim path). No environment
   expansion, no quoting interpretation beyond `.Trim()`.
3. The existing `Get-Command atv -ErrorAction SilentlyContinue` guard.
4. Bare `atv`.

**Override-file location (differs by host, because Claude Code is stateless):**

- **Copilot** (`integrations/copilot-cli/plugins/atv-integration/translate.ps1`): read it
  from the state root — the directory `Get-StateRoot` already returns (beside
  `correlation-state.json`). If `Get-StateRoot` is null (no state env var), there is nowhere
  to read an override from; fall through to the guard, same as its correlation state already
  degrades. Do **not** touch `Get-StateRoot` itself — its heritage `CLAUDE_PLUGIN_DATA`
  fallback stays (correlation state already rides it; dropping it is a behavior change outside
  this phase's scope). This phase reads the override from whatever root `Get-StateRoot`
  resolves, and nothing more.
- **Claude Code** (`integrations/claude-code/plugins/atv-integration/translate.ps1`): read
  it from `$PSScriptRoot` (`Join-Path $PSScriptRoot "atv-command.txt"`), the same directory
  it already loads `map.json` from. Gitignored (Part 4), so a working-tree dev install can
  hold one but a marketplace/skills-dir-copied plugin never ships one. This is exactly right
  for the dogfood scenario: a dev dogfood runs the **working-tree** plugin (Option A symlink
  or Option B path install), so `$PSScriptRoot` resolves to the working-tree plugin dir and
  the file is seen; the operator's daily install is a separate copy that never carries the
  file, so daily use stays on bare `atv`.

**Shape of the change (both scripts).** Fold the override into the existing
`$script:AtvIsOverridden` mechanism rather than adding a parallel branch to `Invoke-Atv`.
Resolve a single `$script:AtvCommand` once, near the current stub-var block:

- if the stub var is non-empty → `$script:AtvCommand = <stub>`
- else if the resolved `atv-command.txt` exists and is non-empty after trim →
  `$script:AtvCommand = <trimmed line>`
- else → `$script:AtvCommand = $null`

`$script:AtvIsOverridden = ($null -ne $script:AtvCommand)`. `Invoke-Atv`'s overridden branch
calls `& $script:AtvCommand @AtvArgs` (replacing the current `& $script:AtvStubExe`); the
non-overridden branch keeps the `Get-Command atv` guard + `& atv` exactly as today.

**Broken/missing target = no-op, never a fallback to `atv`.** Because a present override
sets `$AtvIsOverridden = $true`, a broken target (`& <nonexistent>` throws) is caught by the
existing `try/catch` in `Invoke-Atv` and swallowed — control never reaches the
`Get-Command atv` guard. An **empty/whitespace-only** file is treated as *absent* (it falls
through to the guard), so a stray empty file does not silently disable the translator; only a
non-empty-but-invalid target no-ops.

**Diagnostics only where a log already exists.** The Copilot translator has `Write-Diagnostic`
(its `translator.log`); it may record that it read an override and, on a broken target, that
the invocation failed (the existing `catch` in its `Invoke-Atv` already logs "atv invocation
failed"). The Claude Code translator has **no log and gains none** — it stays silent, exactly
as it is today. Do not add a diagnostic file to the Claude Code plugin.

---

## Part 3 — Tests (TDD, red first)

All precedence tests are offline, extending the existing `IntegrationTranslatorProcess` /
per-host harness. They are automated in full. Write them red first.

**Harness seams needed (call these out — the current harnesses do not support them):**

- `IntegrationTranslatorProcess.Run` already sets `ATV_TRANSLATOR_STUB_EXE` and then applies
  the caller's `environment` dictionary *after*, so passing `["ATV_TRANSLATOR_STUB_EXE"] =
  null` **removes** the stub var for that run. The Copilot harness already threads an
  `environment` dict; add the equivalent to `ClaudeCodeTranslatorHarness.RunTranslator`
  (an optional environment override) so a Claude Code precedence test can run **without** the
  stub var.
- **Copilot** precedence tests write `atv-command.txt` into the `stateDirectory` the harness
  already points at a per-test temp dir — no new isolation needed.
- **Claude Code** precedence tests must not write into the working-tree plugin dir (`$PSScriptRoot`
  is fixed there; writing pollutes the shared folder and races parallel tests). Run the
  translator from a **per-test temp copy** of the plugin dir (`translate.ps1` + `map.json`
  copied together, because the script loads `map.json` from `$PSScriptRoot`), and drop the
  `atv-command.txt` beside the copy. `ClaudeCodePluginArtifactTests` enumerates an explicit
  file list, not the directory, so it is not affected either way — but the temp-copy approach
  keeps the working tree clean regardless.

**Precedence cases, per translator (each is one test):**

1. **File used.** Stub var removed; `atv-command.txt` points at the built stub exe
   (`EnsureStubBuilt`); a driving payload is sent. Assert the stub recorded the expected
   `atv` call(s) — proving the file's target was invoked. (`ATV_STUB_OUTPUT` is set by the
   harness regardless of which tier resolved the command, so the same stub records either
   way.)
2. **Stub beats file.** Stub var set **and** `atv-command.txt` present pointing at a
   different (bogus) target. Assert the stub recorded the call — the file was ignored.
3. **No file = today's behavior.** Stub var set, no `atv-command.txt`. Assert behavior is
   byte-identical to the existing suite for that payload — the new tier is inert when the
   file is absent. (The production tier below the file — `Get-Command atv` → bare `atv` — is
   not exercised offline: on any box with a real `atv` on PATH it would invoke the live
   install. That tier is covered by the live smoke + code inspection, not an automated test.)
4. **Broken target = no-op, no fallback.** Stub var removed; `atv-command.txt` points at a
   nonexistent path; a driving payload is sent. This must not rely on a bare "no `atv` on
   PATH" assumption — on the migrated dev box bare `atv` **is** the live daily install, so a
   buggy fall-through would mint real cards. Instead, **plant a decoy**: copy the built
   StubAtv exe (`EnsureStubBuilt`) to a per-test temp dir as `atv.exe`, prepend that dir to
   the run's `PATH`, and point the decoy at its own output file (the StubAtv `ATV_STUB_OUTPUT`
   seam). Then assert the **decoy's** output file recorded **zero** invocations — a positive
   proof that the broken override no-op'd and did **not** fall through to `Get-Command atv` →
   `& atv`, and one that physically cannot reach the live install because the only `atv` on
   PATH is the decoy.

Both translators get the four cases where the harness supports them (Copilot: all four
directly; Claude Code: all four via the temp-copy + environment-override seam).

**No engine tests, no adapter tests change.** The engine and the CLI verb surface are
untouched by this phase. The adapter suite runs in the per-worktree test pool (`<command>-test-<hash>`),
unaffected by the dev alias rename.

---

## Part 4 — `.gitignore`

Add the Claude Code plugin-dir override file so a working-tree dev install can hold one
without it ever being committed or shipped in a marketplace copy:

```
integrations/claude-code/plugins/atv-integration/atv-command.txt
```

The Copilot override lives in Copilot's private state root (outside the repo tree), so it
needs no `.gitignore` entry.

---

## Part 5 — Docs (each edit follows `.claude/skills/doc-style/SKILL.md`)

These files are in the doc set the style rulebook governs; read
`.claude/skills/doc-style/SKILL.md` before editing, and follow it (describe today, not the
project; say each fact once; plain words; one file per commit).

- **`CLAUDE.md` — "Package identity" section.** Rewrite the pool description from three pools
  to **four**, with the daily-driver rule stated plainly:
  - Dev (default): Name = `<brand>-<pathhash>`, **alias `atv-dev`** (was `atv`).
  - Release: Name = brand, alias `atv` — and this is the identity behind the operator's daily
    card use, installed on the dev box the same way an end user installs it.
  - `-reltest`: unchanged (Name = `<brand>-reltest`, alias `atv-reltest`).
  - Per-worktree test: unchanged.
  - Add the discipline (DIST-12 §5): **bare `atv` is the operator's live install** — in-repo
    and agent work uses `atv-dev` or the test stub and never *drives* bare `atv` (no claim or
    state-changing verb against it, ever); read-only `atv doctor` / `atv --version` to inspect
    the retail install are fine. State that `BuildKindResolver` is Name-based and so the marker
    logic is unaffected by the alias rename (one pointer, not a re-derivation).
- **`integrations/claude-code/README.md`** and **`integrations/copilot-cli/README.md`.** Add
  the override file: what it is (a hand-written one-line command override, read only from the
  plugin's `$PSScriptRoot` / the state root), what it is for (pointing a dev dogfood at
  `atv-dev` so events do not land on the daily retail cards), that a broken target no-ops
  rather than falling back to `atv`, and the **dogfood discipline** (disable the installed
  real integration first — the existing capture-harness rule applied to dogfood). Keep it to
  what the reader does; the rationale lives in DIST-12. Two host-specific additions:
  - **Claude Code README:** install the daily plugin from a source that excludes untracked
    files (the git repo, not a raw copy of the local plugin folder — a folder copy would carry
    a gitignored `atv-command.txt` along and route daily sessions to `atv-dev`); verify the
    installed copy has no `atv-command.txt`.
  - **Copilot README:** say how to find the state root the override lives in — the directory
    containing the plugin's existing `translator.log` and `correlation-state.json`
    (`COPILOT_PLUGIN_DATA` exists only inside the hook process, so it can't be read from a
    shell; find the directory by those files).
- **`docs/release.md` — section 2 rewrite + daily-driver install.** Section 2 currently
  argues *against* installing the retail msix on the dev box because dev and release both
  claim `atv` (alias contention). DIST-12 **reverses** that: dev now claims `atv-dev`, so the
  retail msix can be installed on the dev box as the daily driver and claim `atv` with no
  contention. Rewrite section 2's rationale accordingly, and add the daily-driver install:
  install the plain `-t:AtvRelease` release msix (not `-reltest`), trusting the dev cert once
  (the existing §3.1 elevation step), as the interim retail install until DIST-2 swaps in a
  real cert (at which point the Publisher and PFN change and state starts fresh — note it,
  don't dwell). Keep `-reltest`'s throwaway-smoke role intact (its runbook still ends in
  uninstall — it is not the daily driver); §3's `-reltest` steps stand.

---

## Operator migration checklist (an explicit acceptance step, live)

The cutover from "dev owns `atv`" to "retail owns `atv`, dev owns `atv-dev`" is a real
sequence the operator runs once, supervised. It is an acceptance criterion, not just prose:

1. **Sweep every worktree's dev package off the `atv` alias.** Re-registering only the
   current working copy is not enough: each *other* worktree has its own dev package
   (`<brand>-<pathhash>`, distinct Name/PFN) that still holds an `atv` alias claim from before
   this change, and the last-registered one owns the shim — so `atv` can stay bound to some
   stale worktree. Enumerate the brand-named dev packages (`Get-AppxPackage -Name
   "<brand>-*"`, excluding the `-reltest` and `.Test.` families), and for each one still
   stamped with the old `atv` alias, either rebuild + re-register it (`dotnet run` / `winapp
   run` / explicit register — restamps it to `atv-dev` and releases its `atv` shim) if the
   worktree is still wanted, or remove it (`Remove-AppxPackage`) if it is stale. The current
   working copy is the one you rebuild + re-register.
2. **Confirm `atv` is free.** From a freshly-spawned shell, `atv doctor` should now fail to
   resolve (no shim) — every dev binding to `atv` is gone. If it still resolves, a worktree
   package was missed in step 1; return there.
3. **Install the retail msix.** Build it (`dotnet build src\Atv\Atv.csproj -t:AtvRelease`),
   trust the dev cert once (elevation, `docs/release.md` §3.1), `Add-AppxPackage` the plain
   release msix. It claims `atv`.
4. **Verify from both aliases, fresh shells** (DIST-12 §3 — a real shim invocation, read the
   `doctor` identity line, not a build log): `atv doctor` reports the **Release** identity
   (Name = brand, unmarked — no `(dev)`); `atv-dev doctor` reports the dev-interactive
   identity (`<brand>-<pathhash>`, `(dev)`). Each resolves its own state tree.
5. **Install the daily plugins clean, then drop override files only where wanted.**
   - **Install the daily plugin from a source that excludes untracked files** — the git repo
     (marketplace/path install off a clean checkout), never a raw copy of the local plugin
     folder. A raw folder copy would drag along the gitignored `atv-command.txt` if the
     operator ever wrote one there, silently routing daily sessions to `atv-dev` — the exact
     leak DIST-12 §4 forbids. After install, verify the installed copy contains **no**
     `atv-command.txt` (check the installed plugin dir).
   - **Dev dogfood, Claude Code:** write `atv-command.txt` in the **working-tree** plugin dir
     (`integrations/claude-code/plugins/atv-integration/`, the one the dogfood runs from as
     `$PSScriptRoot`) containing the full `atv-dev` shim path.
   - **Dev dogfood, Copilot:** write it in the state root — the directory holding the plugin's
     existing `translator.log` and `correlation-state.json` (that is what `Get-StateRoot`
     resolves at runtime; `COPILOT_PLUGIN_DATA` exists only inside the hook process, so find
     the directory by those files, not by the env var).
   - Daily use leaves no override file anywhere.

---

## Files affected

```
build/Atv.Package.targets                                        # dev alias -> <command>-dev.exe; update DIST-3-amendment comment
src/Atv/Package/AppxManifest.template.xml                        # header + appExecutionAlias comments: dev = atv-dev
integrations/claude-code/plugins/atv-integration/translate.ps1   # atv-command.txt tier (from $PSScriptRoot), no new log
integrations/copilot-cli/plugins/atv-integration/translate.ps1   # atv-command.txt tier (from Get-StateRoot); translator.log diagnostics
tests/Atv.LogicTests/Integrations/ClaudeCodeTranslatorHarness.cs # optional environment override (to unset the stub var); temp-copy run path
tests/Atv.LogicTests/Integrations/ClaudeCodeTranslatorTests.cs   # 4 precedence tests
tests/Atv.LogicTests/Integrations/CopilotCliTranslatorTests.cs   # 4 precedence tests
.gitignore                                                       # integrations/claude-code/.../atv-command.txt
CLAUDE.md                                                        # "Package identity": four pools + daily-driver + discipline
integrations/claude-code/README.md                              # override file + dogfood discipline
integrations/copilot-cli/README.md                              # override file + dogfood discipline
docs/release.md                                                  # section 2 rewrite + daily-driver retail install
```

`src/Atv/Diagnostics/BuildKind.cs`: **not touched** (Name-based, alias-independent — stated
above so nobody edits it defensively).

## Acceptance criteria (written first)

Automated (offline) unless marked **LIVE**.

1. **Manifest stamp — dev alias.** The dev-kind stamped `obj/AppxManifest.xml` carries
   `<ExecutionAlias Alias="atv-dev.exe" />` (value derived from `$(AtvCommandName)`, not a
   literal); release stamps `atv.exe`; verify stamps `atv-reltest.exe`; the per-worktree test
   template stamps `<command>-test-<hash>.exe`. All four `_AtvIdentityName` values unchanged.
   *(This is a necessary content check, not proof of the binding — the binding is AC7.)*
2. **Translator precedence — file used** (both translators): with the stub var removed and
   `atv-command.txt` pointing at the stub exe, the recorded call(s) match the expected verb —
   the file's target was invoked.
3. **Translator precedence — stub beats file** (both): stub var set + a present bogus
   `atv-command.txt` → the stub is invoked, the file ignored.
4. **Translator precedence — no file = today's behavior** (both): stub set, no file →
   byte-identical to the existing suite for the same payload.
5. **Translator precedence — broken target = no-op** (both): stub removed + `atv-command.txt`
   pointing at a nonexistent path → the broken override no-ops and does not fall through to
   `atv`. Proven positively with a decoy `atv.exe` (a copy of the built StubAtv) planted first
   on the run's `PATH` with its own output file: the decoy records **zero** invocations, so a
   buggy fall-through would be caught and can never escape to the live install.
6. **`BuildKindResolver` regression.** The existing `BuildKind` tests still pass unchanged —
   the alias rename touched no classifier input.
7. **LIVE — dev alias binding.** After a real rebuild + re-register of the working copy,
   `atv-dev doctor` (fresh shell) reports the dev-interactive identity (`<brand>-<pathhash>`,
   `(dev)` marker), and `atv` no longer resolves to the working copy. Verified by reading the
   `doctor` identity line off a shim invocation (DIST-12 §3), never a build log.
8. **LIVE — retail daily driver.** The plain `-t:AtvRelease` release msix installs on the dev
   box and `atv doctor` (fresh shell) reports the Release identity (Name = brand, unmarked),
   coexisting with the `atv-dev` dev package — no alias contention, distinct state trees.
9. **LIVE — plugin override smoke (at least one real host).** A working-tree Claude Code (or
   Copilot) dogfood with `atv-command.txt` pointing at the `atv-dev` shim drives cards onto
   the **dev** pool, leaving the retail daily cards untouched; removing/breaking the override
   leaves the daily cards untouched too (the broken-target no-op, live). This is
   operator-supervised and not subagent-able (the phase-18/19 live-dogfood pattern:
   never fire a real hook from a subagent, exact-PID-only process handling, no raw Ctrl+C).
10. **Operator migration checklist** (LIVE): the five-step cutover above runs clean end to
    end — dev releases `atv`, retail claims `atv`, `doctor` verifies from both aliases.
11. **Docs clean** (per `.claude/skills/doc-style/SKILL.md`): `CLAUDE.md` describes four
    pools and the bare-`atv`-is-the-live-install discipline; both integration READMEs
    document the override file + dogfood discipline; `docs/release.md` §2 no longer claims
    the retail msix cannot be installed on the dev box, and covers the daily-driver install.
    Suites green; NativeAOT publish clean.

**Live-only criteria: AC7, AC8, AC9, AC10.** No build-log or stamped-file inspection
substitutes for them — DIST-12 §3 grounds that identity attaches only through packaged
activation via the alias shim, so the only proof an alias resolves to the intended identity
is a real registration followed by a shim invocation whose `doctor` output is read back.

## Out of scope

- DIST-2 (real signing cert): the retail install stays the dev-cert msix here; the
  Publisher/PFN change and state reset land with DIST-2, not this phase.
- Any `atv` verb or installer that writes `atv-command.txt` — the operator writes it by hand
  (DIST-12 §4 rejected an installer-managed override and a user-wide env-var override).
- Routing the command choice through `atv`'s own config (rejected — read after the exe is
  already chosen, wrong layer) or the full package-exe path (rejected — runs naked).
- The deferred Copilot CLI / Codex / pi *legs* as plan phases (INFRA-31); this phase only
  edits the Copilot plugin that already exists in the tree.
- Interaction round-trip (INTER-*), the raw card-control tier (ERGO-32) — DEFERRED.
