# Plan Execution Progress

Serial execution of `plan/` phases via Sonnet 5 (max-thinking) subagents.
Each phase: an **executor** subagent implements it, then an independent **reviewer**
subagent — handed the phase file directly, not the executor's summary — signs off only
if (a) all tests pass and (b) the code satisfies **every** acceptance criterion in the
phase file. On review failure, a fresh executor retries with the reviewer's objections;
a phase that fails review **twice** halts the whole run for operator attention.

- Branch: `plan-execution`
- Started: 2026-07-07
- Only advance to the next phase after sign-off.
- **Commit convention:** phases 01–04 committed together as `f706cf4` (they were built as one intermingled snapshot). From phase 05 onward, **one commit per phase, made immediately after reviewer sign-off** (message `Phase NN: <title>`, Co-Authored-By trailer). Branch `plan-execution`; no pushing unless asked.
- **Subagent thinking level:** phases 01–03 ran at max (ultrathink). From **phase 04 onward: Sonnet + xhigh** (operator request 2026-07-08, to keep token usage on track).
- **Lean mode is the DEFAULT** (operator, 2026-07-08): terse subagent reports (verdict + files + test counts + deviations, not essays), suppress git CRLF noise (`git add -A 2>/dev/null`), do all build/verify in subagents (their tool I/O stays out of the main context), never inline. Same verification rigor — only report verbosity shrinks. Reserve verbose for contentious/high-stakes phases, or request expansion on one finding on demand.

## Resuming in a fresh session (handoff)

To move oversight to a new, cheaper session (this one gets expensive to resume after idle — the context re-reads uncached): do it at a **committed phase boundary**, then start a brand-new session (`/clear`, or a fresh `claude` in this repo — do NOT resume this long one) and paste:

> You're the orchestrator continuing the multi-phase build in `plan/` for this repo. Read `progress.md` first (live state + conventions + per-phase log), then `plan/README.md` (phase list). Check `git status` / `git log`. Continue from the next unfinished phase using the loop in `progress.md`: a Sonnet **executor** subagent (xhigh) implements the phase TDD-style and verifies every acceptance criterion by running real commands; then a SEPARATE Sonnet **reviewer** subagent (xhigh) — given the phase file directly, not the executor's summary — signs off only if all tests pass AND every acceptance criterion is met. On failure, fresh executor + reviewer objections; two failures → halt and surface. Commit each phase after sign-off. Run in **lean** mode. Only advance after sign-off.

(The orchestration protocol is also in auto-loaded memory. If a phase is mid-flight and uncommitted in the working tree, the new session should review that in-tree work rather than re-run the executor.)

## RESUME HERE

Phase 22 is ✅ **fully complete** — code half (AC1–AC11) signed off + committed `31f1cbe`, and
**AC12 (live dogfood) PASSED 2026-07-21** (all four checks confirmed; details in the phase-22 log).

Phase 25 is ✅ **fully complete** (2026-07-21) — code half (AC1–AC4) signed off + committed, and
**AC5 closed on the operator's own eyeball of a rendered glyph gallery** (details in the phase-25 log).
**Next: phase 23** (Dogfood distribution kit, DIST-13), then phase 24 (Copilot leg, gated on Copilot
access).

**Phase 25 in one line:** `GlyphRenderer.Render` centers Segoe glyphs by the DWrite line box
(`SetParagraphAlignment(CENTER)`) instead of the glyph ink box, so they ride high on the accent
plate; fix = ink-box centering (IDWriteTextLayout overhang/metrics offset, or alpha-scan recenter).
Emoji unaffected (bare, full-bleed). Phase-16 compositor defect, made visible by phase 22's pool.

**Live-phase protocol that worked, for the next one:** the orchestrator drives, the operator
executes. The operator is the hands and eyes (elevation, registration, eyeballing the taskbar), not
the one deciding what to do next. Issue ONE concrete step at a time, wait, interpret, then issue the
next — never hand over a checklist and go quiet. Read the machine state yourself between steps
(`Get-AppxPackage`, each pool's `atv.log` / `tasks.json` / sidecar, the stamped manifest); those
reads are safe and usually more informative than asking. Give ABSOLUTE paths (the operator's shell
sits at `C:\Users\dhopt`, not the repo root). Verify a cmdlet's flags before prescribing them.
Package registration AND any state-changing verb against bare `atv` are blocked for the agent by the
permission classifier — those go to the operator, prefixed with `! ` so output lands in the session.

**Standing rule, broken once and not to be again:** never `git stash`/revert to re-run a red test on
a box whose PATH carries a live install — a reverted translator's fallback resolved bare `atv` and
drove the operator's real install for real. Judge red-first discipline from test structure instead.

**Status legend:** ⬜ pending · 🔄 executing · 🔍 in review · ✅ signed off · ❌ halted (2 failures)

## Phase status

| # | Phase | Status | Attempts | Review outcome |
|---|-------|--------|----------|----------------|
| 01 | Foundation: solution, identity, dev loop, AOT | ✅ | 1 | PASS (1st) |
| 02 | Core seam: `IAppTaskStore`, adapter, fake, logic suite | ✅ | 1 | PASS (1st) |
| 03 | Real-API adapter test harness + per-worktree identity | ✅ | 1 | PASS (1st) |
| 04 | Persistence: write mutex, sidecar store, recycle bin | ✅ | 1 | PASS (1st) |
| 05 | Task operations: validator, advance model, upsert | ✅ | 1 | PASS (1st) |
| 06 | Config, output contract, durable log | ✅ | 1 | PASS (1st) |
| 07 | Icon pipeline: rendering project + icon management | ✅ | 1 | PASS (1st) |
| 08 | CLI framework + lifecycle verbs | ✅ | 1 | PASS (1st) |
| 09 | Watchdog | ✅ | 1 | PASS (1st) |
| 10 | Utility verbs: `list`, `clear`, `doctor` | ✅ | 1 | PASS (1st) |
| 11 | `run` wrapper | ✅ | 1 | PASS (1st) |
| 12 | Release packaging & distribution verification | ✅ | 1 | build-half PASS (committed 4a84daa); supervised smoke RENDERED ✅ 2026-07-10 |
| 13 | Per-host integration artifacts + docs | 🔄 | 1 | Claude Code + Copilot CLI plugins complete and live-confirmed. Codex deferred. |
| 14 | Host-event behavior recorder + Claude Code findings | ✅ | — | COMPLETE. Core proven by real capture (INFRA-30). 14A fc5785b, 14B-artifacts 1eef442, 14B-capture findings this commit. All 8 ACs met; final reviewer byte-verified findings vs raw captures. |
| 15 | v2 semantic engine + integration API contract | ✅ | 15A:1, 15B:2 | 15A PASS (1st); 15B PASS (2nd, 1 fix: child broken-state refusal) |
| 16 | Icon pipeline v2: theme-neutral tile + BYO image | ✅ | 1 | PASS (1st) |
| 17 | Repo-scoped presentation defaults + `--cwd` anchor | ✅ | 1 | PASS (1st) |
| 18 | Claude Code v2 integration: translator + plugin | ✅ | 1 | build/offline scope (AC1,2,3,4,7) PASS (1st); AC5/AC6 live-dogfooded and confirmed 2026-07-14/15 (operator-supervised) |
| 19 | Card fidelity: subagent activity routing + the never-blank title chain | ✅ | 19A:1, 19B:1, 19D:1, 19E:1 | 19A/19B/19D/19E all PASS (1st) and live-confirmed; 19C (AC11) signed off 2026-07-15 on accumulated evidence, operator decision — see sub-tracking |
| 20 | Daily-driver retail identity + plugin command override | ✅ | 1 | All ACs met. [[DIST-14]] found and fixed mid-AC9 (`a9fdfee`) + build-time manifest validation (`954a259`); AC9's rendering half and AC10's tail closed live 2026-07-21. |
| 21 | Dev-run safety rules in the docs (doc-only) | ✅ | 1 | PASS (1st). All 6 ACs met; item-4 stale prose already reconciled by phase 20's commit `269a164` (verified, not re-touched). |
| 22 | Create-anchored card defaults: per-repo icon + anchor deep-link | ✅ | 1 | Code half AC1–AC11 PASS (1st, `31f1cbe`); AC12 live dogfood PASSED 2026-07-21 (all 4 checks). Surfaced an off-center Segoe-glyph-tile finding → phase 25. |
| 25 | Glyph ink-box centering on the accent tile (phase-22 AC12 fallout; **executes before 23**) | ✅ | 1 | Code half AC1–AC4 PASS (1st, `377b65b`); AC5 closed on operator's own eyeball of a 30-glyph rendered gallery 2026-07-21. |

### Phase 14 sub-tracking (single plan file, strict Part A → Part B ordering)

| Sub | Scope | Status | Attempts | Outcome |
|-----|-------|--------|----------|---------|
| 14A | Recorder core + Gate A (AC1–3) | ✅ | 1 | PASS (1st). Reviewer independently reproduced 0/0 build, 47/47 ×3, clean AOT + byte-exact standalone smoke, separation both ways. |
| 14B-artifacts | Matrix (AC4) + conduit template + driver/stage harness + cue script | ✅ | 1 | PASS (1st). 30-event matrix (only WorktreeCreate skip); recorder proven stdout-silent; staged-conduit smoke byte-faithful; cue disable/restore authored-not-run. |
| 14B-capture | Live Claude Code capture (AC5) + findings (AC6) | ✅ | — | PASS. 4 real captures; findings byte-verified vs raw JSONL by final reviewer; LIFE-24 items 2 & 3 answered; SessionEnd sync-teardown proven. |

### Phase 19 sub-tracking (single plan file; 19A/19B independent, 19C supervised)

| Sub | Scope | Status | Attempts | Outcome |
|-----|-------|--------|----------|---------|
| 19A | Part A: carded-subagent `activity` redirect + regression/baseline tests (AC1–7) | ✅ | 1 | PASS (1st, committed `c2d2efd`) |
| 19B | Part B: ERGO-33 engine default + `session_title` forwarding (AC8–10) | ✅ | 1 | PASS (1st, committed `1d61385`) |
| 19D | Part C: premature `ready` mid-fan-out — found live during 19C itself (AC12–16) | ✅ | 1 | PASS (1st, committed `3e82800`) |
| 19E | Part D: cancelled subagent (`TaskStop`) never fires `SubagentStop` — found live during 19C's re-run (AC17–19) | ✅ | 1 | PASS (1st, committed `15cb1cf`). **Live-confirmed 2026-07-15**: re-run of the 3-subagent-cancel scenario in the scratch repo, operator report "worked like I expected" — child card retired, parent unblocked. (First live attempt against this fix showed no effect; root cause was a stale scratch-repo plugin copy, not the fix itself — `atv.exe`'s dev-loop refresh doesn't touch the separately-copied `translate.ps1`/`map.json` under the scratch repo's `.claude/skills/`; re-synced and confirmed.) |
| 19C | AC11: live dogfood covering both parts | ✅ | — | **Operator-supervised, not subagent-able. Signed off 2026-07-15 on accumulated evidence (operator decision), not one single final clean run.** Three live rounds total, each surfacing a further defect beyond original Part A/B scope (19D, then 19E), all fixed and individually live-confirmed: fan-out routing + non-blank titles (Part A/B, earlier rounds), mid-fanout `ready` refusal (19D), cancellation cleanup (19E, this round). Operator judged a dedicated final combined run would only re-confirm already-verified mechanics |

**Scope note for the executor/reviewer loop (2026-07-15):** subagents run **AC1–AC10 only**.
19A and 19B are independent (no shared code, no ordering dependency) and take one commit each
after their own sign-off; they are split so Part A's `ApplyClaim` architecture change gets its
own review surface and a halt there does not entangle Part B's mechanical work. AC11 (19C) is
excluded from both subagents for the same reasons as phase 18's AC5/AC6 — see the phase-18
orchestration note below. The same hard constraints carry over verbatim: never touch
`~/.claude/settings.json` or this repo's `.claude/settings.local.json`, never launch a real
`claude`/`claude -p` session, never fire a real hook, exact-PID-only process handling, no raw
Ctrl+C. The orchestrator runs 19C directly with the operator, same pattern as phases
12/13/14/18.

## Checkpoint C1 — manual taskbar dogfood (after Phase 10, before Phase 11) ✅ RENDERED

**OUTCOME (2026-07-10): RENDERED ✅.** Operator visually confirmed a real taskbar icon. Orchestrator drove `dotnet run -- -- start dogfood-c1 --title "ATV taskbar dogfood" --subtitle "…" --icon FavoriteStarFill` under dev-interactive identity (`Codevoid.AgentTaskVoid-bbbb1168_016qghrny08mj`); `list --json` returned the live card `[{"handle":"dogfood-c1","state":"running",…}]` and the sidecar `dogfood-c1.json` was on disk; the operator eyeballed the taskbar and saw the new standalone star icon ("looks good"); `clear` then emptied it (`list`→`[]`, sidecar dir empty), so the operator also watched it vanish. **This is the first human confirmation that `AppTaskInfo` actually paints on this machine's Win11 taskbar — the product's core premise, previously only programmatic.** Also CLOSES the long-deferred visual items: phase-07 AC6 (drawn Segoe glyph renders on the taskbar), phase-08 AC4, phase-09 AC4 taskbar-empties eyeball, phase-10 AC4. `IsSupported() -> true` on this build; the `CLASS_E_CLASSNOTAVAILABLE` risk did not materialize here. **Dev-run note for future sessions:** the `winapp`-redirected `dotnet run` needs a DOUBLE `--` to pass app args (`dotnet run --project src/Atv -- -- <verb> …`); a single `--` is rejected by winapp ("Unrecognized argument"). The packaged app's stdout DOES pipe back to the console.

(original checkpoint definition, kept for provenance:)

## Checkpoint C1 — manual taskbar dogfood (after Phase 10, before Phase 11) [DEFINITION]

**Not a plan/ phase — an operator-run verification gate the orchestrator must honor before advancing to Phase 11.** Phase 10 is the last of the genuinely dogfoodable work (11 = `run` wrapper, 12 = packaging, 13 = docs), so this is the natural seam to close the single check that has been deferred four phases running.

- **What's still unverified:** whether a real `Windows.UI.Shell.Tasks.AppTaskInfo` task actually *paints an icon on this machine's Windows 11 taskbar*. Every phase since 07 carried this as an OPEN manual item. ALL evidence to date is programmatic (the API accepts create/list/remove and returns correct data; icon PNGs are valid bytes) — nobody has confirmed an icon appears. "API accepted the call" ≠ "icon rendered." This is compatible with both (benign) they render but tests create-and-remove them in a sub-second blink, and (concerning) they don't render at all on this build (`CLASS_E_CLASSNOTAVAILABLE` / activation-registry caveat: API callable on a build where taskbar integration isn't wired). It is the product's whole reason to exist, so settle it empirically here.
- **Procedure:** under the dev-interactive identity (`dotnet run`), use the phase-08 `start` verb to create a task that PERSISTS (unlike the adapter tests, which immediately `Remove()`), then the operator eyeballs the taskbar; follow with `list` (confirms API sees it) and `clear` (confirms it disappears). Orchestrator supplies the exact command sequence; the eyeball is inherently operator-manual. A durable-icon variant (`start --icon …`) closes phase 07's AC6 at the same time.
- **Outcome to record here:** RENDERED ✅ / DID-NOT-RENDER ❌ (+ what `list` reported either way). A DID-NOT-RENDER result is important to surface NOW, not at release — it would reframe phases 11–13.
- **Gate:** do not advance to Phase 11 until this is run and its outcome recorded. (If the operator defers the eyeball, note it explicitly rather than silently skipping.)

## Detail log

### Phase 01 — Foundation ✅ (signed off 1st attempt)
- **Files:** created `Directory.Build.props`, `build/Atv.Package.targets`, `version.json`, `src/Atv/{Atv.csproj,Branding.cs,Program.cs}`, `src/Atv/Package/AppxManifest.template.xml`, `src/Atv/Package/Public/.gitkeep`, `src/Atv/Properties/launchSettings.json`; moved logos to `src/Atv/Package/Assets/`; rewrote `CLAUDE.md`/`README.md`/`AppTaskInfoCli.slnx`; deleted `AppTaskInfoCli.csproj`, root `Program.cs`, `Register-Identity.ps1`, `Unregister-Identity.ps1`, `app.manifest`, `identity/`.
- **Result:** clean build; package identity `Codevoid.AgentTaskVoid-bbbb1168_0.1.0.39392_arm64` via winapp dev loop; manifest stamped under `obj/` only w/ WriteOnlyWhenDifferent; AOT publish 2.64 MB (arm64 2.76 MB), zero warnings; create/list/clear round-trips on Debug + Release AOT.
- **Review:** PASS. AC1–AC6 independently reproduced. Invariants #2 (single brand source) and #3 (no hardcoded PFN) upheld. Residual: taskbar-render is visual-only (not agent-observable); win-x64 API activation inconclusive under arm64 emulation → native arm64 round-trip substituted.
- **Note for later phases:** `AppxManifest.template.xml` display strings are hand-typed brand literals (settled per DIST-7) — a rebrand touches both `Branding.cs` and the template.

### Phase 02 — Core seam ✅ (signed off 1st attempt)
- **Files:** created `src/Atv/Store/{IAppTaskStore.cs,AppTaskView.cs,AppTaskStore.cs}`; `tests/Atv.LogicTests/` (csproj + `MSTestSettings.cs` + `Store/{FakeAppTaskStore.cs,FakeAppTaskStoreTests.cs}` + `Architecture/SeamPurityTests.cs`); `docs/testing/fake-fidelity-promises.md`. Modified `src/Atv/Program.cs` (routed onto the seam) and `AppTaskInfoCli.slnx`.
- **Result:** 16/16 logic tests green (MSTest+MTP, `EnableMSTestRunner`); `AppTaskStore.cs` is the sole importer of `Windows.UI.Shell.Tasks`, enforced by a non-vacuous `SeamPurityTests`; DTO content model faithful to the documented API; AOT publish clean (~2.79 MB); suite runs with no identity/API.
- **Review:** PASS. AC1–AC5 independently reproduced; invariants #7 (single importer) and #8 (fidelity promises single source) upheld.
- **Deferred notes from review (non-blocking):** (a) `SeamPurityTests` only walks `src/Atv/**`, not `tests/**` — widen if test-side WinRT usage ever becomes plausible. (b) Fixed at orchestration: stale "no test project exists yet" line in `CLAUDE.md`.

