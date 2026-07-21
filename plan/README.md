# Execution Plan

Built 2026-07-07 from `brief.md`, `requirements.md`, and every DECIDED question under
`questions/` (per `process.md`, discovery and answering are complete); extended
2026-07-12 with phase 14 (INFRA-23's expansion, INFRA-24..29); extended 2026-07-13
with phases 15–18 (the v2 semantic line — LIFE-24/ERGO-31/LIFE-25/DIST-11 — plus the
icon and repo-config work, ERGO-28/29/30); extended 2026-07-18 with phase 20 (DIST-12's
daily-driver retail identity + plugin command override); extended 2026-07-19 with phases
21–24 (the dogfood-feedback line — INFRA-33's dev-run rules, ERGO-34/35's create-anchored
card defaults, DIST-13's dogfood distribution kit + its access-gated Copilot leg).
DEFERRED questions are OUT of this build's
scope: the whole interaction round-trip (INTER-1..4), DIST-2 (signing-cert
acquisition), DIST-10 (engine adoption vehicle, gated on DIST-2), INFRA-12 (latency
budget), INFRA-22 (GUI-subsystem exe / AttachConsole), INFRA-31/INFRA-32 (recorder
legs + onboarding playbook for not-yet-testable hosts), LIFE-3 (wire-transport
observation), ERGO-32 (raw card-control tier).

Each phase file is self-contained: goal, decisions it implements (cited as
`ID ("title")` — the full records live in `questions/`), files affected, acceptance
criteria, and dependencies. A fresh session should be able to implement a phase from
its file alone, consulting the cited question records only for deeper rationale.

## Standing invariants (apply to every phase)

1. **TDD** (brief assertion 1): write tests, see red, implement, see green. Every
   phase lists its test obligations; "implemented but untested" does not meet
   acceptance.
2. **Brand parameterization** (ERGO-18, "The shipped command name"): binary `atv`,
   identity `Codevoid.AgentTaskVoid`, display name "Agent Task Void". The three
   `Branding` constants feed identity, command name, display strings, env/config
   names, mutex names, paths, artifact names. Never bake a string in twice.
3. **Never hardcode a PFN** (DIST-3, "Dev vs release identity (PFN) divergence", amended
   by DIST-12): everything PFN-keyed (mutexes, app-data paths) derives at runtime from the
   current package. Four identity pools (release/daily `atv` / dev-interactive `atv-dev` /
   `-reltest` / per-worktree test) get structurally separate PFNs and package state. PFN
   divergence alone does not isolate the `AppTaskInfo` provider extension registration,
   which needed its own per-pool fix, separately (DIST-14).
4. **Non-disruptive by default** (FAIL-1, "Failure posture toward the host caller"):
   on any failure the CLI no-ops and exits 0, always writing the durable failure log.
   `--strict` opts into real exit codes.
5. **All API + sidecar writes run under the global per-identity mutex** (INFRA-6,
   "Whether CLI read-modify-write sequences need cross-process serialization") —
   `Local\<brand>-<PFN>-tasks-write`, held across the whole read-modify-write.
6. **Expiry freshness ordering** (requirements.md): within any cycle, apply pending
   writes/reconciliation FIRST, then the expiry pass, which re-reads each handle's
   `lastUpdate` under the mutex immediately before comparing to now. Wall-clock
   `lastUpdate`, never a monotonic timer or pre-sleep cached deadline.
7. **Exactly one type imports `Windows.UI.Shell.Tasks`** (INFRA-8, "The seam between
   CLI logic and the WinRT API"): the `AppTaskStore` adapter. Everything else is plain
   code testable against `FakeAppTaskStore`.
8. **Empirical platform knowledge lives as data in one place**: the ERGO-10 safe-combo
   matrix and the fake's fidelity promises each have a single source of truth.
9. **CLI contract is ERGO-27** ("The consolidated v1 command surface") for phases
   01–13 — the plan inlines what each phase needs, but ERGO-27 is the arbiter on any
   surface question. **From phase 15 on, ERGO-31 ("The v2 semantic verb contract")
   supersedes ERGO-27 as the arbiter** (the v1 lifecycle verbs retire; data/util
   verbs and global flags carry forward).

## Phases and sequencing

| # | Phase | Depends on |
|---|-------|------------|
| 01 | [Foundation: solution, identity model, dev loop, AOT](phase-01-foundation.md) | — |
| 02 | [Core seam: `IAppTaskStore`, adapter, fake, logic suite](phase-02-core-seam.md) | 01 |
| 03 | [Real-API adapter test harness + per-worktree identity](phase-03-adapter-test-harness.md) | 01, 02 |
| 04 | [Persistence: write mutex, sidecar store, recycle bin](phase-04-persistence.md) | 02 |
| 05 | [Task operations: validator, advance model, upsert/resurrection](phase-05-task-operations.md) | 02, 04 |
| 06 | [Config, output contract, durable log](phase-06-config-and-diagnostics.md) | 01, 02 |
| 07 | [Icon pipeline: rendering project + icon management](phase-07-icon-pipeline.md) | 01, 04 |
| 08 | [CLI framework + lifecycle verbs](phase-08-cli-lifecycle-verbs.md) | 04, 05, 06, 07 |
| 09 | [Watchdog](phase-09-watchdog.md) | 03, 04, 05, 06, 08 |
| 10 | [Utility verbs: `list`, `clear`, `doctor`](phase-10-utility-verbs.md) | 08 (parallel with 09) |
| 11 | [`run` wrapper](phase-11-run-wrapper.md) | 08, 09 |
| 12 | [Release packaging & distribution verification](phase-12-release-packaging.md) | 09, 10, 11 |
| 13 | [Per-host integration artifacts + docs](phase-13-host-integrations-and-docs.md) | 09, 10 (12 for install docs) |
| 14 | [Host-event behavior recorder + findings corpus](phase-14-host-event-recorder.md) | — (atv-independent tooling) |
| 15 | [v2 semantic engine + integration API contract](phase-15-v2-semantic-engine.md) | 05, 06, 08, 09 |
| 16 | [Icon pipeline v2: theme-neutral tile + BYO image](phase-16-icon-pipeline-v2.md) | 07, 15 |
| 17 | [Repo-scoped presentation defaults + `--cwd` anchor](phase-17-repo-scoped-defaults.md) | 06, 10, 15, 16 |
| 18 | [Claude Code v2 integration: translator + plugin](phase-18-claude-code-v2-plugin.md) | 15, 17 (16 soft), 14's captures |
| 19 | [Card fidelity: subagent activity routing + the never-blank title chain](phase-19-card-fidelity.md) | 15, 17, 18 |
| 20 | [Daily-driver retail identity + plugin command override](phase-20-daily-driver-identity-and-plugin-override.md) | 12, 18 |
| 21 | [Dev-run safety rules in the docs](phase-21-dev-run-safety-rules.md) | — (doc-only; sequenced after 20) |
| 22 | [Create-anchored card defaults: per-repo icon + anchor deep-link](phase-22-create-anchored-card-defaults.md) | 15, 16, 17, 19 |
| 23 | [Dogfood distribution kit](phase-23-dogfood-distribution-kit.md) | 12, 18, 20 (22 soft) |
| 24 | [Dogfood kit: Copilot CLI auto-wiring leg](phase-24-dogfood-kit-copilot-leg.md) | 23; **gated on Copilot access** |

Sequence is topological: 01 → 02 → {03, 04} → 05/06/07 → 08 → {09, 10} → 11 → 12 → 13.
Phases 03 and 04 are independent of each other; 05/06/07 can interleave; 10 can run
parallel with 09.

Phase 14 (added 2026-07-12) is atv-independent diagnostics tooling with no build
dependency on any other phase. It is sequenced after the shipped phase-13 Claude
Code leg and BEFORE the deferred phase-13 Copilot CLI/Codex legs — those legs'
mappings are verified through its captures (LIFE-24 mapping rule 7) — and before
any LIFE-24 v2 work lands in atv. It builds the recorder core once plus the Claude
Code leg only — the integration that proves the core (INFRA-30). The remaining per-host
legs (Copilot CLI, Codex, pi) are each a future phase, not yet run through the process
(INFRA-31, now DEFERRED-until-testable), to be planned when their host becomes testable.

Phases 15–18 (added 2026-07-13) are the v2 line, strictly serial: 15 → 16 → 17 → 18.
Phase 15 replaces the v1 lifecycle verbs with the ERGO-31 semantic contract (LIFE-24's
engine); 16 extends the icon pipeline (theme-neutral tile + `--icon-file`); 17 adds the
repo-scoped `.atv.json` presentation layer + `--cwd` anchor; 18 ships the Claude Code
translator as a native plugin (DIST-11), superseding the phase-13 Claude Code artifact.
The deferred phase-13 Copilot CLI/Codex legs, when their hosts become testable
(INFRA-31), are authored against the ERGO-31 v2 surface following the phase-18 pattern —
not the phase-13 v1 mapping.

Phase 19 (filed 2026-07-14/15, widened 2026-07-15) is phase-18-live-dogfood fallout: two
independent card-fidelity defects that share one supervised validation cycle. Part A
implements ERGO-31 §5's already-decided fan-out addressing rule (a carded subagent's
`activity` currently renders on the parent card), and closes the coverage gap that let it
ship — the fan-out suite tested child-card lifecycle exhaustively but never child-card
*content*. Part B consumes ERGO-33, terminating ERGO-26's precedence chain in a never-blank
built-in default. Everything is automated except one final live dogfood.

Phase 20 (added 2026-07-18) implements DIST-12, which amends DIST-3: the operator's daily
card use moves onto the installed retail identity (Name = brand, alias `atv`), and the
working copy stamps alias `atv-dev` so rebuild/reap can no longer touch the install backing
real sessions. It edits the build-kind-aware alias stamp (`build/Atv.Package.targets`,
phase 12), adds a hand-written `atv-command.txt` command-override tier to both host
translators (the Claude Code plugin, phase 18, and the in-tree Copilot plugin), and rewrites
the identity docs. Automated except the alias-binding, retail-install, and override-smoke
steps, which are live-only (identity attaches only through packaged activation via the alias
shim, so an alias claim is proven by a real registration + a `doctor` read-back, not a build
log). Depends on 12 (the release-identity target + alias stamp) and 18 (the translator + its
stub harness the precedence tests extend).

Phases 21–23 (added 2026-07-19) are the dogfood-feedback line, run in that order after
phase 20. Phase 21 is doc-only: it encodes INFRA-33's dev-run safety rules (validate
through `dotnet run`, throwaway-identity live tests with explicit exact-Name teardown,
`--unregister-on-exit`'s narrow role) into `CLAUDE.md`; it runs first so phases 22–23's
live steps — and any later phase's — inherit the rules, and phase 20's own live checklist
was amended at planning time with INFRA-33's registered-vs-compiled version assertion.
Phase 22 consumes ERGO-34/35: both card defaults (icon, deep-link) move to the engine's
create branch keyed off the ERGO-30 anchor, and the shared structural fix — explicit-flag
gating so plain updates stop rewriting the icon file and deep-link — lands with them.
Phase 23 consumes DIST-13: the one-command dogfood kit (dual-arch dev-signed
`.msixbundle`, public cert, per-integration plugin zips, install/uninstall scripts) — the
pre-publication hand-off path that precedes DIST-10/11's real channels; it hard-follows
20 (override tier + leak-proof zips) and soft-follows 22 so dogfooders receive the fixes
they are evaluating. Its installer auto-wires **Claude Code only**; the Copilot
auto-wiring leg is phase 24, split out 2026-07-19 (operator direction) because the
operator has no Copilot access to verify the wiring commands with — phase 24 carries an
explicit external gate (Copilot access) so 23's completion stays legible and the pending
work is one visible row, not nuance inside a "done" phase.
