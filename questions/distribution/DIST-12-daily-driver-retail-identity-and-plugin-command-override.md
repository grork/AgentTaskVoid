# DIST-12: Daily driver on the retail identity; dev vacates `atv`; plugin command override
**Status:** DECIDED (2026-07-18, opened and answered in one session)
**Plan:** phase-20
**Amends:** DIST-3 ("Dev vs release identity (PFN) divergence") — its "dev-interactive owns
bare `atv`" clause and its rejection of a dev alias are superseded by points 1–2 below.

**Decision:**

1. **The operator's daily driver is the retail identity** — Name = brand exactly, alias
   `atv` — installed the way an end user installs it, with the host plugins installed from
   their official locations. The plugins invoke bare `atv` and need zero configuration in
   daily use. Until DIST-2 ("Signing / certificate acquisition") lands, "retail" means the
   `-t:AtvRelease` dev-cert msix installed locally: same Name, same alias. When the real
   cert changes the Publisher string, the PFN changes and state starts fresh — accepted,
   cards are ephemeral.

2. **Dev-interactive stamps alias `atv-dev`, not `atv`.** Bare `atv` always means the
   installed retail build; `atv-dev` always means the working copy. The alias is one fixed
   name across worktrees (last-registered worktree owns the shim); per-worktree Names and
   state isolation are unchanged. Knock-on: rebuild/reap can no longer touch the install
   that tracks the operator's own sessions — the original problem (daily use riding the
   dev exe that `build/Atv.DevReap.targets` kills on every compile) dissolves.

3. **Grounding (verified live, 2026-07-18, primary dev box):** package identity attaches
   only through packaged *activation*. Invoking `atv.exe` by full path runs naked — even
   from inside the registered loose-layout `AppX` folder. The per-alias shim
   `%LOCALAPPDATA%\Microsoft\WindowsApps\<alias>.exe` activates with identity and is
   itself a stable, version-independent full path. So every invokable pool needs an alias,
   and nothing anywhere needs organic PATH resolution — a full shim path works wherever a
   deterministic path is wanted.

4. **Translator command resolution** (both `translate.ps1`s, identically):
   `ATV_TRANSLATOR_STUB_EXE` (test seam, absolute priority) → `atv-command.txt` if present
   (one trimmed line, the verbatim command to invoke — typically the full `atv-dev` shim
   path; no env expansion) → the existing `Get-Command atv` guard → bare `atv`. Locations:
   Copilot reads it from its state root (beside `correlation-state.json`); Claude Code,
   which is stateless, reads it from `$PSScriptRoot` (gitignored, so a working-tree dev
   install can hold one but a marketplace copy never ships one). A present-but-broken
   override no-ops — it never falls back to `atv`, so dev-session events cannot leak onto
   the daily cards. The operator writes the file by hand; no verb or installer touches it.

5. **Automated tests are unaffected** — translator tests stub (the stub outranks the
   file), adapter tests run in the per-worktree test pool. Two disciplines get written
   down (CLAUDE.md + integration READMEs): in-repo/agent work never invokes bare `atv`
   (that is the operator's live install — use `atv-dev` or the stub), and live plugin
   dogfooding disables the installed real integration first (the existing capture-harness
   rule, applied to dogfood).

6. **`-reltest` is unchanged** — still the throwaway release-smoke pool. Final pool map:
   retail/daily (`atv`), dev (`atv-dev`), reltest (`atv-reltest`), test (`atv-test-<hash>`).

**Rejected, in a phrase each:** routing the choice through atv's own config (read only
after the exe is already chosen — wrong layer); full path to the package exe (runs naked —
verified, point 3); repurposing `-reltest` as the daily driver (its smoke runbook ends in
uninstall); a user-wide env-var override (ambient — leaks into any future harness that
forgets to clear it); a per-hash dev alias (unmemorable; shim contention across worktrees
accepted instead).

## Question

Daily dogfood use runs through the host plugins, which invoke `atv` — and on the dev box
that resolved to the dev-interactive build, per DIST-3. So the exe doing real daily work
was the same exe the inner loop rebuilds, reaps (`Atv.DevReap.targets` kills anything
holding it), and breaks at will. The operator wants side-by-side: a stable install backing
daily use, and a freely breakable working copy — without the plugins needing to know about
dev at all. Which identity backs daily use, and how do the translators pick their `atv`?

## Why this surfaced

Operator, 2026-07-18, once the Claude Code and Copilot CLI plugins made daily card use
real and continuous rather than occasional manual dogfood. DIST-3 had considered
dev/release coexistence only as an artificial release-smoke case and rejected a dev alias
on a convenience argument ("the primary `atv` = the working copy") that the plugin reality
inverted.