### Phase 03 — Real-API adapter harness ✅ (signed off 1st attempt)
- **Files:** created `tests/Atv.AdapterTests/*` (MTP exe: `IdentityGate.cs`, `TasksJsonReader.cs`, `AdapterFidelityTests.cs`, `PeriodicClobberTests.cs`, `AssemblySetup.cs`, `MSTestSettings.cs`, `Package/AppxManifest.template.xml`), `build/Atv.TestIdentity.targets`, `tools/Atv.TestIdentityTool/*` (subprocess `PackageManager` helper), `global.json` (routes `dotnet test` through native MTP). Modified `AppTaskInfoCli.slnx`, `docs/windows-ui-shell-tasks/README.md`.
- **Key discovery:** a directly-launched exe does NOT carry package identity → uses the plan-permitted AppExecutionAlias fallback via a hard-link to the alias stub. Test identity pool = `Codevoid.AgentTaskVoid.Test.<worktree-hash>` (isolated from dev/release). Registration is external (MSBuild `_MTPBuild`/`_TestRunStart` hook → subprocess tool); in-test gate only asserts, SKIPs when identity absent.
- **Result:** adapter suite single-pass green (17 pass / 1 periodic skip), serial; clears tasks before+after; unregister/sweep external and not required; API-absent → skip+exit0; periodic 4×100 clobber gated off by default (manual run: ~67/400 survive → confirms last-writer-wins).
- **Review:** PASS. All 7 ACs met. Deviations assessed OK: `global.json` (no regression — logic suite still 16/16, AOT clean), `--ignore-exit-code 8` (verified doesn't mask failures — broken assert still exits 2), hardlink identity mechanism (sound, gate never registers in-proc). Residuals: full concurrent-worktree run (formula proven), sweep actual-reap path (code-reviewed, blocked from live exercise). Executor's "CLAUDE.md is stale" claim was mistaken; fixed the one real stale line at orchestration.

### Phase 04 — Persistence ✅ (signed off 1st attempt; executor completed inline by orchestrator)
- **Execution note:** the executor subagent hit a session-token limit after writing all source + test files but before verifying. Orchestrator finished the verification pass inline: fixed 2 compile errors (`ReconcilerTests` missing `using Atv.Store`; `Assert.ThrowsException`→`Assert.Throws` ×2 for MSTest 4.x) and one **real concurrency bug** (`SidecarStore.Write` atomic rename threw `UnauthorizedAccessException` when a lock-free reader held the destination open → added bounded `ReplaceAtomically` retry, 100×2ms, preserves atomicity); cleaned 6 MSTEST0037 style warnings. Independent reviewer still ran as a separate subagent.
- **Files:** created `src/Atv/Persistence/{WriteGate,SidecarStore,SidecarEntry,Reconciler,RecycleBin,AppPaths,HandleEncoding,PersistenceJsonContext}.cs`; `tests/Atv.LogicTests/Persistence/{WriteGateTests,SidecarStoreTests,ReconcilerTests,RecycleBinTests,HandleEncodingTests,AppPathsTests,CountingAppTaskStore,TempDirectory}.cs`.
- **Result:** solution builds 0 warn/0 err; logic suite 69/69 green (incl. phase-02's 16); src AOT publish clean (~2.92 MB). WriteGate takes a raw `System.Threading.Mutex` (no abstraction), holds it across the whole critical section, handles AbandonedMutex; sidecar = reversible percent-encoded per-handle files, atomic temp+rename, wall-clock `lastUpdate` every write; reconciler 4 rules + structural no-FindAll/no-sweep proof via `CountingAppTaskStore`; recycle bin TTL round-trip + scavenge + hot-path-never-enumerates proof.
- **Review:** PASS. AC1–AC5 met; invariants #2/#3/#5/#6 verified by grep + code read; SidecarStore retry fix assessed sound.
- **Note for phases 05/09:** `RecycleBin.Tombstone` uses temp+rename WITHOUT the SidecarStore retry — OK under its current "all members run inside WriteGate" contract, but if a lock-free recycle-bin reader is ever added, guard it the same way.

### Phase 05 — Task operations ✅ (signed off 1st attempt)
- **Files:** created `src/Atv/Operations/{SafeCombinationMatrix,Validator,AdvanceModel,Resurrection,TaskOperations}.cs`; `tests/Atv.LogicTests/Operations/*` (11 files: harness + matrix/validator/advance + per-verb start/step/state/done-fail-attention/resurrection/concurrency/remove).
- **Result:** 132/132 logic tests green (63 new + 69 prior); build 0/0; AOT clean. Matrix = 7 safe cells as data in `SafeCombinationMatrix.cs` (independently cross-checked vs `state-content-compatibility.md`). Each verb: WriteGate(once) → reconcile → miss-path recycle check → validate → store write → sidecar stamp. FIFO cap 10; step preserves+re-sends read state; resurrection within TTL restores core info + fresh steps.
- **Review:** PASS. AC1–AC7 met; invariants #1/#4/#5/#6/#8 upheld; no scope creep (returns structured outcomes, no arg-parse/exit-code/icon-render).
- **Deviation RATIFIED by operator (2026-07-08):** `start` on a *never-seen* handle **creates** (not a no-op). Operator chose "ratify + tidy docs" → amended `plan/phase-05-task-operations.md` (resurrection bullet + AC6) and `questions/usage-ergonomics/ERGO-25-*.md` (recycle-bin caveat) so the records match the shipped code: (a) never-seen no-op applies only to the five update-class verbs; `start` creates; (b) a resurrecting `start` uses the caller's fields (not the tombstone's), fresh steps. Included in the phase-05 doc-tidy commit.

### Phase 06 — Config, output contract, durable log ✅ (signed off 1st attempt; lean mode)
- **Files:** created `src/Atv/Config/{Settings,SettingsLoader,SettingsJsonContext}.cs`, `src/Atv/Diagnostics/{FailureLog,Posture,Output}.cs`; tests `tests/Atv.LogicTests/{Config,Diagnostics}/*` (6 files).
- **Result:** 206/206 logic tests green (74 new); build 0/0; AOT clean. Precedence flag>env>file>default (STJ source-gen, flat string→string map); brand-derived env names; non-disruptive posture (exit0 + always-logged failure) w/ `--strict` exit vocab 1/2/3/4 + `--json` `{ok,reason}`; durable log w/ size+age rotation, swallows write failures; INFRA-13 capability→exit(2/3) mapping folded into `Posture`.
- **Review:** PASS. AC1–AC5 met; invariants #1/#2/#4 upheld; scope held (no doctor/watchdog/run wiring). Deviations all ACCEPTABLE: `--json`+`--strict`→strict exit wins; some JSON contexts folded into DTO files; `MutexWaitBudget`=`WriteGate.DefaultTimeout`.
- **Notes for phase 08 (composition root):** (a) wire `SettingsLoader`'s `Warnings` seam into `FailureLog` at startup (loader deliberately has no Diagnostics dep); (b) add a `--json`+`--strict` combination test (currently untested, non-blocking).

### Phase 07 — Icon pipeline ✅ (signed off 1st attempt; lean mode)
- **Files:** created `src/Atv.IconRendering/*` (discrete project: `GlyphRenderer`/`GlyphProbe`/`ShapeRenderer`/`SoftwareCanvas`/`PngEncoder` + CsWin32 `NativeMethods.txt`/`.json`); `src/Atv/Icons/{IconTokens,IconService}.cs`; tests `tests/Atv.IconRendering.Tests/*`, `tests/Atv.LogicTests/Icons/*`, `.../Operations/TaskOperationsIconTests.cs`. Made icon-aware (extension points): `src/Atv/Operations/{TaskOperations,Resurrection}.cs` (optional `IconService?` collaborator — reap on remove/reconcile-drop/per-handle-drop, real resurrection move-back). `Reconciler.cs`/`RecycleBin.cs` untouched (react to their return values). `Atv.csproj` P2P ref + `.slnx`.
- **Tech:** zero-GPU D2D/DWrite/WIC software render target; CsWin32-generated source-gen COM interop (build-time only, `PrivateAssets=all`, `allowMarshaling:false`); hand-rolled PNG encoder (avoids IStream interop); one fixed 64px PNG per glyph (no scale-150 — `IconUri` takes plain file paths, no OS scale-selection); raw file-path escape hatch shipped. Separate-by-session per-handle copies; single-owner move model (live XOR recycle).
- **Result:** build 0/0; LogicTests 236/236 (206 prior + 30), IconRendering.Tests 11/11; AOT clean, **exe delta = 0 bytes** (2,922,496). Deviations (CsWin32, hand-rolled PNG) reviewer-ACCEPTABLE.
- **Residuals (non-blocking):** AC4 default→drawn-shape tier untested end-to-end (no seam exists by ERGO-22 design; shape renderer directly tested; indirect coverage adequate). *Candidate future tech-debt: add a GlyphProbe seam to IconService if full fallback-chain coverage is ever wanted.*
- **⚠️ AC6 STILL OPEN (correction 2026-07-09):** the real-taskbar visual is NOT verified by a human. The executor subagent *claimed* to render + view sample PNGs, but that only checks pipeline output, not the taskbar; neither operator, orchestrator, nor reviewer eyeballed anything. Programmatic evidence (valid PNG bytes/dims, cache/move/reap) is solid; the genuine AC6 taskbar render remains a manual check the operator (or a real end-to-end dogfood) must close. (Earlier "eyeballed" wording in commit f5b801d overstated this.)
- **Forward:** phase 08 wires `start --icon` + default; phase 09 calls the `MoveToRecycle` ops; phase 10 wires `clear` bulk icon purge.

### Phase 08 — CLI framework + lifecycle verbs ✅ (signed off 1st attempt; lean mode)
- **Files:** created `src/Atv/Cli/{CommandLine,Dispatcher,CompositionRoot,WatchdogGate}.cs`; tests `tests/Atv.LogicTests/Cli/*` (7 files) + `tests/Atv.AdapterTests/LifecycleVerbsEndToEndTests.cs`. Modified `src/Atv/Program.cs` (POC deleted → thin main), `src/Atv/Persistence/AppPaths.cs` (+watchdog mutex name, LIFE-18), `PostureTests`, `AppPathsTests`.
- **Result:** hand-rolled AOT-safe parser (globals anywhere, per-verb flags after verb); `CompositionRoot` = sole prod-instance producer; 7 verbs wired through phase-05 ops under the phase-06 posture wrapper; defaults (per-handle icon, `file:` deepLink); ERGO-2 sweep on start/remove, not step; `EnsureWatchdog`/`WatchdogGate` present but INERT (phase 09 supplies hosts). Both parked phase-06 items landed (loader Warnings→FailureLog; `--json`+`--strict` test). LogicTests green + adapter suite 24 pass/1 skip (incl. ≥1 e2e per verb). AOT 0 warnings. Invariant #7 re-verified.
- **Cross-phase fix:** real-adapter tests surfaced a genuine platform bug — `AppTaskContent.CreateSequenceOfSteps` throws `E_INVALIDARG` on an empty `executingStep` (fake never modeled it). Fixed at source: `AdvanceModel.NoStepsYetPlaceholder="Not started yet."` baseline, treated as "nothing to archive". Rippled into `TaskOperations`/`AdvanceModel` + their phase-05 tests (reviewer confirmed adaptations legit, not weakened) + `docs/windows-ui-shell-tasks/AppTaskContent.md`.
- **Review:** PASS. AC4 taskbar-visual residual (unautomatable); dogfood confirmed programmatically via real `tasks.json`.
- **⚠️ Notes to carry forward:** (a) **Binary 3.97 MB** (4,160,512 B) vs 2.92 MB — cause: `Atv.IconRendering` D2D/WIC interop is now REACHABLE via `CompositionRoot`→`IconService` (phase 07 had trimmed it as unreachable, hence its 0-byte delta). Over INFRA-2's ~3.5 MB soft ceiling. Non-blocking; **natural home for a trim pass = phase 12 (release packaging / DIST-5 AOT size verification)**. (b) Per-verb value flags aren't verb-scoped — e.g. `step h --title x` is accepted-and-ignored (outside AC text; minor). (c) AC4 real-taskbar visual still an OPEN manual check (see phase 07 note) — closeable now that `start --icon` works.

### Phase 09 — Watchdog ✅ (signed off 1st attempt; lean mode; executor stopped mid-verify then resumed by a fresh executor on the same in-tree work)
- **Files:** created `src/Atv/Watchdog/{WatchdogLoop,EnsureWatchdog,IWatchdogHost,ProcessHost,InProcThreadHost,BootRecovery,StartupTaskControl}.cs`, `src/Atv/Cli/Verbs/WatchdogVerb.cs`, `build/Atv.DevReap.targets`, `tests/Atv.AdapterTests/WatchdogProcessHostTests.cs`, `tests/Atv.LogicTests/Watchdog/{WatchdogLoopTickTests,WatchdogLoopRunTests,EnsureWatchdogTests,BootRecoveryTests,FakeWatchdogHost,WatchdogTestHarness}.cs`. Modified `src/Atv/Cli/{CompositionRoot,Dispatcher}.cs`, `src/Atv/Program.cs`, `src/Atv/Persistence/RecycleBin.cs` (+`WipeAll`), `src/Atv/Properties/launchSettings.json` (+"watchdog (foreground)" / "app + spawn" profiles), `tests/Atv.AdapterTests/AssemblySetup.cs`, `tests/Atv.LogicTests/Cli/DispatcherHarness.cs`, `RecycleBinTests.cs`. Deleted phase-08's inert `src/Atv/Cli/WatchdogGate.cs` + `tests/.../Cli/WatchdogGateTests.cs`.
- **Result:** build 0/0; LogicTests **349/349** (+34 net; +39 phase-09 watchdog unit tests, −5 deleted gate tests), reproducible across 5 full-suite runs (executor fixed one *test-only* parallelism flake in an abandoned-mutex arrange step — product Win32 logic was correct). AdapterTests **25 pass / 1 skip** (periodic clobber gated off, phase-03 precedent). AOT clean, **4.49 MB** (~+326 KB from new `Process`/`StartupTask`/`AppInstance` surface — extends INFRA-2 overage, phase-12 trim job, non-blocking).
- **AC3 (required real-process integration):** `WatchdogProcessHostTests` genuinely spawns a detached `atv watchdog` via `ProcessHost` (fire-and-forget, no child-handle await), asserts the real `Local\<brand>-<PFN>-watchdog` mutex appears→persists→disappears (real `Mutex.OpenExisting` on the production-derived name), a real WinRT-created card is `Remove()`d, a recycle tombstone is written, and the process self-exits on empty set. No fake host.
- **Review:** PASS (independent reviewer re-ran build/LogicTests/AdapterTests/AOT itself). AC1–AC3 met-automated; AC4 (reboot) / AC6 (sleep-wake) met-with-open-manual-item (state-machine + fake-clock freshness tests present + non-vacuous with controls); AC5 (dev loop) met (profiles + reap target wired; reap logic code-verified). Invariants #5 (writes under one `WriteGate.TryRun`), #6 (freshness: `ExpireIdle` re-reads sidecar `lastUpdate` per-handle immediately before comparing to wall-clock `now`, proven by rescue test + control), #7 (only `AppTaskStore` imports the WinRT ns; watchdog observes via `Reconciler`→`FindAll()`, NO raw tasks.json reader), #2/#3 (brand/PFN derived at runtime) all pass. Manifest `StartupTask TaskId="CodevoidAgentTaskVoidBootRecovery"` matches `StartupTaskControl.TaskId` exactly.
- **Orchestrator fix at sign-off:** corrected the stale `CLAUDE.md` line that said the profile sets bare `WATCHDOG_MODE=off` — the real resolved var is brand-derived `ATV_WATCHDOG_MODE` (`SettingsLoader.CurrentEnvVarName`); the executor had correctly fixed the actual code (`launchSettings.json` + `AssemblySetup.cs`, which prevented every real-adapter run from silently spawning a live detached watchdog) but not the doc prose. (Same doc-drift precedent as phase 02.)
- **⚠️ Open manual checks (AC4/AC5/AC6):** physical reboot → clean taskbar + wiped recycle bin; F5 "watchdog (foreground)" breakpoint hit + a real stale-watchdog dev-reap; laptop sleep/wake past the idle period without reaping a card rescued by a fresh write. All three are unautomatable and remain operator-manual (honest precedent from phase 07/08) — closeable opportunistically; do not block phase progression.
- **Non-blocking note for later:** no Dispatcher-level test asserts `EnsureWatchdog` is invoked from each of the 6 lifecycle-verb call sites (`DispatcherHarness` exposes the fakes for it; wiring verified by code read). Nice-to-have, outside AC text.

### Phase 10 — Utility verbs (`list`, `clear`, `doctor`) ✅ (signed off 1st attempt; lean mode)
- **Files:** created `src/Atv/Cli/Verbs/{ListVerb,ClearVerb,DoctorVerb}.cs`, `src/Atv/Diagnostics/DoctorChecks.cs` (`DoctorProbes`/`DoctorContext`/`DoctorReport`/`DoctorChecks` — injected-probe core, testable with zero OS access); tests `tests/Atv.LogicTests/Cli/{ListVerbTests,ClearVerbTests,DoctorTests}.cs`, `tests/Atv.LogicTests/Diagnostics/DoctorChecksTests.cs`, `tests/Atv.AdapterTests/UtilityVerbsEndToEndTests.cs`. Modified `src/Atv/Operations/TaskOperations.cs` (+`List()`/`TaskListEntry` lock-free, +`ClearAll()`/`ClearSummary` under one `WriteGate.TryRun`), `src/Atv/Diagnostics/Posture.cs` (+`RunQuery` for verbs with their own `--json` shape — thin `RunCore(emitMutatingResult:false)`, same backstop/log/`--strict` mapping as `Run`, `Run` behaviorally untouched), `src/Atv/Watchdog/EnsureWatchdog.cs` (+`IsRunning(mutexName)` liveness probe), `src/Atv/Cli/{CommandLine,Dispatcher,CompositionRoot}.cs` (`--include-recycle-bin`; route the 3 verbs; wire real probes incl. registry-based Dev Mode via `Microsoft.Win32.Registry`, no new package), `src/Atv/Program.cs` (usage text), + harness/coverage in `DispatcherHarness`/`CommandLineTests`/`PostureTests`.
- **Result:** build 0/0; LogicTests **407/407** (+58); AdapterTests **27 pass / 1 skip** (+2 real-API e2e: `List_CorrelatesRealCardsWithSidecarHandles`, `Clear_RemovesRealCards_...NoOpSecondTime` — both drive the live WinRT API, no fakes). AOT clean, **~4.37 MB** (INFRA-2 overage, phase-12 trim; not a regression).
- **Verbs:** `list [--json]` identity-global via `FindAll()`, sidecar handle-correlation, entryless tasks still listed, stdout=data. `clear [--include-recycle-bin]` purges every task+sidecar+per-handle icon (entryless included) under the WriteGate reconcile-then-act; recycle bin excluded by default / wiped (records+co-located icons via `RecycleBin.WipeAll`) with the flag; no prompt, no `--all` gate; clean 2nd-run no-op; render cache survives. `doctor [--json][--verbose]` = identity(+PFN)/API `IsSupported()` (wrapped)/Dev Mode(dev-facing)/config+app-data+sidecar+log paths (ERGO-26)/watchdog liveness/`winget install <brand-derived placeholder>` remedy; always exit0 unless `--strict` (identity+API feed the code; dev-mode/watchdog never do).
- **Review:** PASS (independent; re-ran build/LogicTests/AdapterTests/AOT + live `doctor`/`list`). AC1–AC3+AC5 met-automated; AC4 = doctor output/paths verified live, taskbar-empties eyeball deferred (phase 07/08/09 precedent). Invariants #2/#3/#4(RunQuery)/#5/#7 all pass. All 3 executor deviations accepted (own-`Posture` call, brand-derived winget placeholder, `clear` purges entryless — all sound / spec-aligned).
- **Live evidence (executor, real dev identity):** `start demo-task` → `list --json` `[{"handle":"demo-task",...}]` → `clear --json` `{"ok":true,"reason":"Cleared 1 task(s)..."}` → `list --json` `[]`. `doctor` reported `api: IsSupported() -> true` on this machine → **encouraging for Checkpoint C1** (API is active here), but the task was cleared before any taskbar eyeball, so C1 still needed.
- **Non-blocking staleness to fix later (pre-existing, NOT phase-10 diff — do NOT bundle into unrelated commits):** (a) `Capability.Check` error text still points at `Register-Identity.ps1` (deleted phase 01) — user-facing; good candidate for phase 12 (doctor/capability is release-verification) or a small tidy. (b) `RecycleBin.WipeAll` doc comment says "boot-recovery only" but `clear --include-recycle-bin` now also calls it — cosmetic.

### Phase 11 — `run` wrapper ✅ (signed off 1st attempt; lean mode; executor hit a session limit mid-write, resumed by a fresh executor on the same in-tree work)
- **Files:** created `src/Atv/Cli/Verbs/RunVerb.cs`, `src/Atv/Run/{ChildProcess,OutputPump,StepPublisher,LineHygiene}.cs`; tests `tests/Atv.LogicTests/Run/{FakeChildProcess,LineHygieneTests,OutputPumpTests,StepPublisherTests,RunOrchestratorTests,RunTestHarness,ChildProcessRealTests}.cs` + `tests/Atv.LogicTests/Cli/RunVerbDispatchTests.cs`. Modified `src/Atv/Cli/{CommandLine,CompositionRoot,Dispatcher}.cs`, `src/Atv/Operations/TaskOperations.cs` (+`ReplaceSteps`/`TouchKeepAlive` seam for the wrapper), `src/Atv/Program.cs` (usage), `tests/Atv.LogicTests/Cli/{CommandLineTests,DispatcherHarness}.cs`, `tests/Atv.LogicTests/Persistence/CountingAppTaskStore.cs` (+`UpdateCallCount`). Config tunables (`RunUpdateDebounce`=2s, `RunStepMaxLength`, `RunKeepAliveInterval`) already existed in phase-06 `Settings`/`SettingsLoader` (that phase anticipated them) — brand-derived env names, no new gap.
- **Result:** build 0/0; LogicTests **461/461** (+54; stable across 5 runs); AdapterTests **27 pass / 1 skip** (no regression from the ops change). AOT clean, **~4.56 MB** (climbing: 3.97→4.37→4.56 across 08→10→11; INFRA-2 trim = phase 12).
- **Behavior:** `run --title <t> [--icon <tok>] -- <cmd…>`; mints its OWN unique per-run handle (`atv-run-<timestamp>-<guid>`, the one sanctioned exception to caller-supplied handles); launch→`start`, output→debounced `step` stream (10-line rolling buffer, whole-buffer per tick, no read-back, never an empty step → `AdvanceModel.NoStepsYetPlaceholder`), exit0→`done`/nonzero→`fail`, finished card LINGERS. Decoupled reader (byte-for-byte terminal mirror, never blocks) / debounced updater (100-line burst = ONE update, proven via real `UpdateCallCount`). Silent-child keepalive touches sidecar `lastUpdate` with no content write. Ctrl+C forwards + escalates, no orphan, no stuck Running card. `LineHygiene` = fixed 6-step pipeline (ANSI strip / `\r` collapse / control scrub / trim / drop-blank / truncate+ellipsis), max-length the only knob, everything on the "explicitly OUT" list stays out. Applies ONLY to the step copy.
- **Exit-code passthrough (the ERGO-27 C2 crux):** wrapper exit == child exit ALWAYS; `--strict` affects only PRE-launch failures (bad args/can't spawn). `RunOrchestrator.Execute` takes no `--strict` param at all (structural proof). Test deliberately uses child code 3 (collides with `FailureKind.IdentityNotRegistered=3`) to prove genuine passthrough, not coincidental mapping. Confirmed live (0→completed, 3→error, --strict+5→5).
- **Review:** PASS (independent; re-ran build/LogicTests/AdapterTests/AOT + live dogfood). AC1–AC4 met-automated (AC3/AC4 spawn REAL children — `cmd.exe` byte-fidelity + `ping -n 30` real-signal/no-orphan); AC5 lifecycle+exit-code verified live, taskbar-scroll eyeball operator-manual (C1 already proved the platform renders here). Both deviations ACCEPTED: (1) Ctrl+C test installs the same `CancelKeyPress` absorption production uses + calls the real `RequestCancel` (raw `CTRL_C_EVENT` would kill the test host) — exercises the real Win32/escalation/child/orphan path; (2) 2s debounce (phase-06 default) satisfies the actual coalescing requirement at any positive interval, config-tunable. Invariants #2/#3/#4/#5/#7 all pass.
- **Gotcha for future agents driving `run` via the Bash tool:** Git-Bash MSYS path translation mangles `cmd /c <arg>` (the `/c` gets rewritten as a path). Drive `run`-with-`cmd` dogfoods from PowerShell, or avoid `cmd /c`. Product is unaffected — pure Bash-tool artifact.

### Phase 12 — Release packaging ✅ (build-half committed 4a84daa; supervised smoke RENDERED ✅ 2026-07-10)
- **Build-half + identity amendment (reviewer PASS, committed):** `build/Atv.Release.targets` (`-t:AtvRelease` → publish x64+arm64 → winapp package → dev-cert sign; no-op repack proven), `build/winget/manifests/...` (winget validate PASSED; `PackageIdentifier=Codevoid.AgentTaskVoid` matches doctor), finalized winget id wired into `doctor`, `docs/release.md` runbook (incl. §3 supervised-smoke script), CLAUDE.md release+identity sections, `.gitignore +artifacts/`. Build-kind-aware stamping in `build/Atv.Package.targets` + tokenized alias in the template; `src/Atv/Diagnostics/BuildKind.cs` + `(dev)`/`(test)` marker wired into doctor/`--version`/every FailureLog entry. LogicTests **490/490** (+29), AdapterTests 27/1, AOT 0 warnings (measured **4.60 MB x64 / 4.79 MB arm64** — within INFRA-2's accepted 3–5 MB; supersedes the earlier 4.38/4.56 trim-investigation figures).
- **The amendment ([[DIST-3]]/[[DIST-7]], ratified 2026-07-10):** dev-interactive and the dev-cert release resolved to the IDENTICAL PFN (`Codevoid.AgentTaskVoid-bbbb1168_016qghrny08mj`) — `PublisherId` hashes the declared Publisher STRING, not the signing cert (proven by computing the hash; no install). Fix = build-kind-aware Name/alias: **release** = clean pathhash-free `Codevoid.AgentTaskVoid` owning `atv`; **dev** unchanged (`Codevoid.AgentTaskVoid-bbbb1168` + `atv` — verified still identical, dev loop intact); **test** unchanged (`Codevoid.AgentTaskVoid.Test.<hash>` + `atv-test-<hash>`); **smoke** = throwaway `Codevoid.AgentTaskVoid-reltest` + `atv-reltest` so it coexists with dev. NO `atv-dev` daily command. dev/test build self-marks `(dev)`/`(test)`.
- **Reviewer non-blocking note (guarded):** `-p:AtvVerifyIdentity=true`/`AtvReleaseIdentity=true` must ONLY be passed via `-t:AtvRelease` — on a bare `dotnet build` they'd restamp the dev-loop obj manifest and hijack the next `dotnet run` (now warned in `Atv.Package.targets`). The correct smoke build command is safe.

#### ✅ SUPERVISED SMOKE — COMPLETE (2026-07-10): RENDERED ✅ (native arm64, signed full-MSIX)
Ran the minimal supervised smoke with the operator (arm64 machine; x64 functional-verify stays deferred — no x64 device, native-substitution precedent from phase 01). The fresh verify build was a REAL rebuild, not a no-op: NBGV height had advanced one commit past the stale `0.1.13.21419` reltest set left over from build-half development, producing `Codevoid.AgentTaskVoid_0.1.14.19076_arm64_reltest.msix` (signed, `CN=AppTaskInfoCli`). Operator approved the ONE elevation — trusted dev cert thumbprint `EE72026DD2760D068C0ACA9974F168C8062EEA1B` into `LocalMachine\TrustedPeople`. `Add-AppxPackage` → PFN **`Codevoid.AgentTaskVoid-reltest_016qghrny08mj`**, structurally DISTINCT from dev-interactive `Codevoid.AgentTaskVoid-bbbb1168_016qghrny08mj` (same PublisherId hash — shared static Publisher string — but different Name → different PFN), confirming DIST-3's three-pool isolation is now structural, not cert-dependent. From a FRESHLY-SPAWNED `cmd.exe`: `atv-reltest doctor` → `identity: present`, **`api: AppTaskInfo.IsSupported() -> true`** (the genuinely-new datapoint: a SIGNED FULL-PACKAGE install activates AppTaskInfo, not merely the dev loop), all paths isolated under the reltest `LocalState`. `atv-reltest start s1 --title Hi` + `list --json` reported the live card; **operator eyeballed the taskbar and confirmed the card RENDERED ✅** on the packaged path (complements Checkpoint C1's dev-loop render — the product's premise now confirmed on BOTH the dev-loop and signed-MSIX paths). Uninstall via the dev-safe filter (`Get-AppxPackage -Name "*Codevoid.AgentTaskVoid-reltest*" | Remove-AppxPackage`) → **operator eyeballed the card VANISH** (AC5/DIST-9 on the packaged path, minimal depth); reltest package + `%LOCALAPPDATA%\Packages\Codevoid.AgentTaskVoid-reltest_*` folder + `atv-reltest` process ALL gone, dev-interactive `Codevoid.AgentTaskVoid-bbbb1168_..._016qghrny08mj` verified untouched. AC4 (upgrade-in-place) NOT exercised — documented-as-expected, minimal depth (operator's choice).
- **The `(dev)` marker on the reltest `doctor` identity line is CORRECT, not a bug:** `BuildKindResolver` classifies `-reltest` as Dev-pool (Name is neither the bare brand nor `<brand>.Test.*`), so only the real bare-brand release is unmarked. `docs/release.md` §3.3 had wrongly predicted "no marker"; fixed in this commit.
- **Operator kept the dev cert trusted** (declined the §3.6 removal — "needs it later"). Deliberate deviation from the runbook's full cleanup: `Cert:\LocalMachine\TrustedPeople\EE72026D…` remains on this box. Harmless (throwaway self-signed `CN=AppTaskInfoCli`); noted for anyone auditing the trust store later.

**Phase 12 is DONE.** Next: Phase 13 (per-host integration artifacts + docs) via the normal executor→reviewer loop.

#### ⏭️ HANDOFF [EXECUTED 2026-07-10 — see outcome above] — the MINIMAL supervised smoke (operator + orchestrator together)
Everything above is committed. Do NOT re-run the executors. The only remaining phase-12 work is a supervised install smoke on THIS arm64 machine, minimal depth (operator's choice). Steps (full detail in `docs/release.md` §3):
1. Build the smoke artifact (safe command): `dotnet build src\Atv\Atv.csproj -t:AtvRelease -p:AtvVerifyIdentity=true` (add the VS Installer dir to PATH for the x64 leg per CLAUDE.md). Produces `artifacts\release\msix\Codevoid.AgentTaskVoid_<ver>_arm64_reltest.msix` (identity `Codevoid.AgentTaskVoid-reltest`, alias `atv-reltest`).
2. **Operator approves ONE elevation** — trust the dev cert: `Import-Certificate -FilePath artifacts\release\cert\devcert.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople` (elevated).
3. Orchestrator installs (`Add-AppxPackage ..._arm64_reltest.msix`) and drives AC2/AC3 from a FRESHLY-SPAWNED `cmd.exe`: `cmd.exe /c "atv-reltest doctor"` → confirm `identity: present`, a DISTINCT PFN `Codevoid.AgentTaskVoid-reltest_016qghrny08mj` (NOT the dev `Codevoid.AgentTaskVoid-bbbb1168_...`), `api: IsSupported() -> true`, paths under the reltest LocalState; `cmd.exe /c "atv-reltest start s1 --title Hi"` + `cmd.exe /c "atv-reltest list --json"`.
4. **Operator eyeballs** the taskbar card (the genuinely-new datapoint: a SIGNED FULL-MSIX install drives AppTaskInfo). Record RENDERED ✅ / not.
5. Uninstall + cleanup (dev-safe filter): `Get-AppxPackage *Codevoid.AgentTaskVoid-reltest* | Remove-AppxPackage`; remove the trusted cert; confirm no `Codevoid.AgentTaskVoid-reltest*` package/app-data/process left AND the dev `Codevoid.AgentTaskVoid-bbbb1168` package is untouched.
6. AC4 (upgrade) / AC5 (uninstall) are documented-as-expected (DIST-6/DIST-9; minimal depth) — observe opportunistically, don't block.
7. Record the outcome here, flip phase 12 to ✅, commit that record, then proceed to Phase 13.
**Resume prompt for the fresh session:** the standard orchestrator handoff (read progress.md + plan/README.md + git log) — then: "Phase 12's build-half is committed; run the remaining minimal supervised smoke per this HANDOFF / docs/release.md §3, record the outcome, finish phase 12, then continue to phase 13."

### Phase 13 — Per-host integration artifacts + docs 🔄 CLAUDE CODE LEG + DOCS ✅ SIGNED OFF (1st); Copilot + Codex DEFERRED (discrete follow-ups)
- **Scope narrowed by operator mid-executor (2026-07-10):** ship + fully validate the **Claude Code** integration now; **Copilot CLI and Codex become discrete later steps** (no artifacts shipped for them this pass — README lists them "Planned" per the LIFE-8 addition criterion). So AC3 (Copilot/Codex live) is out-of-scope-deferred; AC1/AC2/AC4 judged on the Claude Code leg only.
- **Files:** created `integrations/claude-code/{settings.hooks.json,README.md}`, `docs/configuration.md`, `docs/maintenance/new-build-checklist.md`, `tests/Atv.LogicTests/Integrations/ClaudeCodeArtifactTests.cs` (4 tests: JSON well-formed + 5 event keys, verb-set validity, exact verb set `{start,state,step,attention,done,remove}`, no `--strict`); rewrote `README.md`. **Zero `src/Atv` changes** (artifacts + docs only) — invariant #2 clean by construction.
- **Executor (Sonnet/xhigh):** verified the Claude Code hook surface against the INSTALLED 2.1.207 + a live fetch of the current docs (found real drift: the hooks doc URL moved to `code.claude.com/docs/en/hooks`; SessionEnd carries `exit_reason` not `reason`). Built the artifact with the phase-05 state-reset-after-attention chain (`state running` ahead of every `step`), re-fire-safe upsert `start`, never `--strict`, `shell: powershell` on every hook (necessary here — Git Bash present, so the default hook shell would be `bash` and the PS one-liners would fail).
- **⚠️ Two REAL bugs the live dogfood caught that the executor's doc-only pass missed — both fixed inline by orchestrator + re-verified:**
  1. **Hallucinated `Stop` matcher.** Executor put `"matcher":"model_complete"` on `Stop` with a `stop_reason`-based rationale. Primary docs: `Stop` supports NO matcher (silently ignored) and has no `stop_reason` field. Removed the matcher (Stop now fires `atv done` every turn-end = intended turn-done semantics); corrected the README's false "restricted to model_complete" claims.
  2. **Async `SessionEnd` lost the cleanup.** Executor made all 5 hooks `async:true`. At session teardown an async (fire-and-forget) `SessionEnd` hook is killed before `atv remove` completes → card left behind on `/exit` (observed live: no removal, no log entry). Made `SessionEnd` synchronous (`timeout:10`, no `async`); other 4 stay async. Re-tested live → card removed on `/exit`. (Docs-confirmed: async = "runs in background without blocking"; cleanup-on-exit wants a sync hook.)
- **AC2 live dogfood (orchestrator + operator, isolated project-scoped hooks in a throwaway dir, dev-interactive `atv` identity):** ALL FIVE hooks observed firing live against a real Claude Code session — `start` (card appeared, real `session_id` as handle, Robot icon), `step` (real `tool_name`/`tool_input`, e.g. `Read: {...notes.txt}`), `attention` (fired on its own via a real **idle** `Notification` after unfocusing the terminal — the permission-prompt path didn't fire because it only notifies on unfocused/delayed, and the operator's config auto-allowed/approved-fast), `done` (turn completion → `completed`), `remove` (card disappeared on `/exit`, after the sync fix). The **phase-05 state-reset-after-attention** property verified live directly on the card (needsAttention → `state running` + `step` → the step LANDS, not swallowed) — operator eyeballed the needs-attention surface (icon + "show details" button) and its recovery. Dogfood dir cleaned up afterward; no stray cards (`list`→`[]`).
- **Reviewer (independent Sonnet/xhigh) PASS:** re-fetched the hooks docs and independently confirmed both orchestrator fixes correct; cross-checked `docs/configuration.md`'s 11 tunables against `Settings.cs`/`SettingsLoader.cs` (every default matches); AC5/AC6 met; AC1/AC4 met; AC2 met-live (artifact internally consistent with the dogfood record); invariant #2 clean. **LogicTests 494/494** (foreground, single run, machine stable, ~4.4s). No defects.
- **LIFE-24 filed (OPEN, dogfood learning):** "The host-event → task-state integration semantics (the mapping layer)" — the operator's observation that the event→state/step mapping is semantically loose (running-at-start; overloaded idle: turn-done vs paused vs tool-permission vs question; review ALL events; subagents maybe → own cards glomming by icon; raw-JSON step passthrough nuance; two-way looming but NOT reopened yet). Framed as a semantic *integration layer*, explicitly not the earlier A/B field/state options. Post-v1, does not change this build.
- **⏭️ Remaining Phase 13 work (discrete follow-up steps, NOT this session):** (1) **Copilot CLI** integration artifact + doc-verified/live-verified surface (host not installed on this box — verify against docs + a machine that has it); (2) **Codex** integration artifact (lowest priority; no session-end event → watchdog-only cleanup). Each is its own executor→reviewer pass. The AC1 "verify against installed version" caveat applies to each when tackled. Until both land, Phase 13 is NOT fully ✅ and the plan is NOT fully complete.
- **2026-07-16 Copilot update (supersedes the "host not installed" blocker above):** Copilot CLI 1.0.71 is installed and its native plugin/hooks surface is live. Added the Copilot host-event recorder leg under `tools/host-event-recorder/hosts/copilot-cli/` plus `docs/host-events/copilot-cli.md`; five real captures (scripted + two supervised interactive + resume + conduit probe), 112 records total. Headline findings: native flat `powershell` hook form works (nested Open-Plugin `args` were ignored); `userPromptSubmitted` precedes `sessionStart` and also carries raw `<system_notification>` wake-ups; session id survives resume; real Blocked signals are `notification:permission_prompt` and `preToolUse:ask_user`, never bare `permissionRequest`; subagent tool hooks use unique child `call_*` session ids while raw `subagentStart/Stop` expose only agent type; task-tool args + child prompt matching provide a possible stateful high-fidelity correlation path; main `agentStop` and prompt-mode `sessionEnd:complete` can fire while background workers remain; `/exit` gives reliable `sessionEnd:user_exit`; manual `/compact` fires `preCompact`; interrupt leaves an orphaned `preToolUse`; synchronous hook process cost is material (~0.6 s median timestamp-to-append, ~2.3 s p95). Recorder leg complete; production Copilot translator/plugin remains.
- **Copilot production plugin built (same date; offline gate):** `integrations/copilot-cli/` now contains a native plugin, minimal synchronous hook set, v2-only translator, map, marketplace, and install/behavior docs. Operator chose the high-fidelity handler-local bridge rather than parent-only degradation: parent `preToolUse:task` stores only `SHA256(cwd + NUL + exact prompt)` + parent/task metadata in `COPILOT_PLUGIN_DATA`; child `userPromptSubmitted` atomically claims the sole match into `call_* -> parent/task`; child tool/stop events then address the existing engine child via parent + `--agent`. Pending/active records expire, raw prompts are never stored, ambiguous duplicate prompts never guess, all state/atv failures exit 0 and degrade to lifecycle-only, and no Copilot transcript/internal store is read. Added real-process translator/artifact tests including a two-process claim race; shared the existing compiled stub-atv process harness with Claude without regressing its suite.
- **Copilot production live dogfood COMPLETE (2026-07-16, operator-supervised):** loaded the real plugin with `--plugin-dir` in `D:\temp\atv-copilot-sandbox`. Two background subagents (`visual-readme`/`visual-sample`) produced parent + 2 child cards, visually confirmed grouped together. The correlation bridge routed each child stream onto its own engine child: both displayed `Running Start-Sleep -Seconds 30`, then their own `Reading README.md` / `Reading sample.txt` activity. Children retired independently; the first Ready retry was correctly refused while its sibling remained; the second retirement let Ready succeed; `/exit` removed the parent. Durable `trace-in`/`trace-out` confirms the full sequence, `atv list --json` ended `[]`, and plugin correlation state ended `{pending:[],active:[]}`. Programmatic sync-fanout dogfood also confirmed exact child routing before the visual run. Copilot leg is complete; only Codex remains for phase 13.
- **Permission-recovery limitation RATIFIED (operator, 2026-07-16):** Copilot's child-raised `notification:permission_prompt` carries only the parent session id and there is no public permission-completed hook, so the parent can remain NeedsAttention after approval until later parent activity/final Ready. Claude's same-locus behavior only clears at the permissioned tool's `PostToolUse` anyway (an hour-long build still means an hour); widening Copilot's synchronous post hooks would add cost without solving that case. Leave current behavior as-is — no timers, transcript reads, UI automation, or disabling permission attention.

### Phase 14A — Recorder core + Gate A ✅ (signed off 1st attempt; lean mode)
- **Files:** created `tools/host-event-recorder/{HostEventRecorder.csproj, Constants.cs, ArgvParser.cs, Envelope.cs, MutexNaming.cs, GuardedAppender.cs, PathResolution.cs, SessionResolution.cs, Recorder.cs, Program.cs}`; `tests/HostEventRecorder.Tests/*` (13 files, MTP/MSTest, references ONLY the recorder — no Atv ref, plain `net10.0`); `docs/host-events/README.md` (conventions + pinned-name mirror). Modified `AppTaskInfoCli.slnx` (+2 members), `.gitignore` (+`tools/host-event-recorder/captures/`).
- **Standing-invariant inversion honored:** recorder consumes no brand constant / no `Atv.*` / no `Windows.UI.Shell` / no package identity; plain `net10.0` (no Windows TFM); vanilla console exe.
- **Pinned plumbing names** (`Constants.cs`, mirrored in README): env vars `HOSTREC_SESSION` (session id; `--session` overrides) and `HOSTREC_CAPTURE_DIR` (capture dir; `--capture-dir` overrides); filename `session-{id}.jsonl`; fallback path basis = `AppContext.BaseDirectory` + `captures/` (never cwd); fallback session id `adhoc-{yyyy-MM-dd}` UTC; mutex name `Local\HostEventRecorder-{sha256-hex-of-normalized-path}`.
- **Result:** build 0/0 with both new projects; LogicTests-style suite **47/47** green ×3 (stable); AOT publish clean (arm64, single 1.82 MB native exe); standalone smoke appended a byte-exact 6-field envelope (non-ASCII/quote/newline round-tripped identically through the escaped `payload` string). Envelope = exactly `{ts,host,event,pid,session,payload}`; `payload` stored as opaque STJ-escaped string, never re-parsed (proven by a non-normalization test).
- **Review:** PASS (independent; reviewer re-ran build/tests ×3/AOT publish + own standalone smoke with tricky payload + separation greps both ways). All 3 Gate A criteria reproduced. **Deviations all ACCEPTABLE:** (1) argv>env precedence for both `--session` and `--capture-dir` (consistent with the one explicit session-tier rule); (2) 40-real-thread concurrency proof (a named OS `Mutex` is arbitrated identically across threads and processes; the suite also has a real-subprocess default-path test) — reviewer judged adequate, flagged a real multi-process append test as optional future defense-in-depth; (3) session-id filename sanitization (invalid FS chars→`_`, tested, low-risk).
- **Non-blocking note:** `EnvelopeRoundTripTests` doc comment overstates that `RecorderCaptureTests` re-proves the tricky payloads through disk (it only runs ASCII through disk) — cosmetic; the AOT standalone smoke covers the real tricky-payload disk path. Tighten when next touched.

### Phase 14B-artifacts — Claude Code leg artifacts ✅ (signed off 1st attempt; lean mode)
- **Files:** created `docs/host-events/claude-code.md` (safe/skip matrix + conduit implementation notes + pending-findings scaffold), `tools/host-event-recorder/hosts/claude-code/{settings.hooks.template.json, stage.ps1, driver-scripted.ps1, cue-script.ps1}`. Orchestrator tidy: refreshed the now-stale "Status" section of `docs/host-events/README.md` (Part A had said `claude-code.md` doesn't exist yet).
- **Matrix (AC4):** 30 candidate events enumerated from the current hooks reference (WebFetch, cross-checked exact by the reviewer). ONE axis (does camping suppress/replace a default host action?). **Only `WorktreeCreate` is skip-classified** — the literal replacement class ("non-zero exit fails creation," no hook-absent fallback). All other 29 SAFE — a passive log-and-exit-0 observer changes nothing even on decision-capable events (`PreToolUse`/`PermissionRequest`/`Stop`/`PostToolUse`) **because the recorder is provably stdout-silent** (0 bytes stdout/stderr, verified empirically by the reviewer — no `Console.Write` anywhere, only `Console.Error` on error paths). `Elicitation`/`ElicitationResult` flagged safe-lower-confidence (docs state no explicit no-fallback contract); INFRA-26's mid-session pull/downgrade is the sanctioned correction path.
- **Conduit template + posture:** exec/args form, NO shell (byte-faithful raw stdin to the recorder, cleaner than a PS `ConvertFrom-Json` round-trip) — `<RECORDER_EXE_PLACEHOLDER>` for the absolute path (no alias/installer/PATH). Camps exactly the 29 safe events, `WorktreeCreate` omitted. `async:true` on all 28 in-turn events; `SessionEnd` ALONE synchronous (`timeout:10`) — the phase-13 teardown-race precedent (INFRA-27).
- **Stage/driver/cue:** `stage.ps1` builds the recorder, makes a throwaway scratch project OUTSIDE the repo, substitutes the real exe path into a scratch-only `.claude/settings.json`, mints+exports `HOSTREC_SESSION`/`HOSTREC_CAPTURE_DIR` (project-scoped, never user-wide). `driver-scripted.ps1` = thin `-p` driver for the scripted beats only (drives no skip event); documents `-p` one-shot limits. `cue-script.ps1` = supervised runbook: disable-user-wide-atv-hooks is step 1, restore is in a `finally` (verbatim `Copy-Item` from an untouched backup, refuses to clobber); walks the full LIFE-24 beat corpus + the Claude-Code-ADDED subagent-permission beat; never fakes a signal. **Authored, NOT executed** (live-session guardrail).
- **Executor caught + fixed a real bug:** `stage.ps1` path-substitution `-replace '\\','\\\\'` double-escaped the JSON path (PS `-replace` has no backslash-escaping); `Test-Path` masked it (Windows tolerates doubled separators). Fixed to single-escape; reviewer reproduced correct.
- **Mechanical validation (no live host):** solution build 0/0; config valid JSON; **staged-conduit smoke** — real stage step + a staged hook line invoked with mock stdin (non-ASCII + quotes) appended a correct 6-field `session-<id>.jsonl` line under `captures/`, byte-faithful; scratch cleaned. Git hygiene: no `captures/` tracked, no scratch config in-repo, `hosts/` = only `claude-code` (AC7/AC8).
- **Review:** PASS (independent; reviewer re-ran build + matrix WebFetch cross-check + recorder-stdout-silence proof + `stage.ps1` + staged smoke + git hygiene). All three flagged items adjudicated ACCEPTABLE (path fix correct; Elicitation lower-confidence honest; `PostToolUse` `decision:block` doc-discrepancy correctly deferred to the capture — doesn't change classification given stdout-silence).
- **⚠️ Environmental note for the live run:** the operator's `~/.claude/settings.json` currently has **no `hooks` key** — the shipped phase-13 atv integration isn't actively installed right now (cue script handles both presence and absence). And a doc discrepancy to settle in the capture: current hooks ref says `PostToolUse` supports `decision:"block"`/`updatedToolOutput`, vs the phase-13 doc's "cannot block; already ran."
- **Remaining (14B-capture, operator-supervised):** AC5 (real supervised capture spanning ≥1 session JSONL over the beat corpus + subagent-permission beat; teardown event captured; did-not-fire results) and AC6 (distilled findings with version+date stamps; LIFE-24 items 2&3 answered). NOT subagent-able — a human at the terminal, per the plan's "no PTY automation in v1."

### Phase 14B-capture — live Claude Code capture + findings ✅ (operator-supervised; final reviewer PASS)
- **How it ran:** operator + orchestrator together (the phase-12/13 pattern), since the live beats (permission dialogs, interrupt, idle) need a human at the terminal — not subagent-able. Orchestrator staged the conduit (`stage.ps1`) and drove the SCRIPTED beats (`driver-scripted.ps1` → real nested `claude -p`); operator drove the INTERACTIVE beats in a separate terminal. Env: Claude Code **2.1.207**, no user-wide atv hooks installed (clean baseline, no disable needed).
- **Four real captures** (all gitignored under `tools/host-event-recorder/captures/`, never committed): `cc-20260712-212159` (scripted -p, 23 rec), `cc-interactive-1` (50 rec: permissions + main-thread interrupt), `cc-interactive-2` (9 rec: interrupt DURING a tool call), `cc-interactive-3` (7 rec: clean idle test). Findings distilled into `docs/host-events/claude-code.md` (Findings section), stamped `2.1.207` / `2026-07-12,13`.
- **Headline results (all byte-verified by the final reviewer against the raw JSONL):**
  - **SessionEnd sync-teardown PROVEN** — captured as the final record of all 4 sessions (`reason:"other"` for -p, `reason:"prompt_input_exit"` for /exit). The phase-13 async-loss bug does not recur with `async:false`+`timeout:10`. (Bonus: `-p` DOES fire SessionEnd, contra the driver's own header comment.)
  - **LIFE-24 item 2 ANSWERED:** subagent-scoped events (`SubagentStart/Stop`, `Pre/PostToolUse`, `PostToolBatch`, `PostToolUseFailure`, **`PermissionRequest`**) carry the subagent's `agent_id`+`agent_type`; main-thread instances don't. `agent_id` is **unique per parallel spawn** (4 distinct ids across 2 runs). **`Notification:permission_prompt` carries NO `agent_id`; `PermissionRequest` does** — so subagent-permission attribution must key off `PermissionRequest`, not the `Notification` the shipped phase-13 hook maps. `TaskCreated`/`TaskCompleted` do NOT fire for subagents.
  - **LIFE-24 item 3 ANSWERED:** `idle_prompt` fires **~60 s** after a turn completes (measured 60.055 s), **once** (no repeat over ~39 min), and is **NOT focus-gated** (fired while focused — correcting an earlier phase-13-based inference). Post-interrupt idle stays weakly answered (interactive-1's idle windows were all <60 s except a pre-/exit gap).
  - **User interrupt fires NO distinguishing hook event** — during generation: nothing; during a tool call: an **orphaned `PreToolUse`** with no `PostToolUse`/`PostToolUseFailure` (so `is_interrupt` is for tool-self-failures, not interrupts). Implication noted for any future `run`-style wrapper.
  - **Field-name correction:** `SessionEnd`'s field is **`reason`**, not `exit_reason` (the phase-13 note in `integrations/claude-code/README.md` is wrong on this — not edited from this phase; atv's hook reads only `session_id` so it's unaffected).
- **Review:** PASS (independent final reviewer; read all 4 raw captures + the doc; re-derived line counts, the 60.055 s idle delta, the 4 distinct agent_ids, the same-`prompt_id` PermissionRequest-vs-Notification agent_id bytes, SessionEnd presence+reasons, the orphaned-PreToolUse interrupt, an exhaustive did-not-fire event scan (13 event names = the Fired set), the `2.1.207` stamp; AC7 no jsonl tracked + gitignored; AC8 only claude-code host; build 0/0, tests 47/47). **All 8 phase-14 ACs met; core proven by real capture (INFRA-30).**
- **Orchestrator tidy at sign-off:** refreshed `docs/host-events/README.md` "Status" (now says phase complete). Non-blocking note left by reviewer (not actioned, design-intent vs actual): the matrix's `PreToolUse` "Driver coverage" cell credits scripted subagent-internal `agent_id` coverage, but the scripted subagents made no internal tool calls — the `agent_id`-bearing tool events actually came from interactive-1; the Findings table attributes this correctly.

### Phase 15 — v2 semantic engine ✅ (15A + 15B both signed off — see "Phase 15 (15A + 15B) is DONE" below)
- **Orchestration note (2026-07-13):** resuming a fresh orchestrator session for phases 15–18. Subagent thinking level bumped to **max-thinking** (operator instruction this session, supersedes the phase-04 xhigh convention for 15–18). New escalation rule for 15–18: on a phase failing review twice, OR reviewer objections being ambiguous/contested, OR execution revealing a later phase's plan is wrong or phases less independent than assumed — spawn an **Opus 4.8 advisor** subagent to diagnose + recommend (it does not implement or change the plan); surface its recommendation to the operator before acting on it. Still one commit per phase (sub-part) after sign-off, lean mode, branch `plan-execution`.
- **Split (orchestrator discretion, per the phase file's own sizing note):** **15A** = verb surface + five-state model + claim semantics + projection legality + stdin/normalizer + surface migration + docs (AC1–4, 7, 8, and the non-clock/non-fanout slice of AC9). **15B** = clocks (presence-gated Ready decay vs hygiene reap) + fan-out addressing (AC5, AC6, remaining AC9 coverage). Mirrors the phase-14A/14B precedent.

#### Phase 15A — v2 verb surface + claim semantics ✅ (signed off 1st attempt)
- **Files:** created `src/Atv/Semantics/{SemanticState,ActivityKind,BrokenReason,SessionEndedReason,Normalizer,Rendering,SemanticEngine}.cs`, `docs/integration-api.md`, `tests/Atv.LogicTests/Semantics/*` (11 files), `tests/Atv.LogicTests/Cli/DispatcherSemanticVerbsTests.cs`, `tests/Atv.AdapterTests/SemanticVerbsEndToEndTests.cs`. Modified `src/Atv/{Program.cs, Persistence/{SidecarEntry,SidecarStore,PersistenceJsonContext}.cs, Cli/{CommandLine,Dispatcher,CompositionRoot}.cs, Cli/Verbs/RunVerb.cs, Operations/TaskOperations.cs}`, `docs/windows-ui-shell-tasks/README.md`, plus mechanical v1→v2 adaptations across existing test files (`DispatcherHarness`, `OperationsHarness`, `RunTestHarness`, `DispatcherPlatformDownTests`, `DispatcherRemoveTests`, `ListVerbTests`, `ClearVerbTests`, `CommandLineTests`, `RunOrchestratorTests`, `ChildProcessRealTests`, `UtilityVerbsEndToEndTests`). Deleted 9 v1-only test files (`TaskOperationsStartTests`, `StepTests`, `StateTests`, `DoneFailAttentionTests`, `TaskOperationsIconTests`, `TaskOperationsResurrectionTests`, `TaskOperationsConcurrencyTests`, `DispatcherStartTests`, `DispatcherUpdateVerbsTests`, `LifecycleVerbsEndToEndTests`).
- **Result:** LogicTests 494→**570/570** green; AdapterTests **31 pass / 1 skip** (periodic clobber, pre-existing gate). Build 0/0. AOT (`win-arm64`) clean, **4.76 MB**. All 8 semantic verbs (`working`/`activity`/`blocked`/`ready`/`broken`/`agent-started`/`agent-stopped`/`session-ended`) implemented with full transition-table + claim semantics; v1 verbs (`start/step/state/attention/done/fail`) rejected (silent non-strict, `InvalidArguments` strict); `list`/`run`/`clear`/`doctor`/`remove`/`watchdog` preserved, `run`'s exit-code/debounce/lingering-card contract intact over v2 internals. `docs/integration-api.md` self-contained, honestly flags fan-out (§5) and clocks (§6) as 15B-not-yet-built.
- **Two real-API discoveries fixed (documented in `docs/windows-ui-shell-tasks/README.md`):** (1) `IconUri` doesn't round-trip an `ms-appx://` URI — adapter tests switched to `file://`; (2) `AppTaskInfo.UpdateTitles` throws on an empty title against an already-live card (though `Create` tolerates it) — would have broken any real translator omitting `--title` on follow-ups. Fixed by making title/subtitle genuinely idempotent claims (absent flag falls back to current value) throughout the engine, matching every other optional field; verified live + regression-tested.
- **Review:** PASS (independent; reviewer reproduced build/LogicTests/AdapterTests/AOT itself, forked a sub-review for the mechanical test diffs, read `docs/integration-api.md` cover-to-cover). AC1,2,3,4,7,8 + in-scope AC9 all independently verified; invariants (#2/#3/#4/#5/#6/#7) upheld; `SeamPurityTests` still non-vacuous. Non-blocking notes for 15B: `--reset`/`ParseResult.Reset` is now a dead vestige in `CommandLine.cs` (accepted-and-ignored, harmless); `EngineMemory.Goal`/`ActiveAgentLoci` bookkeeping exists but isn't consumed by rendering yet — correct 15B on-ramp; `WatchdogLoop.cs` correctly untouched.
- **Left for 15B:** child-card minting (2nd concurrent `agent-started` → `<session>#<agent_id>`, icon-URI reuse, cascade on remove/session-ended), Ready→Idle presence-gated decay clock (Idle currently unreachable), `docs/integration-api.md` §5/§6 to be filled in for real.

#### Phase 15B — clocks + fan-out ✅ (signed off 2nd attempt)
- **Files (attempt 1, uncommitted):** created `src/Atv/Presence/{IPresenceSource,Win32PresenceSource}.cs`, `src/Atv/Watchdog/ReadyDecay.cs`, `tests/Atv.LogicTests/Watchdog/{FakePresenceSource,ReadyDecayPassTests}.cs`, `tests/Atv.LogicTests/Semantics/{ReadyDecayClockTests,SemanticEngineFanOutTests}.cs`. Modified `docs/integration-api.md` (§5/§6 filled in), `src/Atv/Cli/CompositionRoot.cs`, `src/Atv/Config/{Settings,SettingsLoader}.cs`, `src/Atv/Operations/TaskOperations.cs` (+`CascadeRemoveChildren`), `src/Atv/Persistence/{PersistenceJsonContext,SidecarEntry,SidecarStore}.cs`, `src/Atv/Semantics/SemanticEngine.cs`, `src/Atv/Watchdog/WatchdogLoop.cs`, `tests/Atv.AdapterTests/SemanticVerbsEndToEndTests.cs`, `tests/Atv.LogicTests/Watchdog/WatchdogTestHarness.cs`.
- **Result:** LogicTests 570→605/605; AdapterTests 31/1→32/1 (real identity available, incl. a live fan-out mint/cascade e2e); build 0/0; AOT clean ~5.08 MB.
- **Review verdict: FAIL (1 objection).** AC5 (clocks) and everything else in AC6/AC9 independently reproduced and judged non-vacuous (transition-only clock start, presence-gating, courtesy-demotion, two-clocks-independence control, retroactive 1st-worker carding, byte-identical icon, cascade both reasons, structural not string-pattern child detection, SeamPurityTests intact). **The one blocking objection:** the phase file states children are scaffolding "Working/Completed only" (verbatim, also in ERGO-31 §4 and LIFE-24) — but only `Blocked`'s `ApplyClaim` sets `refuseIfChild:true` (`SemanticEngine.cs:150`); `Broken` (line 190) and `SessionEndedErrorCore` (~line 380) don't, so `atv broken <child> --reason timeout` and `atv session-ended <child> --reason error` both succeed live today, leaving a child in Error/Broken — a third state beyond the sanctioned two. No test covers either path against a child handle; `docs/integration-api.md` §5 is silent on it.
- **Fix (2nd attempt, fresh executor):** added `refuseIfChild:true, refusedVerbPhrase:"broken"` to `Broken`'s `ApplyClaim` call; added an equivalent structural (`ParentHandle`-keyed, not string-pattern) guard to `SessionEndedErrorCore`; two new red→green tests (`Broken_AgainstAChildHandle_IsStructurallyRefused`, `SessionEndedError_AgainstAChildHandle_IsStructurallyRefused`) confirmed failing pre-fix, passing post-fix; `docs/integration-api.md` §5 rewritten to state the rule exhaustively (all 3 refused verbs; `session-ended --reason finished`/`remove` against a child explicitly called out as unaffected). Files: `src/Atv/Semantics/SemanticEngine.cs`, `docs/integration-api.md`, `tests/Atv.LogicTests/Semantics/SemanticEngineFanOutTests.cs`, plus 2 non-behavioral doc-comment additions (`AgentStarted` nested-fan-out gap note, `TaskOperations.RemoveCore` `CardedAgentLoci`-desync note).
- **Final files (15B total, both attempts):** created `src/Atv/Presence/{IPresenceSource,Win32PresenceSource}.cs`, `src/Atv/Watchdog/ReadyDecay.cs`, `tests/Atv.LogicTests/Watchdog/{FakePresenceSource,ReadyDecayPassTests}.cs`, `tests/Atv.LogicTests/Semantics/{ReadyDecayClockTests,SemanticEngineFanOutTests}.cs`. Modified `docs/integration-api.md`, `src/Atv/Cli/CompositionRoot.cs`, `src/Atv/Config/{Settings,SettingsLoader}.cs`, `src/Atv/Operations/TaskOperations.cs`, `src/Atv/Persistence/{PersistenceJsonContext,SidecarEntry,SidecarStore}.cs`, `src/Atv/Semantics/SemanticEngine.cs`, `src/Atv/Watchdog/WatchdogLoop.cs`, `tests/Atv.AdapterTests/SemanticVerbsEndToEndTests.cs`, `tests/Atv.LogicTests/Watchdog/WatchdogTestHarness.cs`.
- **Result:** LogicTests 570→**607/607**; AdapterTests 31/1→**32 pass/1 skip** (incl. a real-API fan-out mint/cascade e2e against the live WinRT platform). Build 0/0. AOT (`win-arm64`) clean, **~5.08 MB**.
- **Review (2nd attempt):** PASS. Independently re-verified the whole of AC5/AC6/AC9 fresh (not just the delta) — clock transition-only-start, presence-gating, courtesy-demotion, two-clocks-independence control, retroactive 1st-worker carding, byte-identical icon (exact `Uri` equality), all 3 child-state refusals now structural and `ParentHandle`-keyed (not string-pattern), `session-ended --reason finished`/`remove` against a child confirmed unaffected, cascade both reasons, `SeamPurityTests` intact. `docs/integration-api.md` §5/§6 accurate and self-contained.
- **Non-blocking notes carried forward (documented in code, not fixed — narrow/out-of-band):** manual `remove <child-handle>` (bypassing `agent-stopped`) desyncs the parent's `CardedAgentLoci` bookkeeping; nested `agent-started` against an already-minted child handle is unguarded/untested.

**Phase 15 (15A + 15B) is DONE.** ERGO-31/LIFE-24's v2 semantic engine is fully implemented, tested, and live-verified: 8 semantic verbs, five-state model with claim semantics, projection legality, stdin/normalizer, presence-gated Ready decay separate from the hygiene reap, and fan-out child-card addressing with correct icon-URI reuse and state exclusivity. `docs/integration-api.md` is the normative translator contract for phase 18. Next: Phase 16 (icon pipeline v2).

### Phase 16 — Icon pipeline v2 ✅ (signed off 1st attempt)
- **Files:** created `src/Atv.IconRendering/{TileCompositor,RasterNormalizer,PixelExtraction}.cs`, `tests/Atv.IconRendering.Tests/{PngTestReader,RasterNormalizerTests,TileRenderingTests}.cs`, `tests/Atv.LogicTests/Cli/DispatcherIconFileTests.cs`. Modified `src/Atv.IconRendering/{GlyphRenderer,SoftwareCanvas,NativeMethods.txt}`, `src/Atv/{Icons/{IconTokens,IconService},Cli/{CommandLine,Dispatcher},Program}.cs`, `docs/integration-api.md`, `tests/Atv.LogicTests/{Cli/CommandLineTests,Icons/IconServiceTests}.cs`.
- **Result:** LogicTests 607→**634/634**; `Atv.IconRendering.Tests` 11→**33/33**; AdapterTests unchanged **32 pass/1 skip** (no live-API surface touched). Build 0/0. AOT clean both arches, win-arm64 **~5.09 MB** (essentially zero delta from phase 15B's ~5.08 MB, as predicted — WIC decoders are system DLLs).
- **Behavior:** monochrome Segoe glyphs now composite onto a filled rounded-rect accent tile (`#0078D4`, 20% corner radius) — default Robot renders white-on-tile, fixing the out-of-box bare-black-glyph contrast problem (ERGO-28). Emoji and caller-supplied logos render bare (recorded build-detail choices, each covered by a real pixel-level test). `--icon-file <path>` is now a supported, validated, normalized input on all 8 v2 semantic verbs except `session-ended` (7 upserting verbs) — PNG/JPG/ICO → WIC-decoded → 64px fit (downscale + aspect-preserving transparent letterbox pad) → flattened/normalized transparency → cached per-handle with the exact phase-07 ownership/lifecycle machinery (remove-reap, expiry tombstone, resurrection move-back, TTL purge). `--icon`+`--icon-file` together is a usage error (non-strict silent no-op / strict `InvalidArguments`). `run` deliberately excluded — still only takes legacy `--icon` (own private resolver in `RunVerb.cs`, unmodified) — noted for phase 17/18 awareness.
- **Untrusted-input hardening (AC3), adversarially reviewed:** byte-size cap checked via `FileInfo.Length` before any read; decompression-bomb defense via `IWICBitmapFrameDecode.GetSize()` header check (2048px cap) before pixel materialization, proven against a real crafted 50000×50000-declared/tiny-body PNG fixture; format allowlist by magic bytes, never extension; path traversal proven impossible (destination always `HandleEncoding`-derived from the handle, never the source path); source file proven never modified.
- **Real bug caught and fixed:** the Segoe glyph render-once cache key (`segoe-{codepoint}-{size}`) wasn't versioned against the new tile-compositing algorithm — a pre-existing dev-identity's cached bare-black-glyph PNG would have been served forever, silently defeating the whole contrast fix. Caught via this machine's persistent dev-loop identity (not reproducible from a fresh temp-dir unit test). Fixed by renaming to `segoe-tile-*`; the 3 phase-07 tests hardcoding the old key updated to match (reviewer grepped the whole repo to confirm zero stale references remain).
- **Review:** PASS (independent; reviewer reproduced build/LogicTests/IconRendering.Tests/AdapterTests/AOT itself, adversarially re-checked all AC3 security properties, confirmed the cache-key fix by repo-wide grep). AC1–6 fully met with genuine pixel-level (not vacuous) test evidence. **AC7 (manual taskbar dogfood): met-with-open-manual-item**, per the established phase 07/08/09/10/11 precedent — strong programmatic pixel-correctness evidence stands in for the literal operator eyeball, which remains open/non-blocking. Dogfood commands for whenever the eyeball happens: `dotnet run --project src\Atv -- -- working <handle> --title "Tile check"` (default tile) and `... --icon-file <path-to-png-or-jpg-or-ico>` (BYO logo), then `list --json` / `clear`.

### Phase 17 — Repo-scoped presentation defaults + `--cwd` anchor ✅ (signed off 1st attempt)
- **Files:** created `src/Atv/Config/RepoSettings.cs` (anchor resolution, `.atv.json` discovery walk, allowlist filter, `.git/HEAD` branch read, template expansion), `src/Atv/Icons/IconGroupRegistry.cs` (repo-grouping owner registry, self-healing, atomic writes), `tests/Atv.LogicTests/Config/{RepoSettingsTests,SettingsLoaderPresentationTests}.cs`, `tests/Atv.LogicTests/Semantics/SemanticEngineRepoDefaultsTests.cs`, `tests/Atv.LogicTests/Cli/DispatcherRepoDefaultsTests.cs`. Modified `src/Atv/Config/SettingsLoader.cs`, `src/Atv/Semantics/SemanticEngine.cs` (repo defaults applied only in the genuine-creation branch of `ApplyClaimCore`), `src/Atv/Cli/{CommandLine,Dispatcher,CompositionRoot}.cs`, `src/Atv/Diagnostics/DoctorChecks.cs`, `src/Atv/Cli/Verbs/DoctorVerb.cs`, `src/Atv/Program.cs`, `docs/{configuration.md,integration-api.md}`, `README.md`.
- **Result:** LogicTests 634→**697/697**; IconRendering.Tests unchanged 33/33; AdapterTests unchanged 32 pass/1 skip. Build 0/0. AOT (win-arm64) clean, **~5.12 MB** (+~30 KB from phase 16).
- **Behavior:** repo-local `.atv.json`, discovered by walking up from a `--cwd` anchor (never the process's own cwd — hooks spawn from arbitrary dirs; falls back to process cwd only for direct human use), nearest-wins, stops at `.git`/filesystem root. Allowlist is the entire trust mechanism: only `title-template`/`subtitle`/`icon`/`icon-file`/`group` can have any effect; `deep-link` and all operational keys are ignored + durably logged if present (verified live: a throwaway malicious `.atv.json` trying to smuggle both was correctly rejected-and-logged). Applied ONLY on card creation (`SemanticEngine.ApplyClaimCore`'s never-seen-handle branch — proven by counting-spy tests, 1 discovery call on create, 0 on update). Full 5-layer precedence (`flag>env>repo>user>default`) proven both directions of the two easy-to-invert orderings (repo-beats-user, env-beats-repo). `{repo}`/`{branch}` templates expand via cheap `.git/HEAD` reads (no `git` subprocess), covering `refs/heads/*`, detached HEAD, and worktree `.git`-as-file; missing info is dropped (not left literal). `doctor --verbose` surfaces anchor+source+found-file+parse-status — live-confirmed as a one-look diagnosis.
- **Review:** PASS (independent; reviewer reproduced build/LogicTests/AdapterTests/AOT itself, ran a real dev-loop dogfood confirming both the allowlist rejection and `doctor` output live, traced the allowlist code path end-to-end as the phase's central security property). Executor's deviation (repo-defaults logic lives in `SemanticEngine.cs` not `Operations/*` per the phase file's literal list) confirmed correct — post-phase-15, `TaskOperations` no longer owns any create/upsert path.
- **Non-blocking notes for phase 18:** `run`-minted cards don't pick up repo title/subtitle/icon-override defaults (consistent with phase 16 excluding `run` from `--icon-file`) — translators using `run` won't get repo branding; `--cwd` is a clean `GlobalOptions` field (parallel to `--watchdog-mode`), recognized anywhere, costs nothing when absent — good shape for phase 18's `--cwd ${CLAUDE_PROJECT_DIR}` forwarding. Two narrow, untested-but-defensible precedence edge cases noted (icon-file always beats icon regardless of layer; repo `group:true` overrides an explicit caller icon on create) — not security issues, not blocking.

### Phase 18 — Claude Code v2 plugin ✅ (signed off 1st attempt; live dogfood complete 2026-07-14/15)
- **Orchestration note (safety-scoped, 2026-07-14):** this project's process/hook testing has previously crashed a Claude Code session (operator-confirmed incident, two known mechanisms: overly-broad process kills colliding with something waiting on the process, and raw console Ctrl+C hitting the whole console process group — see memories [[process-termination-can-crash-claude-code]] / [[raw-ctrl-c-can-crash-claude-code]]). Both the phase file's own AC5 ("LIVE dogfood... not subagent-able") and this precedent led the orchestrator to scope BOTH the executor and reviewer to AC1/2/3/4/7 only (the buildable/testable artifact), explicitly excluding AC5 (live session dogfood: Working/Blocked/Ready/fan-out/removal) and AC6 (repo branding through the real conduit). Both subagents were given hard constraints: never touch `~/.claude/settings.json` or this repo's `.claude/settings.local.json`, never launch a real `claude`/`claude -p` session, never fire a real hook, exact-PID-only process handling, no raw Ctrl+C. The orchestrator will run AC5/AC6 directly with the operator, same pattern as phases 12/13/14's supervised steps.
- **Files:** created `integrations/claude-code/.claude-plugin/marketplace.json`, `integrations/claude-code/plugins/atv-integration/{.claude-plugin/plugin.json, hooks/hooks.json, translate.ps1, map.json}`, `tests/Atv.LogicTests/Integrations/{ClaudeCodePluginArtifactTests,ClaudeCodeTranslatorHarness,ClaudeCodeTranslatorTests}.cs`, `tests/Atv.LogicTests/Integrations/TestAssets/StubAtv/{Program.cs,StubAtv.csproj}`. Modified `integrations/claude-code/README.md` (rewritten), `README.md`, `docs/{configuration.md,integration-api.md,host-events/claude-code.md}`, `tests/Atv.LogicTests/Atv.LogicTests.csproj` (exclude StubAtv from the recursive glob). Deleted `integrations/claude-code/settings.hooks.json` (phase-13 fragment), `tests/Atv.LogicTests/Integrations/ClaudeCodeArtifactTests.cs`. **Zero `src/Atv` changes** (confirmed both by executor and independently by reviewer).
- **Result:** LogicTests 697→**738/738**. Build 0/0. No AOT re-check needed (no `src/Atv` touched).
- **Behavior:** ships as a real Claude Code **plugin** (manifest + hooks declaration + `translate.ps1` + `map.json`), superseding the phase-13 one-liner fragment — install wires all hooks with zero `settings.json` hand-edits (two mechanisms verified structurally: skills-directory plugin, and local marketplace + `enabledPlugins`). Every hook line is a plain `-File` program+args invocation of `translate.ps1` (no embedded one-liners, no `shell:` selection); async on every in-turn event, `SessionEnd` alone synchronous (`timeout:10`); free text rides stdin (`--flag -`) never argv; explicit UTF-8 both ends; payload fragments never re-serialized. Full event→verb mapping table implemented per `docs/host-events/claude-code.md`'s phase-14 capture findings (`PermissionRequest`-not-`Notification` attribution, `SessionEnd`'s real `reason` field). `--cwd ${CLAUDE_PROJECT_DIR}` forwarded on every non-terminal call.
- **Deliberate deviation, verified correct by both subagents:** `translate.ps1` never passes `--title`/`--subtitle`/`--icon` (unlike the old v1 artifact) — an explicit flag always beats phase 17's repo `.atv.json` per `SemanticEngine.ApplyRepoDefaults`'s precedence (`flag>env>repo>user`), so hardcoding identity flags would permanently defeat repo branding. Locked in by a dedicated test; both executor and reviewer independently read the actual phase-17 precedence code to confirm.
- **Capture staleness (AC3):** installed Claude Code is **2.1.209** vs. the capture stamp **2.1.207** — a 2-point drift, honestly flagged in both the capture doc and plugin README, no re-capture attempted (correctly deferred — **orchestrator decision needed before the live dogfood**: re-run an INFRA-29 organic re-capture first, or proceed and treat the dogfood itself as the freshness check).
- **Two real PowerShell bugs found and fixed:** `$OutputEncoding = [Text.Encoding]::UTF8` silently prepends a BOM (fixed via a BOM-less `UTF8Encoding` construction); `powershell.exe -File` mishandles a bare `-` token when the target is itself a PS script (worked around by compiling the test-only stub `atv` as a real exe rather than a PS-script stub).
- **Review:** PASS (independent; reviewer reproduced build/tests itself, live-fetched current Claude Code plugin docs from `code.claude.com` and confirmed the manifest/hooks schema against them — not memory, per the phase-13 lesson — including confirming the "skills-directory plugin" zero-config mechanism is real and accurately described; reproduced the `claude --version` check itself, 2.1.209; grep-swept for any retired v1 verb, zero hits). **Safety-constraint compliance independently confirmed**, including catching and correcting a stale claim: `~/.claude/settings.json` DOES exist (predates this session, 2026-07-13 21:58, the operator's own personal settings, no `hooks` key) — untouched by the executor either way. Minor non-blocking note: `map.json`'s `StopFailure` reason-vocabulary keys don't exactly match the current documented `error_type` values, but the code's fallback-to-`fatal` makes this harmless and the row is already flagged best-effort/uncaptured.
- **Orchestrator fix at this checkpoint:** the reviewer's safety check surfaced that the orchestrator's own EARLIER in-session check of `~/.claude/settings.json` ("NO FILE") was itself wrong — a Bash-tool PowerShell invocation bug (`$env:USERPROFILE` is PowerShell syntax; Bash pre-mangles `$env:...` before PowerShell ever sees it). Re-checked directly via the PowerShell tool: file exists, no `hooks` key, confirming the original safety conclusion (no ambient atv hooks) was right, but for an unverified reason at the time. Lesson: use the PowerShell tool directly for PowerShell-syntax commands, never embed `$env:`/`$_`-style syntax inside a Bash `-Command` string.
#### Live dogfood (AC5/AC6), operator + orchestrator together, 2026-07-14/15

**Setup:** an isolated scratch repo (`<temp>/atv-cc-dogfood`, own `git init`, outside
`AppTaskInfoCli` entirely — operator's explicit choice over the repo-scoped
`<repo>/.claude/skills/` alternative), plugin installed via README Option A
(skills-directory, zero `settings.json` edits) — never the operator's real
`~/.claude/skills/`. `atv` itself needed no rebuild (already registered dev-identity,
reachable from any cwd). Claude Code was **2.1.210** at dogfood time (one further point
past the 2.1.209 build-time note / 2.1.207 capture stamp) — no re-capture triggered, no
behavior difference attributable to the drift was observed.

**Per-event evidence** (same bar as Checkpoint C1):
- **`working`** ✅ RENDERED — card appeared with the real goal text within ~1s; reproduced
  twice (two fresh sessions, two different handles), confirmed both visually and via
  `atv list --json`.
- **`activity`** ✅ RENDERED — tool-call lines updated live.
- **`blocked`** ✅ RENDERED — a real `PermissionRequest` (denied a PowerShell parse-check)
  correctly set state `needsAttention` with the real question text, same-locus attributed.
- **`blocked` recovery** ✅ CONFIRMED, with a real finding: a **denied** permission request
  aborts the whole Claude Code turn (`turn ended in error: [ede_diagnostic] turn aborted
  (aborted_tools) stop_reason=tool_use`, per a `claude --debug-file` capture) — and that
  abort path fires **no `Stop` hook at all** (none registered in the capture, vs. a normal
  turn's clear `Registering async hook ... (Stop)` line). Not a plugin bug: there is no
  Claude Code hook for "turn aborted by a manual permission denial." The card recovers on
  the very next `UserPromptSubmit` (confirmed live) or on `SessionEnd`. Separately
  investigated and ruled out as a fix: Claude Code's `PermissionDenied` hook exists but
  fires only for auto-mode-classifier denials, never a manual interactive deny — confirmed
  empirically by wiring `PermissionDenied` into the **scratch-only** `hooks.json` copy and
  observing zero registration in a `--debug-file` capture of a real manual denial (the
  committed `integrations/claude-code` plugin was never touched by this experiment).
- **fan-out** ✅ RENDERED — 2 real parallel subagents; durable-log timestamps show both
  child cards minted together at spawn and each retired **independently**, ~9.5s apart, at
  its own real completion time — correct `agent-started`/`agent-stopped` → mint/retire.
- **removal (`SessionEnd`)** ✅ CONFIRMED on `/exit`.
- **AC6 repo branding** ✅ CONFIRMED — a `.atv.json` (`title-template`/`subtitle`/`icon`/
  `group`) dropped into the scratch repo; a **new** session (repo-config discovery is
  create-only, per `docs/configuration.md`) rendered title `atv-cc-dogfood (master)` with
  the configured subtitle/icon, and `atv doctor --verbose` confirmed the file was found and
  parsed `ok` — the real `--cwd ${CLAUDE_PROJECT_DIR}` conduit, not a synthetic call.
- **Ready→Paused decay** (phase 15B, not a phase-18 AC but validated live here for the
  first time) ✅ CONFIRMED — operator let a `Completed` card sit undisturbed and observed
  the watchdog demote it to `Paused` after the designed ~5-minute presence-gated
  `ReadyDecayThreshold`, exactly matching `src/Atv/Watchdog/ReadyDecay.cs`.
- **`StopFailure`/Broken:** not exercised, per the README's own best-effort note — not
  forced artificially.

**Findings surfaced** (all live-only signal; none were visible in the offline/doc-only
build — the recurring lesson of this whole integration):

1. **Fixed this session — real, reproducible `atv` bug.** `SemanticEngine.ClaimReady`'s
   bare (no-`--summary`) re-affirmation of an already-`TextSummaryResult`-held Ready card
   threw `"Unhandled exception: ... executingStep cannot be empty"` against the real
   platform — hit on **every** real session, ~60s after each `ready`, via Claude Code's
   `idle_prompt` `Notification` → bare `ready <sid>` call (harmless in effect, swallowed by
   `translate.ps1`'s FAIL-1 `Invoke-Atv` wrapper, but silently broken every time, and
   littering the durable log). Root cause: `CurrentSteps(ctx)` reads back an empty
   `ExecutingStep` for a `TextSummaryResult`-held card, and the no-summary path fed that
   straight into a new `SequenceOfSteps` — the same hazard `ReadyDecay.DemoteToIdle`
   already guards against, but `ClaimReady` didn't. **Fix:** identical guard
   (`executing.Length > 0 ? executing : AdvanceModel.NoStepsYetPlaceholder`) in
   `src/Atv/Semantics/SemanticEngine.cs`'s `ClaimReady`. New regression test
   `Ready_BareReassertion_AfterASummaryResult_NeverProducesAnEmptyExecutingStep`
   (`tests/Atv.LogicTests/Semantics/SemanticEngineTransitionTests.cs`; LogicTests
   738→**739/739**). Live-verified against the real platform both pre-fix (reproduced the
   exact log line) and post-fix (clean, `executingStep` reads back `"Not started yet."`,
   zero log entries) — required a rebuild via `dotnet run` (a plain `dotnet build` does
   NOT refresh the loose-layout dev registration) after the prior watchdog process
   self-exited (cleared the live card, no process kill needed).
2. **Filed as phase 19 (not fixed this session)** — now `plan/phase-19-card-fidelity.md`,
   renamed 2026-07-15 when the phase widened to carry ERGO-33 as well.
   A carded subagent's own tool-call activity renders on the **parent** card instead of its
   own child card — `docs/integration-api.md` §5's already-decided addressing rule ("a
   subagent's own further activity should target the CHILD handle directly... not the
   parent") isn't implemented. Root cause is in `atv` itself, not the translator:
   `translate.ps1` already sends the documented `atv activity <session> --agent <agentId>
   ...` call shape correctly; `SemanticEngine.ClaimActivity` just never consults the
   engine's own `EngineMemory.CardedAgentLoci` (which already knows whether `agentId` has a
   minted child card) to redirect the claim there — it only ever uses `agentId` for
   `blocked`-locus clearing. No translator change needed; the fix is engine-side claim
   routing, its own design pass (see the phase file for why).
3. **ERGO-33 filed:** operator feedback that an empty default title (no `.atv.json`) reads
   as unpolished. DECIDED later the same day (2026-07-14) after widening from "the built-in
   default" to the whole title/subtitle chain; consumed into phase 19 on 2026-07-15. Not
   implemented.

**Resulting file changes this session:** `src/Atv/Semantics/SemanticEngine.cs` (the
`ClaimReady` fix), `tests/Atv.LogicTests/Semantics/SemanticEngineTransitionTests.cs` (+1
regression test), `plan/phase-19-fanout-activity-child-routing.md` (new; since renamed
`plan/phase-19-card-fidelity.md`),
`plan/README.md` (phase table +1 row), `questions/usage-ergonomics/ERGO-33-...md` (new)
+ its README index line. `integrations/claude-code/` itself: **zero changes** — the
shipped phase-18 plugin was correct as committed at `1e29427` throughout this dogfood.

**Phase 18 is DONE.** All 7 ACs are signed off — build/offline (1/2/3/4/7, prior attempt)
and live (5/6, this session). Next open item in the plan/ tree: phase 19 (filed, not
started).

### Phase 19A — carded-subagent `activity` redirect ✅ (signed off 1st attempt; committed `c2d2efd`)
- **Concurrency note:** 19A and 19B executors ran as parallel subagents in the SAME working
  directory (not separate worktrees) — an orchestrator experiment, not standing practice. Both
  landed non-overlapping hunks in `src/Atv/Semantics/SemanticEngine.cs` (19A: `Activity`/
  `ClaimActivity` region ~L146-266; 19B: `ApplyRepoDefaults`/title-default region ~L664-886) and
  both independently verified the full LogicTests suite green with both sets of changes present
  (767/767). No merge conflicts; both executors reported transient build/read artifacts from the
  shared `obj`/`bin` (spurious failures, one false "file reverted" read) that resolved cleanly on
  retry/rebuild — no data loss. **Given the coordination overhead this cost (both executors had
  to actively work around each other, and the orchestrator then had to git-stash-surgery the two
  parts apart before independent review — see below), future phases should default back to
  serial execution or real `git worktree` isolation for split sub-phases, not same-directory
  parallel subagents.**
- **Orchestrator isolation step (between executor and reviewer):** since 19A/19B's only shared
  file (`SemanticEngine.cs`) had cleanly non-overlapping hunks, the orchestrator split the diff
  with `git apply --cached` (staged exactly 19A's hunk + its 3 whole-file additions) then
  `git stash push --keep-index -u` (set 19B's remaining changes aside), leaving the working tree
  with ONLY Part A's diff for the 19A reviewer. After 19A's commit, `git stash pop` auto-merged
  cleanly (non-overlapping hunks) to restore Part B for its own isolated review. Verified clean
  before/after each step (`git status`/`git diff --stat`).
- **Files:** `src/Atv/Semantics/SemanticEngine.cs` (new `ActivityCore`/`ApplyRedirectedActivity`/
  `ClaimActivityParentLocusOnly`, `Activity()` now owns its own `WriteGate.TryRun`); new
  `tests/Atv.LogicTests/Semantics/SemanticEngineActivityRedirectTests.cs` (13 tests, AC1-7 —
  a sibling file to `SemanticEngineFanOutTests.cs` rather than an addition to it, a reasonable
  deviation from the phase file's file list); `tests/Atv.LogicTests/Persistence/
  CountingAppTaskStore.cs` (+`CreateCallCount`, proves AC6's no-`IconService.Place` claim
  structurally since `IconService` is sealed/no-interface); `docs/integration-api.md` §5
  (redirect mechanism documented, replacing stale "translator should address the child" prose).
- **Result:** a carded agent's `activity` now redirects the CONTENT claim to the child's own
  sidecar entry (same `ApplyClaimCore` pipeline a direct child call takes, byte-identical icon
  URI reuse, zero new `IconService.Place`/`Create` calls) while the PARENT still gets its
  same-locus block-clearing in the same `WriteGate` critical section (two sidecar writes, one
  mutex — the design note's "shaping problem, not a locking one"). Uncarded/retired agentId
  falls through unchanged (never resurrects a retired child); `blocked --agent` untouched
  (parent-only, decision point 1).
- **Review:** PASS (independent reviewer, re-ran build/tests itself: 752/752 LogicTests + 13/13
  new redirect tests in isolation, NativeAOT clean 4.88 MB). All 7 ACs judged individually with
  specific test evidence; traced AC6's structural proof through the actual `Place`/`Create` call
  graph to confirm soundness; confirmed invariant #5 (one `WriteGate` critical section, two
  sidecar writes) by code read; confirmed `blocked --agent` genuinely untouched. One
  self-corrected mid-review slip (reviewer briefly edited the tracked file for a revert
  experiment instead of a scratch copy, caught it, `git checkout --` reverted, verified clean) —
  no impact on the verdict.

### Phase 19B — ERGO-33: never-blank title/subtitle chain ✅ (signed off 1st attempt; committed `1d61385`)
- **Files:** `src/Atv/Semantics/SemanticEngine.cs` (`ApplyRepoDefaults` now falls through to new
  `BuiltInDefaultTitle`/`BuiltInDefaultSubtitle`/`AnchorFolderName`/`SafeGetPathRoot` when every
  layer above is absent or resolves empty); `tests/Atv.LogicTests/Semantics/
  SemanticEngineRepoDefaultsTests.cs` (+9: 3 table rows, equal-names suppression, drive-root
  brand floor, empty-template fallthrough, extended 5-layer precedence terminus, child-card
  lock); `integrations/claude-code/plugins/atv-integration/translate.ps1` (new `Get-TitleArgs`,
  mirrors `Get-CwdArgs`, wired only into `UserPromptSubmit`); `tests/Atv.LogicTests/
  Integrations/ClaudeCodeTranslatorTests.cs` (+6) and `.../ClaudeCodePluginArtifactTests.cs`
  (one pre-existing test fixed — it asserted `--title` is never passed, now false under
  ERGO-33); `integrations/claude-code/README.md` + `docs/configuration.md` (defaults documented).
- **Result:** default title = `<anchor-folder>`, or `<anchor-folder> (<repo-folder>)` below a
  differently-named `.git` root (suppressed when equal), floored at `Branding.Name` for a bare
  drive root; default subtitle = branch or empty; any layer resolving to empty (including an
  explicit-but-empty `--title ""`, not just a template's empty expansion) falls through to the
  default — reviewer traced this broader reading against ERGO-33's unqualified "never renders a
  blank title again" and judged it the only consistent mechanism, not an overreach. Translator
  forwards `session_title` as `--title` on `UserPromptSubmit` only, when present.
- **Real platform finding (documented, not a blocker):** PowerShell 5.1 native-argv marshalling
  silently strips embedded literal double-quotes from `--title`'s value (non-ASCII and newlines
  survive intact) — reviewer independently reproduced it. Matches the pre-existing
  `docs/integration-api.md` §7 argv-quoting caveat; this is that caveat's first concrete
  instance now that `--title` carries arbitrary host text instead of translator-chosen
  constants. Structurally unfixable in scope (no free spare stdin slot on `UserPromptSubmit`);
  documented rather than hidden.
- **Review:** PASS (independent reviewer, re-ran build/tests itself: 767/767 LogicTests +
  28/28 + 32/32 + 20/20 isolated; NativeAOT clean 4.88 MB). Also independently built the stub-atv
  harness and drove `translate.ps1` as a real subprocess itself for AC10, rather than trusting
  the test file. Confirmed `MintChildCard` untouched (child titles stay out of the chain) and
  the unreachable-`_discoverRepo`-null path unweakened.
- **Non-blocking notes carried forward:** (a) no test locks the CLI-reachable "explicit
  `--title \"\"`" case as distinct from "template expands to empty" — same code path, just not
  independently exercised; (b) `docs/integration-api.md` §7's blanket "translator-chosen
  constants, never arbitrary text" claim is now stale for `--title` specifically — worth fixing
  whenever that doc is next touched (not required by Part B's file list, which only assigns §5
  to Part A).

**Phase 19: 19A + 19B DONE.** AC1–AC10 all automated and signed off. Only AC11 (19C, the live
dogfood covering both parts at once, operator-supervised) remains — the orchestrator hands back
here per the phase file's execution structure. Not run yet this session.

### Phase 19D (Part C) + 19E (Part D) + AC11 sign-off — found live, fixed, and confirmed within this same phase

AC11's live dogfood surfaced two further defects beyond Part A/B's original scope, both
diagnosed from real `claude --debug-file` captures and the durable `atv.log` (never from
assumption), fixed via the same TDD executor loop, and committed separately:

- **19D (Part C, `3e82800`):** a premature `ready` mid-fan-out — Claude Code's `Stop` fires as
  soon as it dispatches Task-tool subagent calls, not when they finish, so the unconditional
  `Stop -> ready` translator mapping claimed the parent `Completed` while children were still
  demonstrably running. Fixed with a new `refuseIfActiveChildren` structural refusal
  (`SemanticEngine.cs`, mirrors the existing `refuseIfChild` pattern) gating `Ready` on the
  addressed handle's `ActiveAgentLoci`. Full root-cause detail in
  `plan/phase-19-card-fidelity.md`'s Part C section.
- **19E (Part D, `15cb1cf`):** a cancelled subagent (Claude Code's `TaskStop` tool) never fires
  `SubagentStop` — confirmed by direct comparison in a live capture (a naturally-completing
  sibling got `SubagentStop` within 1ms of completion; the cancelled agent never got one,
  anywhere in the rest of the session). Since `translate.ps1` only ever called `agent-stopped`
  from `SubagentStop`, this orphaned the cancelled agent's card and, worse, made 19D's own
  `refuseIfActiveChildren` refuse `ready` on the parent *permanently* (the locus never left
  `ActiveAgentLoci`). Fixed translator-only: `TaskStop`'s `tool_input.task_id` is the same id
  format used everywhere as `agent_id`, so `PreToolUse:TaskStop` now maps directly to
  `agent-stopped <sid> --agent <task_id>`. No engine change needed. Full detail in the phase
  file's Part D section.
- **Diagnostic infrastructure added along the way (`d69d341`):** two new always-on log
  categories, `trace-in` (every CLI call as received, before any parsing/routing/engine logic)
  and `trace-out` (what was actually applied, every outcome kind) in `Dispatcher.cs`/
  `CompositionRoot.cs`. Used to definitively separate "the event never reached atv" from "atv's
  own logic mishandled it" — concretely, a controlled direct-CLI concurrency stress test (up to
  50 concurrent real `agent-started` calls, zero losses) first ruled out `WriteGate` mutex
  contention as the source of an earlier-observed multi-second card-appearance delay, before
  the `TaskStop` investigation found the real, distinct defect above.
- **First live attempt at verifying 19E showed no effect** — not a fix regression, but a
  process gap: the scratch dogfood repo's plugin copy (`translate.ps1`/`map.json` under its
  `.claude/skills/`) is a separate file tree from `atv.exe`, and refreshing the dev-loop
  package registration (`dotnet run`) does not touch it. Re-synced by hand; re-run confirmed
  the fix immediately ("worked like I expected").
- **AC11 sign-off (2026-07-15, operator decision):** on accumulated evidence across three live
  rounds rather than one final single clean end-to-end re-run — fan-out routing and non-blank
  titles (Part A/B, earlier rounds), mid-fanout `ready` refusal (19D), and cancellation cleanup
  (19E) have each been separately confirmed live at this point; a dedicated final combined run
  was judged to only re-confirm already-verified mechanics.

**Phase 19 is DONE.** All parts (A, B, C, D) implemented, tested, and live-confirmed.

### Post-phase-19 dogfood: two more bugs found live, fixed (2026-07-15)

Found in further `atv-dogfood-p19` scratch-repo testing after phase 19's sign-off, diagnosed
from the durable `atv.log` (`trace-in`/`trace-out`), fixed, and tested -- not tied to a new
plan phase, logged here per the same convention as 19D/19E.

- **Duplicate steps in the sequence.** `atv.log` showed the SAME `activity` label
  (`step=...`) trace-in twice, milliseconds apart, for one real tool call --
  `translate.ps1`'s `PreToolUse`/`PostToolUse` switch case both map to the identical
  `activity` claim (label derived from `tool_input`, never `tool_response`, so Pre and Post
  are byte-identical). Traced through `AdvanceModel.Advance`'s archive-then-set logic: Pre
  archives the PRIOR step and sets the new label; Post then archives that SAME label (since
  it's now "current") and re-sets it again -- every tool call left an adjacent duplicate in
  `completedSteps`, halving the useful history under the 10-entry FIFO cap. Operator
  direction: fix in the engine, not by dropping an event -- `AdvanceModel.Advance` now
  short-circuits as a true no-op when `newExecutingStep == currentExecutingStep` (neither
  archived nor re-set). `src/Atv/Operations/AdvanceModel.cs`; tests in
  `tests/Atv.LogicTests/Operations/AdvanceModelTests.cs`.
- **Ready→Idle (Paused) resetting the text instead of holding the last message.** Traced to
  `ClaimReady`: a bare `ready` (no `--summary`, e.g. `idle_prompt` after a `Stop` that DID
  carry one) re-affirming a card whose live content is already a `TextSummaryResult`
  reads back an empty `ExecutingStep` -- `AppTaskInfo` has no readback for that text at all
  (confirmed against `docs/windows-ui-shell-tasks/AppTaskInfo.md`: only `GetCompletedSteps()`/
  `GetExecutingStep()` exist, no summary-text getter) -- so it fell to the
  `NoStepsYetPlaceholder` ("Not started yet."), and `ReadyDecay.DemoteToIdle` had the
  identical fallback on the Ready→Paused decay itself. Not a coding mistake -- a real,
  confirmed platform write-only asymmetry -- but no design doc had actually ratified losing
  the text as acceptable. Fixed by having the engine remember its own copy: new
  `EngineMemory.LastSummary` (schema v3→v4, `src/Atv/Persistence/SidecarEntry.cs`), set by
  every `ready --summary` claim, consulted by `ClaimReady`'s bare-reaffirm path AND
  `ReadyDecay.DemoteToIdle` before falling back to the placeholder, and cleared everywhere a
  card leaves Ready (`ClaimWorking`/`ClaimBlocked`/`ClaimBroken`/`ProjectAfterLocusChange`/
  `DemoteToIdle` itself) -- mirrors the existing `ReadyDecay = null` "leaving Ready clears the
  clock" idiom. `src/Atv/Semantics/SemanticEngine.cs`, `src/Atv/Watchdog/ReadyDecay.cs`; tests
  in `SemanticEngineTransitionTests.cs` and `ReadyDecayPassTests.cs`.
- **Parent card frozen on stale text for the whole fan-out window.** Found live in a THIRD
  round of dogfooding (same day, after the two fixes above were already live-verified):
  operator wrote a file, then spawned two parallel subagents -- the parent card's step stayed
  on `"Writing sleep10.cmd"` for the entire ~40s fan-out, even though both new child cards
  updated fine. Confirmed byte-for-byte against `atv.log`: both `agent-started` trace-outs and
  both `agent-stopped` trace-outs left the parent's `step=` completely unchanged: `ClaimAgentStarted`
  deliberately never touched the parent's content/state (ERGO-31's transition table target-state
  column was blank by original design -- the theory being that the new child card appearing was
  signal enough on its own). Fixed: a real `agent-started` (has `--agent <id>`) now advances the
  parent's step via `Rendering.BuildAgentStartedLine` (`"Started {name-or-agentId}"`), routed
  through the same `ProjectAfterLocusChange` pipeline `activity` already uses -- lands Working
  from any non-Blocked prior state, but (operator decision) never clears a pending Blocked
  question, since agent-started isn't one of LIFE-24's block-clearing trigger events.
  `agent-stopped` deliberately did NOT get the same treatment (operator decision, 2026-07-16):
  stop events arrive in a slow trickle well after the fact, and the child card retiring is
  signal enough. `src/Atv/Semantics/{Rendering.cs,SemanticEngine.cs}`;
  `questions/usage-ergonomics/ERGO-31-v2-semantic-verb-contract.md` amended (transition table
  row + dated note); tests in `SemanticEngineTransitionTests.cs` (replaced the now-false
  `AgentStarted_FromAnyPriorState_NeverChangesState` with 5 tests covering the new content
  claim, the Blocked-preserving carve-out, and the archive-into-history behavior). Live-verified
  by replaying the exact write-then-fan-out sequence against the real dev binary: parent's step
  read `"Started general-purpose"` instead of the stale file-write text.
- **Verification:** full `Atv.LogicTests` suite green (790/790) after all three fixes. No
  `Atv.AdapterTests`/translator changes needed -- all three fixes are pure engine/watchdog logic.

**Two OPEN loose ends surfaced during this dogfood, NOT fixed, NOT yet triaged -- flagged here
so they aren't lost:**
1. **Detached watchdog holds a piped stdout handle open forever.** _(Filed 2026-07-20 as
   [[INFRA-34]] "The detached watchdog inherits the caller's stdio handles" -- OPEN. It recurred
   during phase-20 execution; the question doc carries the mechanism, the call sites, and the
   options. The note below is the original observation, kept as provenance.)_ Invoking the bare `atv`
   PATH alias directly from a piped/redirected terminal (bypassing the dev launch profile's
   `ATV_WATCHDOG_MODE=off`) spawns a real detached watchdog (`EnsureWatchdog`'s default
   "spawn" mode) that appears to inherit the caller's stdout/stderr handles -- the caller's
   pipe never sees EOF, hanging indefinitely, even after the caller's own visible work is
   done. Worked around during this session with `$env:ATV_WATCHDOG_MODE = "off"` before any
   direct CLI scripting. Pre-existing behavior, not caused by any of the three fixes above --
   only surfaced because this session drove `atv` directly via script for live verification
   instead of through a real host's hooks (which always run with a real terminal, not a
   redirected pipe, so this may never have been hit in normal use). Not reproduced from a
   normal interactive terminal; only from a tool-redirected one.
2. **A subagent's background-completion notification pollutes the parent's goal.** Observed in
   the SAME `atv.log` capture that surfaced item 3 above: a `<task-notification><task-id>...`
   XML fragment (Claude Code's own internal wake-up message for a backgrounded task) arrives at
   `UserPromptSubmit` and gets forwarded verbatim as `working --goal -` by `translate.ps1`,
   since the translator has no way to distinguish it from a real user prompt. Briefly shows raw
   plumbing text on the card mid-fan-out instead of anything human-readable. Not a regression
   from these fixes -- appears to be pre-existing `translate.ps1` behavior, visible in this
   session's captures going back to the very first `atv-dogfood-p19` log excerpt read at the
   start of this investigation.

### Phase 20 — Daily-driver retail identity + plugin command override ✅ (automated half signed off on 2nd review, `269a164`; live half closed 2026-07-21, `5646e3a`; neutral end-of-phase review COMPLETE, all 11 ACs)

- **Scope split, same pattern as phases 18/19:** subagents ran AC1–AC6 + AC11 only. AC7–AC10 are
  live and operator-supervised — DIST-12 §3 grounds that identity attaches only through packaged
  activation via the alias shim, so no build-log or stamped-file inspection substitutes for them.
- **Files:** modified `build/Atv.Package.targets` (dev alias branch → `$(AtvCommandName)-dev.exe`),
  `src/Atv/Package/AppxManifest.template.xml` (comments), both
  `integrations/{claude-code,copilot-cli}/plugins/atv-integration/translate.ps1` (the
  `atv-command.txt` tier), `tests/Atv.LogicTests/Integrations/*` (harness env-override +
  `CreateAtvDecoy`/`PrependToPath` + 8 precedence tests), `.gitignore`, `CLAUDE.md`,
  `docs/release.md`, both integration READMEs, `plan/README.md` (invariant #3 → four pools).
- **Result:** build 0/0; LogicTests **822/822** unfiltered (+9 net); NativeAOT clean, 5,128,704 B
  (~4.9 MB) arm64. All four build kinds verified stamped via `-getProperty:` without touching the
  live dev `obj/` manifest: dev → `atv-dev.exe`, release → `atv.exe`, verify → `atv-reltest.exe`,
  test template unchanged. `BuildKind.cs` untouched, its 27 tests unchanged.
- **Review:** **FAIL then PASS.** First pass confirmed AC1–AC6 by independent re-run (incl. reading
  `StubAtv/Program.cs` to prove the AC5 decoy assertion is non-vacuous — the decoy only writes its
  output file when actually invoked, so `File.Exists == false` is real evidence of zero calls), but
  failed AC11: the alias rename was applied to the sections the phase file named and left stale in
  three other sentences of the same two files, one of which (`docs/release.md` §3.5) was a live
  runbook step telling the operator to verify dev-interactive via `atv doctor` — which post-migration
  reads the **retail** install instead. Fixed inline by the orchestrator (phase-04 precedent; doc
  text only, no code, so the reviewer's build/test/AOT runs stood), then re-verified by the same
  reviewer → PASS.
- **The review sweep found a fourth spot, pre-existing:** `docs/maintenance/new-build-checklist.md`
  told the reader to drive bare `atv` with state-changing verbs (`atv start a --title …`) to render
  cards for platform verification. Harmless while dev owned `atv`; after this phase those commands
  land on the operator's retail daily install. Fixed in its own commit (`9fb7063`), NOT bundled into
  the phase commit (phase-10 convention). **Still stale there and not fixed:** those steps use the
  v1 lifecycle verbs, retired by ERGO-31 in phase 15 — a separate, larger doc job.
- **⚠️ Safety incident during execution.** The executor `git stash`-reverted both `translate.ps1`
  files to observe a red test; the reverted script's fallback resolved bare `atv` on PATH — the
  operator's live dev-interactive install — and really ran `atv working sess-1 --goal -`. It then
  hung ~8 minutes (that is [[INFRA-34]], filed this session) and was terminated by exact PID.
  Operator checked the taskbar afterward: **no stray `sess-1` card** — either the watchdog expired it
  or the kill landed before the write. **Rule reinforced for future phases: never stash/revert to re-run a red test on a box
  whose PATH carries a live install** — judge red-first discipline from test structure instead. The
  reviewer was explicitly forbidden from repeating it.

#### Live half (operator-run, 2026-07-20)

- **AC7 met.** Operator removed every registered `Codevoid.AgentTaskVoid*` package (migration steps
  1–2 satisfied outright: nothing held `atv`), then `dotnet run --project src/Atv -- -- doctor`
  re-registered the working copy. From a fresh shell, `atv-dev doctor` reported
  `Codevoid.AgentTaskVoid-bbbb1168_0.1.84.14087_arm64__016qghrny08mj` with the `(dev)` marker. No
  version skew: registered `0.1.84.14087` matches the build (INFRA-33's check).
- **Cert snag worth remembering.** The install first failed as untrusted. Cause: `artifacts/` is
  gitignored, so a clean at some point dropped `devcert.pfx` and a later `-t:AtvRelease` regenerated
  it — a NEW key with the SAME subject `CN=AppTaskInfoCli`. The 2026-07-10 trusted cert
  (`EE72026D…`) no longer matches the signing cert (`02228A9E…`). **Subject is not identity — compare
  thumbprints.** `docs/release.md` §3.1's "once per cert" is right; it is once per *generated* cert,
  and regeneration is invisible unless you check. Fixed by importing the current `devcert.cer` into
  `LocalMachine\TrustedPeople`.
- **AC8 met.** `Add-AppxPackage` on the plain arm64 release msix; from a fresh shell `atv doctor`
  reported PFN `Codevoid.AgentTaskVoid_0.1.84.14087_arm64__016qghrny08mj` — Name = brand exactly,
  **no marker** (correct Release classification) — with its own state tree
  (`…\Packages\Codevoid.AgentTaskVoid_016qghrny08mj\LocalState`), distinct from dev's
  `…-bbbb1168_016qghrny08mj`. Both aliases resolve, no contention. DIST-12's split is live.

#### The rendering collision found mid-AC9 — DIST-14 ("AppTaskProvider extension Id must be build-kind-aware too")

AC9's override smoke proved its ROUTING half immediately (session events went to `atv-dev`; the
retail `atv.log` showed only two read-only orchestrator calls). But no card rendered on the dev
pool, which opened a long live diagnosis. The answer was a real platform defect, unrelated to
phase 20's own changes but exposed by them.

**The defect.** The `com.microsoft.apptaskprovider` `uap3:AppExtension`'s `Id` was a static
literal shared by all four identity pools. The Shell resolves its provider registration by that Id
ALONE, so two registered atv packages collide and only one provides tasks. The loser still writes
cards to its own `SystemAppData\AppTasks\tasks.json` and `FindAll()` still returns them — only the
taskbar stays blank. Silent, and invisible until two pools drive cards at once, which had never
happened before: phase 20 is what makes coexistence the everyday arrangement.

**How it was established, in order:** dev alone renders → register a second package sharing the Id
and dev immediately stops → give the two DISTINCT Ids and both render concurrently (operator ran
this; two icons at once) → the execution alias is not a variable (`atv.exe`, `atv-dev.exe`,
`atv-test.exe` each render when that package is the only one registered).

**It contradicts the documented contract.** Microsoft documents `uap3:AppExtension`'s `Id` as a
within-app discriminator, `AppExtensionCatalog` exposes each extension as (package, id), and
`uap11:Id` explicitly scopes uniqueness "for all extensions in a package". Nothing documents
machine-global uniqueness. So this is the experimental `Windows.UI.Shell.Tasks` host diverging
from its own documented identity model — worth knowing so a future reader does not assume it was
our manifest authoring at fault. Written up in `docs/windows-ui-shell-tasks/README.md`.

**Fix (`a9fdfee`):** the Id stamps from the same `{IdentityName}` token as `Identity/@Name`, so
there is no second computation to drift. Reviewer independently re-derived all four values (22/31/
30/36 chars, each equal to its pool's Name) and verified the Microsoft citations verbatim against
the live docs.

**Follow-on (`954a259`), operator-directed:** nothing in the repo verified the stamped manifest at
all — phase 20's AC1 was checked by ad-hoc `-getProperty:` calls that were never captured. Rather
than a unit test, the operator asked for a build-time validation step: read back the manifest
ACTUALLY WRITTEN to disk and fail the build on any mismatch (`Identity/@Name`, the alias, the
extension Id, any surviving `{...}` placeholder, the 39-char limit). Reading the written file is
the whole point — a check that recomputes expected values the way the stamper does would be
circular ("who watches the watchers"). The reviewer provoked all five conditions against the
SHIPPED targets, each error carrying its `build\Atv.*.targets(<line>,<col>)` prefix as proof it
was the real `<Error>` firing and not a reproduction of the logic.

**Diagnostic lesson worth keeping.** The collision hypothesis was raised early and dropped after
the operator (correctly, per the docs) said provider extensions are not single-per-machine, and
after a "dev alone still does not render" result appeared to refute it. That result was misleading:
the package had already been poisoned by the retail install and did not recover when retail was
removed. The cheap decisive test — two packages, distinct Ids — was available the whole time and
would have settled it in one step. **A hypothesis contradicted by documentation is still worth one
empirical test when the test is cheap.**

**Non-blocking, carried forward:** the validation's regexes match raw text near the attribute
rather than anchoring to the element, so a future template attribute reorder could make the
extension-Id check read the wrong value. Verified correct against both templates today. Worth
anchoring to element boundaries whenever those targets are next touched.

#### AC9 rendering half + AC10 tail — closed live 2026-07-21 ✅

Build under test: **`0.1.91.10209`**, both arches, rebuilt after deleting the stale pre-fix
`artifacts/release/msix/` (verified pre-fix first: its manifest still carried the shared
`Id="Codevoid.AgentTaskVoid.AppTaskProvider"`). Signing cert `02228A9E…` was already trusted, so no
elevation was needed — the pfx survived because only the `msix/` subfolder was deleted.

- **Both pools registered from the fixed build.** Dev via `dotnet run -- -- doctor`
  (`Codevoid.AgentTaskVoid-bbbb1168`, `(dev)`), retail via operator `Add-AppxPackage`
  (`Codevoid.AgentTaskVoid`, unmarked). Both at `0.1.91.10209` — no version skew (INFRA-33).
  Stamped provider Ids confirmed **distinct**: `Codevoid.AgentTaskVoid-bbbb1168` vs
  `Codevoid.AgentTaskVoid`. AC7/AC8 re-confirmed on the fixed build.
- **AC9 rendering, platform level.** One card driven onto each pool; operator saw **two taskbar
  icons at once with correct labels**, each pool's `tasks.json` holding only its own card. This is
  the arrangement DIST-14 made impossible.
- **AC9 plugin-driven, both halves.** A real Claude Code session in `atv-e2e-sample` (junction →
  working tree → sees `atv-command.txt`) put an `atv-e2e-sample` card on the **dev** pool with
  retail's `tasks.json` empty and its log showing no session traffic. Then the override was
  repointed at a nonexistent path and another prompt sent: **no writes anywhere**. The dev sidecar's
  file mtime moved, but its `LastUpdate` stayed at the pre-break value — the watchdog re-sampling
  `ReadyDecay.LastSampledAt`, not a translator call. Live broken-target no-op confirmed.
- **AC10.** Migration steps 1–2 were satisfied last session (all packages cleared); 3–4 done above;
  step 5 closed by installing the daily plugin from a **clean clone** at
  `C:\Users\dhopt\Source\atv-plugin-clone` (`marketplace add <clone>\integrations\claude-code` +
  `plugin install --scope user`), then verifying the installed copy contains **no**
  `atv-command.txt` (contents: `map.json`, `translate.ps1`, `.claude-plugin/plugin.json`,
  `hooks/hooks.json`) and that its translator falls through to the bare-`atv` guard.

**Two findings worth carrying forward:**

1. **`marketplace add <github-repo>` cannot install this plugin.** It clones and requires
   `.claude-plugin/marketplace.json` at the **clone root**; ours is nested at
   `integrations/claude-code/`. And `marketplace add <local-path>` does **not** copy — it records the
   path as `installLocation` and reads it in place, so the source directory must be durable. Hence
   the separate clean clone. Adding a root-level `marketplace.json` would make
   `claude plugin marketplace add grork/AppTaskInfoCli` work directly, with Claude Code owning and
   updating the clone — the only arrangement where "is my daily plugin stale?" has a command that
   answers it. **Filed as distribution work for phase 23 (DIST-13), not taken here** — phase 20's
   step 5 explicitly sanctions a "path install off a clean checkout", so the clone route meets the
   criterion as written.
2. **New footgun: user-scope daily plugin + the `atv-e2e-sample` junction double-drive.** With the
   daily plugin enabled at user scope it loads in *every* session, including the dogfood repo, where
   the junctioned skills-dir copy also loads — one routing to `atv`, one to `atv-dev`.
   `claude plugin disable` is **not** sufficient: the name stays claimed
   ("the name `atv-integration` is already taken by an installed plugin"), and the skills-dir copy
   silently does not load at all. The dogfood run required a full
   `claude plugin uninstall atv-integration@agent-task-void`. **The daily plugin was left
   uninstalled** at operator instruction (the marketplace entry remains, so reinstall is one
   command). Both integration READMEs currently say "disable" — that advice is wrong and should be
   corrected to "uninstall"; natural home is phase 21's doc work.

**Also observed, unrelated:** `README.md`'s Manual usage examples still show the v1 lifecycle verbs
(`step`/`attention`/`state`/`done`), retired by ERGO-31 in phase 15 — the same staleness already
logged against `docs/maintenance/new-build-checklist.md`. Separate doc job.

### Phase 21 — Dev-run safety rules in the docs ✅ (doc-only; signed off 1st attempt; lean mode)
- **Files:** `CLAUDE.md` only (two new paragraphs in the "Dev loop: `dotnet run` / F5" section, between the `tests/Atv.LogicTests` line and the "One quirk" paragraph). No source/test/build/manifest changes.
- **Landed (INFRA-33 rules 3–4 + caveat):** Rule 3 — a real-taskbar check registers a throwaway identity you control (`-reltest`/alias `atv-reltest` per `docs/release.md` §3, or a one-off `winapp run`), never the operator's `atv`, and tears down by **exact Name** (`Remove-AppxPackage`, never a bare `*Codevoid.AgentTaskVoid*` wildcard — it sweeps the retail/dev/test pools); teardown-drops-cards cited to DIST-9, not restated. Rule 4 — `--unregister-on-exit` is a narrow programmatic-only path: no MSBuild property passes it through `dotnet run` (WinApp targets map only `--with-alias`/`--no-launch`/`--debug-output`/`--args` → direct `winapp run` only), and it drops cards on exit so it can't observe a persistent card or hold state. The `ERROR_PACKAGES_IN_USE`/live-watchdog deferral caveat = one brief DIST-6-cited sentence by the teardown guidance.
- **Item 4 (stale bare-alias prose) required zero edits:** the phase-21 spec assumed phase 20 left the validate-through-`dotnet run` paragraph untouched, but phase 20's build-half commit `269a164` had already rewritten it to the four-pool language (`atv-dev` = working copy, bare `atv` = retail). Executor and reviewer both independently confirmed the tree's end state is AC4-compliant; the paragraph was verified, not re-touched.
- **Review:** PASS (independent, 1st). All 6 ACs met — AC4 checked by the reviewer's own full-file grep (no sentence frames bare `atv` as a dev/worktree alias or validation target; the two remaining bare-`atv` mentions are the correct retail-install framing). Doc-style compliant (reviewer ran the skill's AI-tell grep net — no matches). AC6 doc-only diff: `git status` shows only `CLAUDE.md`; `dotnet build` 0/0; logic suite 821/821 (executor's run — a doc-only diff cannot move it, reviewer relied on that reasoning rather than re-running).
- **Still-open doc job (NOT phase 21 scope, carried from phase 20's log):** both integration READMEs say "disable" the daily plugin where the correct advice is "uninstall" (name stays claimed on disable + the skills-dir copy silently stops loading). Phase 20's note guessed phase 21 as its home, but phase 21's plan scope is `CLAUDE.md`-only ("No other docs"), so it was NOT taken here — remains an unhomed doc fix. Also still open from phase 20's log: `README.md` Manual-usage examples show retired v1 lifecycle verbs.

### Phase 22 — Create-anchored card defaults ✅ CODE HALF (AC1–AC11) signed off 1st attempt; ⏳ AC12 live pending (lean mode)
- **Files:** `src/Atv/Icons/IconTokens.cs` (curated emoji pool + combined default pool + SHA-256 pick helper), `src/Atv/Semantics/SemanticEngine.cs` (create-time placement moved in; repo-hash icon default; anchor deep-link default + floors; skip `UpdateDeepLink`/`Place` on unclaimed updates; child mint/redirect read parent LIVE values), `src/Atv/Cli/Dispatcher.cs` (removed per-verb `_icons.Place`; thread `deepLinkExplicit`), `src/Atv/Cli/Verbs/RunVerb.cs` (dropped pre-place; thread run's real token/flags), `src/Atv/Cli/CompositionRoot.cs` (app-data URI ALSO supplied to engine as floor; dispatcher keeps its copy), `src/Atv/Diagnostics/DoctorChecks.cs` + `src/Atv/Cli/Verbs/DoctorVerb.cs` (unconditional would-pick icon line), `docs/configuration.md` + `README.md` (both defaults w/ honesty notes). Tests: 8 new LogicTests files (Icons/Semantics/Cli/Run) + `CountingAppTaskStore.UpdateDeepLinkCallCount` + harness edits.
- **Result:** build 0/0; LogicTests **875/875** (+54 from 821); NativeAOT win-arm64 publish clean, **~4.93 MB** (within INFRA-2's 3–5 MB). Combined icon pool = 168 entries (30 Segoe + 138 single-scalar emoji, no dups). Pick recipe pinned: normalize (`GetFullPath` → trim trailing seps → `ToUpperInvariant`) → SHA-256 UTF-8 → first 8 bytes big-endian → mod pool count; NEVER `String.GetHashCode`; per-machine stability only.
- **Structural fix (Part 1):** the two defects (icon PNG stomped to Robot on every update; deep-link reverted to app-data on every `activity`) shared one cause — dispatcher resolved+placed on every verb, engine wrote deep-link unconditionally. Fixed by mirroring the `iconExplicit` pattern: `deepLinkExplicit` flag threaded through all upserting verbs (optional, defaults `true` so ~38 call sites keep semantics); plain (non-explicit) update skips `UpdateDeepLink` entirely and does NOT `Place`/force-recreate (compares against `live.IconUri`, step history preserved); child mint + redirected activity now read `parentLive.IconUri`/`DeepLink` (not passed-through args) so the shared-`IconUri` glomming invariant holds and children inherit the parent's anchor deep-link, not the floor; `run` adopted the engine placement path.
- **Review:** PASS (independent, 1st). Reviewer re-ran build/tests/AOT and **independently recomputed the SHA-256 pick in out-of-repo PowerShell** for the 3 pinned paths (agrees with production). Both deliberately-flipped pre-existing tests judged legitimate (coverage rerouted, not weakened): `IconTokenChanged...ForcesRemoveCreate` (`withIcons:true→false` — forced-recreate branch is unreachable with a real `IconService` since `Place` returns a content-independent per-handle path; now exercised via the null-icons degradation) and `NoDiscoverRepoWired...→ButIconStillPlaces` (create now always places). Invariants #2/#4 re-verified.
- **Orchestrator tidy at sign-off:** fixed the one cosmetic doc-style nit the reviewer flagged — a negation-contrast clause ("…is a one-look diagnosis, not a guessing game") in `docs/configuration.md`'s repo-config diagnosis section, reworded to plain phrasing. (Same [[avoid-negation-contrast-phrasing]] rule.)
- **✅ AC12 (LIVE, operator-supervised) — PASSED 2026-07-21.** Driven with the operator on the `atv-dev` dev-interactive pool (`Codevoid.AgentTaskVoid-bbbb1168_016qghrny08mj`, `api: IsSupported()->true`), INFRA-33-compliant (never bare `atv`); orchestrator read the platform store (`SystemAppData/AppTasks/tasks.json`), sidecars, and rendered PNGs directly between steps. All four checks confirmed:
  1. **Non-Robot pool icon** — `working ac12-a … --cwd <repo root>` (no `--icon`, no repo config) placed a Segoe tile (`Segoe:Error`/E783, `ac12-a.png` = the `segoe-tile-E783-64.png` render), operator eyeballed a non-Robot glyph. The new AC11 `doctor` line showed `default icon: Segoe:Error -- repo-hash default for 'C:\Users\dhopt\Source\AppTaskInfoCli'` and correctly diverged icon-key (repo root, `.git`) from deep-link-anchor (`src\Atv`, process cwd).
  2. **Click opens the anchor folder** — card's `deepLink` = `file:///C:/Users/dhopt/Source/AppTaskInfoCli`; operator clicked → File Explorer opened at the repo root.
  3. **clear+recreate → same icon** — operator watched the card vanish on `clear` and return; recreated `ac12-a.png` was **byte-identical** to the E783 cache tile (deterministic pick reproduced).
  4. **Two repos → different icons** — a 2nd card anchored at `C:\Users\dhopt` (its own distinct deep-link, empty subtitle) rendered the **same** E783 glyph. Investigated on disk (not assumed): both handle PNGs byte-identical, no new cache tile. **Independently recomputed the SHA-256 pick** (out-of-band PowerShell replicating the exact recipe, pool N=168 = 30 Segoe + 138 emoji): `…\APPTASKINFOCLI` → index **13** AND `C:\USERS\DHOPT` → index **13** — a **genuine collision**, not a bug (8 sample paths spread across indices 9/13/36/45/115/156/164). This is exactly ERGO-34's documented ~1/168 degradation; the cards stay distinguishable by title/subtitle. Then a 3rd card anchored at `C:\Users\dhopt\Source` (index 45 → emoji `1F618` 😘) rendered a **visibly distinct** tile (operator: "lovely emoji") — proving the pick varies and the emoji render path works live on this 26100 box. Cleaned up via `clear` (operator eyeballed all three vanish; store/handles/sidecar verified empty on disk).
- **⚠️ Finding surfaced during AC12 (NOT an AC12 failure — filed separately for triage):** the Segoe-glyph tiles render the glyph **off-center**, riding high on the accent plate (operator caught it; orchestrator confirmed by opening `segoe-tile-E783-64.png` — the "!" ink sits high, more padding below). Root cause: `GlyphRenderer.Render` (`src/Atv.IconRendering/GlyphRenderer.cs:87`) vertically centers with DWrite `SetParagraphAlignment(PARAGRAPH_ALIGNMENT_CENTER)`, which centers the **line box** (ascent+descent), not the glyph **ink box**; Segoe Fluent Icons glyphs reserve descent space their ink doesn't fill, so they sit high. Emoji unaffected (`onTile:false`, full-bleed). Phase-16 tile-compositor defect, pre-existing, made more visible by phase 22's pool. Candidate fix: measure ink via `IDWriteTextLayout` overhang/metrics and offset. See RESUME-HERE finding; awaiting operator triage (fix before/within phase 23, since dogfooders would see it, vs. file as its own numbered question).

### Phase 25 — Glyph ink-box centering ✅ CODE HALF (AC1–AC4) signed off 1st attempt; ⏳ AC5 live pending (lean mode)
- **Files:** `src/Atv.IconRendering/GlyphRenderer.cs` (the fix) + new `tests/Atv.IconRendering.Tests/GlyphInkCenteringTests.cs`. `TileCompositor.cs` untouched.
- **Fix:** the on-tile Segoe path centered by the DWrite line box (`SetParagraphAlignment(CENTER)`), so glyphs rode high. Executor chose the **alpha-scan recenter** mechanism (phase file option 2, over IDWriteTextLayout/OVERHANG_METRICS): draw once to a transparent scratch canvas, scan the non-transparent ink bbox, redraw for real translated by `(tileCenter − inkCenter)`. No new interop; the same ink-bbox predicate the test verifies with is what production computes the correction with. Emoji path (`onTile:false`) untouched.
- **Result:** build 0/0; `Atv.IconRendering.Tests` **38/38**; `Atv.LogicTests` **875/875**; NativeAOT win-arm64 publish clean, **4.94 MB**.
- **Review:** PASS (independent, 1st). Reviewer **directly confirmed red-before/green-after** by reverting only `GlyphRenderer.cs` to HEAD and re-running: `StatusWarning` (EA84) horizontal center off by **11px** pre-fix (32 expected, 43 actual), **≤0.5px** post-fix (own probe: Error −0.5/−0.5, Robot 0/0, StatusWarning 0/0.5, Link 0/0 — genuinely centered, not squeaking under ±2px). Verified the ink predicate against real colors: `TileCompositor.AccentColor`=`#0078D4` (R=0), `GlyphColor`=white (R=255), so the test's `R≥128` classifier is max-contrast-correct. AC2 emoji byte-pin confirmed a genuine pre-fix reference (passed against reverted HEAD too). Only the two files changed; no pre-existing test assertions altered. Executor's glyph substitution (StatusWarning/Link, chosen via a full 30-glyph sweep for a real measured violation rather than tolerance-gaming the phase file's illustrative examples) judged legitimate.
- **✅ AC5 (operator-supervised) — CLOSED 2026-07-21 on the operator's own eyeball.** The operator asked for a gallery instead of a single live taskbar card. Orchestrator rendered a contact sheet of ALL 30 curated Segoe glyphs + an emoji sampler at 128px via the phase-25 `GlyphRenderer` (a throwaway `dotnet run` console referencing `Atv.IconRendering` directly — no atv, no registration), each tile framed so centering is visible. **These are the exact `PngEncoder` bytes atv points a card's `IconUri` at (proven equivalent in AC12, where the operator eyeballed the pre-fix off-center version live), so the render === the taskbar appearance.** Operator opened `C:\Users\dhopt\atv-glyph-gallery.png` and confirmed the glyphs (incl. `Error`/`StatusWarning`, the worst pre-fix offenders) are now centered ("They look great"); the temp PNG was then deleted at the operator's request.
- **⚠️ Process miss caught + corrected (recorded so it isn't repeated):** the orchestrator first tried to close AC5 on a gallery it had only `Read` into its OWN context — the operator had not seen it. The operator rejected that ("**I** haven't seen it"). Fixed by copying the PNG to an operator-openable path and waiting for the operator's actual view. **Rule reinforced: an image the orchestrator renders/Reads is NOT operator evidence — a human-eyeball AC requires the human to open and look at it themselves.** See [[host-integration-needs-live-dogfood]].

**Minor observed inaccuracy (non-blocking, later tidy — do NOT bundle into an unrelated commit):** `doctor`'s app-data line says the platform's `tasks.json` lives "under [LocalState]", but the real AppTaskInfo store is at `…\Packages\<PFN>\SystemAppData\AppTasks\tasks.json` — a *sibling* of `LocalState`, not under it (observed directly during AC12). The durable log + sidecar index DO live under LocalState; only the tasks.json clause is off. Candidate one-line `DoctorChecks`/`DoctorVerb` wording fix.

_(Further per-phase notes appended below as phases execute.)_
