# Execution Plan

Built 2026-07-07 from `brief.md`, `requirements.md`, and every DECIDED question under
`questions/` (per `process.md`, discovery and answering are complete); extended
2026-07-12 with phase 14 (INFRA-23's expansion, INFRA-24..29). DEFERRED
questions are OUT of this build's scope: the whole interaction round-trip (INTER-1..4),
DIST-2 (signing-cert acquisition), INFRA-12 (latency budget), INFRA-22 (GUI-subsystem
exe / AttachConsole), LIFE-3 (wire-transport observation).

Each phase file is self-contained: goal, decisions it implements (cited as
`ID ("title")` — the full records live in `questions/`), files affected, acceptance
criteria, and dependencies. A fresh session should be able to implement a phase from
its file alone, consulting the cited question records only for deeper rationale.

## Standing invariants (apply to every phase)

1. **TDD** (brief assertion 1): write tests, see red, implement, see green. Every
   phase lists its test obligations; "implemented but untested" does not meet
   acceptance.
2. **Brand parameterization** (ERGO-18, "The shipped command name"): binary `atv`,
   brand "Agentaskvoid". ONE brand constant feeds identity, command name, env/config
   names, mutex names, paths, artifact names. Never bake the string in twice.
3. **Never hardcode a PFN** (DIST-3, "Dev vs release identity (PFN) divergence"):
   everything PFN-keyed (mutexes, app-data paths) derives at runtime from the current
   package. Three identity pools (release / dev-interactive / per-worktree test) are
   deliberately isolated.
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
9. **CLI contract is ERGO-27** ("The consolidated v1 command surface") — the plan
   inlines what each phase needs, but ERGO-27 is the arbiter on any surface question.

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

Sequence is topological: 01 → 02 → {03, 04} → 05/06/07 → 08 → {09, 10} → 11 → 12 → 13.
Phases 03 and 04 are independent of each other; 05/06/07 can interleave; 10 can run
parallel with 09.

Phase 14 (added 2026-07-12) is atv-independent diagnostics tooling with no build
dependency on any other phase. It is sequenced after the shipped phase-13 Claude
Code leg and BEFORE the deferred phase-13 Copilot CLI/Codex legs — those legs'
mappings are verified through its captures (LIFE-24 mapping rule 7) — and before
any LIFE-24 v2 work lands in atv.
