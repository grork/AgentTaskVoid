# Phase 24: Dogfood kit — Copilot CLI auto-wiring leg

**Depends on:** phase 23 (the kit, its per-host verified-recipe structure, and its
automated producer checks this extends).
**External gate:** requires working GitHub Copilot CLI access (a machine/account that can
run the real `copilot` tool). The operator currently has none — which is why this is its
own phase rather than a part of 23 (operator direction, 2026-07-19): phase 23 can be
marked fully complete, and this phase's pending state reads at a glance instead of as a
partially-done 23. Same shape as the INFRA-31 deferred-until-testable host legs, except
this work is already DECIDED (DIST-13, "Easy-to-use scripts that produce package and
plugins") and therefore planned now.
**Unblocks:** nothing downstream; it completes DIST-13's "for each available integration,
install it" for the second implemented host.

## Goal

Extend the kit's installer/uninstaller with a **verified** Copilot CLI wiring recipe, and
prove it live. Until this phase, the kit ships the Copilot plugin zip with manual
instructions only; after it, a Copilot-equipped dogfooder gets the same prompted
auto-wiring the Claude Code leg already has.

## Decisions implemented

DIST-13's installer clause ("for each implemented integration whose host is detected
present, prompt then wire") — the Copilot half, deferred from phase 23 because its
mechanism is unverified. Two disciplines govern this phase (both are standing operator
guidance, the reason the leg was split rather than shipped on faith):

- **Verify tool behavior, not static artifacts.** DIST-13's pinned Copilot commands
  (`copilot plugin marketplace add <dir>` + `copilot plugin install
  atv-integration@<marketplace>`) came from Microsoft Learn docs, not from running the
  tool — and `integrations/copilot-cli/README.md` documents a different install path (a
  GitHub-slug `copilot plugin install grork/AppTaskInfoCli:integrations/copilot-cli/...`
  and the dev-only `--plugin-dir` flag), with no local-marketplace flow. One of these
  pictures is stale or wrong; only the real CLI can say which.
- **Host integration needs a live dogfood.** Doc-derived host wiring has produced
  plausible-but-wrong configs before; the leg is not "done" until a real Copilot session
  exercises it.

## Work

1. **Pin the real wiring mechanism against the shipped `copilot` CLI.** Run its help /
   experiment with a local marketplace add + install from the kit's expanded plugin dir.
   The marketplace name is whatever `integrations/copilot-cli/.github/plugin/marketplace.json`
   declares — `agent-task-void-copilot`, which is NOT the Claude marketplace name
   (`agent-task-void`); never reuse the Claude string. Reconcile the outcome three ways:
   the kit uses the verified commands; `integrations/copilot-cli/README.md` is corrected
   if its install section is stale (doc-style skill); and if the DIST-13 record's pinned
   commands prove wrong, append a dated correction note to the question file rather than
   leaving a record that lies.
2. **Add the Copilot recipe to the installer's verified set** (the per-host data
   structure phase 23 built for exactly this) and the symmetric removal to the
   uninstaller. Detection, prompt, consent, and skip semantics are identical to the
   Claude leg; no new script architecture.
3. **Kit README:** replace the Copilot piece's pending-auto-wiring note with verified,
   **zip-relative** manual-install commands (phase 23's zip-relative rule applies).
4. **LIVE smoke on the Copilot-equipped machine:** `install.ps1` detects Copilot →
   prompts → wires; a real Copilot session (or a manual `atv` call if a session can't be
   driven) lands a card; `uninstall.ps1` unwires it; other identity pools untouched.
   Conduct per phase 21's INFRA-33 rules and the phase-18/19 live-dogfood constraints
   (disable any installed real integration first; never fire a real hook from a
   subagent; exact-PID-only process handling).

## Files affected

```
build/dogfood/install.ps1                    # + Copilot entry in the verified recipe set
build/dogfood/uninstall.ps1                  # + symmetric Copilot removal
build/dogfood/README-template(s).md          # Copilot piece: verified zip-relative commands, pending note dropped
integrations/copilot-cli/README.md           # install section corrected if stale (doc-style skill)
questions/distribution/DIST-13-*.md          # dated correction note IF the pinned commands prove wrong
```

## Acceptance criteria (written first)

1. **Verified mechanism.** The Copilot wiring commands in the installer were exercised
   against the real `copilot` CLI (record the evidence — help output or a transcript —
   in the phase's progress entry); the marketplace name matches the tree's
   `marketplace.json`; the README/record reconciliation from Work item 1 is done.
2. **Additive scripts.** Installer wires Copilot when detected and consented; absent/
   declined → skipped exactly like the Claude leg; uninstaller removes symmetrically;
   still zero bare-wildcard package operations and zero hand-typed brand strings
   (phase 23's AC4 grep re-run passes).
3. **Kit README.** The Copilot instructions are zip-relative and the pending note is
   gone.
4. **LIVE — end-to-end on the Copilot machine.** Wire → a card renders from a Copilot
   session → uninstall unwires; the retail package and any dev/test pools on that
   machine untouched.
5. **Phase 23's automated checks still green.** Producer, zip-leak, and script-safety
   checks re-run clean — the leg is additive, not a regression.

## Out of scope

- Any change to the Copilot integration's *behavior* (translator, mapping, LIFE-26's
  accepted stale-after-approval window — all stand as shipped).
- The Copilot recorder/capture legs and onboarding playbook (INFRA-31/INFRA-32 — still
  DEFERRED until the host is testable in that deeper sense).
- Codex/pi or any other host (same INFRA-31 boundary as phase 23).
