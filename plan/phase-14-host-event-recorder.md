# Phase 14: Host-event behavior recorder + Claude Code findings

**Depends on:** nothing in the atv build — the recorder is atv-independent by design
(INFRA-24). Sequenced after the shipped phase-13 Claude Code leg, whose live dogfood
supplied the lessons this generalizes (sync-at-teardown; doc-derived mappings keep being
wrong live).
**Produces:** the live captures later work consumes — the deferred phase-13 Copilot/Codex
integration legs (no host mapping is verified without live capture, LIFE-24 rule 7) and
the LIFE-24 v2 translator line.

## Goal

Build the standalone host-event diagnostics recorder ratified in INFRA-23: a dumb,
verbatim append core that any host's hooks can spawn to log every event firing — full raw
payload, byte-faithful — into a per-session JSONL file. Wire it to Claude Code, capture
real sessions, and distil confirmed findings (including explicit "did not fire" results)
into `docs/host-events/` as the token-cheap durable reference future sessions read instead
of re-running experiments (the `docs/windows-ui-shell-tasks/` pattern). The core is
unfalsifiable in isolation, so this phase deliberately couples it with the Claude Code
integration that proves it (INFRA-30).

## Scope — two deliverables, in strict dependency order

This phase builds **Part A, then Part B**:

- **Part A — the shared recorder core** (the exe). Host-agnostic: it understands no host
  and no payload. This is the dependency — built, tested, and smoke-checked standalone
  behind **Gate A** (the *ready-to-integrate* bar) before any Part B file is created.
- **Part B — the Claude Code leg.** The conduit config, driver harness, cue script, live
  capture, and findings that consume the core. Begins only once Gate A is green — and it is
  what *proves* the core (see below).

**The core is unfalsifiable in isolation** (INFRA-30, "proof in the pudding"): Gate A's unit
tests and AOT smoke show the exe is byte-correct and buildable, not that it works as a
host-event recorder. Only a real host spawning it through real hooks and producing correct
captures proves that. So Part B is not merely "host #1" — it is the core's validator, and
**phase-14 is not complete, and the core is not proven, until the Claude Code live capture
works.**

Copilot CLI, Codex, and pi get **no artifacts this phase** — no matrix, conduit, driver, or
capture. Each is a future per-host phase, built when its host is testable (the rollout model
is INFRA-30; whether and how those legs get built is INFRA-31, OPEN — not yet processed).
The design each will instantiate is already pinned in INFRA-26/27/28, and the verbatim core
admits them with no change (INFRA-29's stable/churn line), so nothing here is provisional on
their behalf.

**Invariant note:** standing invariant #2 (brand parameterization) deliberately INVERTS
for this phase — the recorder consumes no brand constant, no `Atv.*` reference, no package
identity (INFRA-24; the absence of identity machinery is part of the separation).
Invariants #3–#6 concern atv's own runtime and don't apply. #1 (TDD) applies in full to the
core.

---

## Part A — the recorder core (build and gate first)

Host-agnostic and atv-independent. Everything here is done and green before Part B starts.

### Project & placement (INFRA-24)

- Compiled C# console tool at `tools/host-event-recorder/`, project `HostEventRecorder`,
  exe `host-event-recorder`. It lives under `tools/` — a sibling of `src/`, alongside
  `Atv.TestIdentityTool` — and NOT under `src/`: `src/` is the atv **product** tree, and
  INFRA-24 requires this tool structurally separate from atv (no reference in either
  direction). A slnx member so `dotnet build` builds it and it cannot rot; it references no
  atv project and consumes no `$(AtvBrandName)`.
- Plain `net10.0` TFM — named mutexes need no Windows TFM, and the absence of
  `-windows10.0.26100.0` reinforces the separation criterion 2 checks. No manifest, no
  `winapp`, no identity: a vanilla console exe.
- NativeAOT single-file only on an explicit `dotnet publish -p:PublishAot=true`; the
  solution build and dev loop stay a normal managed build. AOT-safe by construction:
  source-gen JSON (`JsonSerializerContext`) for the fixed 6-field envelope, no
  reflection-based serialization.

### Invocation & envelope (INFRA-24, INFRA-25)

- Invoked once per event: host tag + event name ride argv (static per hook line, e.g.
  `host-event-recorder --host claude-code --event PostToolUse`); the raw payload arrives on
  stdin, read as raw bytes and decoded UTF-8. Exact byte control is why this is compiled C#
  (kills the PS-5.1 mojibake class LIFE-24 called out).
- One JSONL file per capture session, session id in the filename; one object per line:
  `{ts, host, event, pid, session, payload}`. `ts` = ISO-8601 UTC, sub-millisecond, wall
  clock at write time; `pid` = the recorder invocation's own; `payload` = the host's bytes
  as a **JSON-escaped string**, never re-parsed or re-emitted (re-serialization silently
  normalizes key order / whitespace / number formatting; byte-faithful and losslessly
  reversible instead). Malformed and non-JSON payloads are captured just the same.

### Guarded append (INFRA-25)

- Appends serialized by a named `System.Threading.Mutex` scoped to the log file, held only
  for the one small append — records structurally cannot tear under parallel hook fan-out.
  File line order is the authoritative sequence; there is no seq field.
- The mutex name derives from the log file's NORMALIZED full path (canonicalized,
  case-folded, hashed — named-mutex names can't contain path separators, and NTFS paths are
  case-insensitive): two processes handed the same file via different path spellings must
  resolve the same mutex.

### Capture location & session id (INFRA-25)

- The driver ALWAYS sets the path env var (a recorder-named variable, not `ATV_*`)
  explicitly, pointing at the canonical gitignored `tools/host-event-recorder/captures/`.
  The recorder's own fallback (no arg, no env) is **exe-adjacent**
  (`AppContext.BaseDirectory`-relative `captures/`), never cwd-relative — hooks spawn the
  recorder with an arbitrary working directory, so a cwd-relative default would drop raw
  payloads into whatever un-gitignored directory a stray invocation runs in. Raw payloads
  carry prompts, cwd paths, tool inputs — possibly secrets — so they are never committed;
  only distilled findings reach `docs/host-events/`.
- Session id: the driver mints it and exports it into the host process's environment so
  every hook-spawned recorder inherits it; an argv flag overrides; absent both, a dated
  ad-hoc fallback id so manual captures still land deterministically.

### Pin the plumbing names first (INFRA-25)

The session env var, the path-override variable, the JSONL filename format, and the
default-path resolution basis (exe-adjacent, never cwd) are cross-artifact contracts
(driver ↔ recorder ↔ tests ↔ every future `hosts/*` tree). Pin them in ONE place — the
recorder's constants file, mirrored in `docs/host-events/README.md` — as the **first act of
execution**, before the append core is fleshed out.

### Gate A — the core is ready to integrate

Gate A is the *ready-to-integrate* bar, not the core's validation: it proves the exe is
byte-correct, buildable, and separate, so it can be wired to a real host. The core is only
*proven* by Part B's live Claude Code capture (INFRA-30). Part B does not begin until all of
these hold:

- **TDD suite green:** byte-faithful round-trip (non-ASCII UTF-8, embedded quotes/newlines,
  malformed and non-JSON payloads), the six envelope fields, one file per session id,
  no-tear concurrent appends (parallel writers; every line parses; no interleaving),
  mutex-name agreement (same file via different path spellings resolves the same mutex), and
  the default-path fallback (no arg/env → exe-adjacent regardless of cwd).
- **Solution `dotnet build` is 0 warn / 0 err** with the new projects.
- **AOT publish is clean**, and the single exe appends a correct envelope when driven
  standalone (argv + stdin smoke).
- **Separation verified both ways:** `HostEventRecorder` references no atv
  project/namespace/brand constant, and nothing under `src/` references the recorder.

---

## Part B — the Claude Code leg (only after Gate A)

Consumes the validated core. Claude Code is the one host installed and testable here.

### Safe/skip matrix, before any capture (INFRA-26)

- A safe/skip matrix in `docs/host-events/claude-code.md`, first derived from Claude Code's
  own hook docs, then confirmed by the capture run. One classification axis: does camping
  this event suppress or replace a default host action? Passive log-and-exit-0 is safe even
  on decision-capable events (declining to decide changes nothing); the **replacement
  class** (`WorktreeCreate`-style, where the hook must do the replaced work) is SKIPPED in
  v1 with the reason recorded (camp-with-care collapses into skip).
- An unsafe camp found mid-session: pull/downgrade that event's row and record the observed
  reason. Runs are supervised and the config is version-controlled; no heavier process.
- The matrix exists BEFORE any capture runs — every candidate event classified, skip reasons
  recorded.

### Observer posture (INFRA-27)

- Async everywhere except the teardown-adjacent event. Async adds ~0 to the host's event
  loop; a ~100 ms synchronous spawn per in-turn event is real perturbation. But an async
  teardown hook loses the process-exit race (the exact phase-13 `SessionEnd` bug), so Claude
  Code's `SessionEnd` — and only it — is synchronous under a tight timeout (the shipped
  integration's `timeout: 10` precedent). Because the replacement class is skipped
  (INFRA-26), the recorder never does work inside a hook; the teardown race is the only
  reason blocking is ever accepted.

### Conduit staging & hook environment (INFRA-28)

- The checked-in conduit config is a **template** with a placeholder for the recorder exe's
  absolute path (the exe has no alias, installer, or PATH story). The driver harness begins
  with a stage step: build/publish the recorder, create a throwaway scratch capture project
  directory, substitute the real exe path into the config, mint + export the session id. The
  substituted config exists only in the scratch dir, never in the repo.
- The conduit is **project-scoped in the scratch dir**, never user-wide (user-wide would
  camp on every real session the operator runs). The operator's installed user-wide atv
  hooks (the shipped phase-13 integration) are temporarily disabled during capture runs for
  a clean observer baseline; the disable and re-enable are the cue script's FIRST and LAST
  steps, never a remembered manual edit (forgotten-before contaminates the baseline,
  forgotten-after leaves the operator's real sessions cardless). The findings doc records
  each session's hook environment either way.

### Driver, scenario & the added beat (INFRA-28)

- A thin driver harness over Claude Code's own scripting surface (`-p`/print or SDK). Driver
  tech is unconstrained — dev-box diagnostics, not a shipped artifact, so DIST-4's
  in-box-runtime posture does not apply.
- The host-agnostic beat corpus (LIFE-24, ratified): fresh start → first prompt → tool calls
  → parallel subagent fan-out (≥2 concurrent) → a real permission prompt → a user interrupt
  → an idle wait past the notification threshold → clean exit. Never fake the signal under
  test: a tool the session isn't pre-authorized for (real permission prompt), literally
  waiting out the idle threshold, a prompt explicitly requesting ≥2 parallel subagents.
- Claude Code ADDS one beat: a subagent task requiring a not-pre-authorized tool, so a
  **subagent-originated** permission prompt fires (LIFE-24 empirical item 2's
  does-`Notification`-carry-`agent_id` sub-question; the corpus's permission beat is
  main-thread and its fan-out beat raises no prompt).
- **The capture may span multiple session JSONLs.** `-p` is one-shot (the process exits
  after the response — no idle window for `idle_prompt`, no interactive permission dialog,
  "interrupt" degenerates to a kill), and whether SDK-managed permissions (`canUseTool`)
  even fire the `Notification` hook is itself unverified — so idle-wait, interrupt, AND
  live-permission all land in a supervised session whose "driver" is a cue script plus
  pre-staged prompt sequence (a human at the terminal; no PTY automation in v1). Scripted
  beats run through the scripting surface where it genuinely reaches them. Findings aggregate
  across the host's sessions, each citing its session id(s).
- The driver only exercises events the matrix has cleared; it never drives a skip-classified
  event.

### Findings (INFRA-29)

- Confirmed findings distilled into `docs/host-events/claude-code.md`, each carrying a
  **host version + capture date** stamp (the `integrations/claude-code/README.md` "Verified
  against:" convention). Every expected-but-absent event is recorded as an explicit
  did-not-fire finding citing its session id — including the teardown event captured,
  proving the sync-at-teardown posture against the phase-13 async-loss bug.
- First consumer: LIFE-24's open empirical items 2 (which events fire inside subagents;
  `agent_id` uniqueness across parallel spawns; whether `Notification: permission_prompt`
  carries `agent_id`) and 3 (does `idle_prompt` fire after a user interrupt, on what
  timing/repetition) — answered there, or recorded as attempted with what was observed if
  genuinely unresolvable.
- Re-capture is organic (INFRA-29): no CI, no cadence — a future session about to trust a
  mapping checks the stamp against the installed host version and re-runs if stale.

---

## Files affected

```
Part A — core
  tools/host-event-recorder/HostEventRecorder.csproj  # new project; slnx member, no Atv refs
  tools/host-event-recorder/*.cs                       # constants (pinned first), argv/stdin, envelope, mutex, append
  tests/HostEventRecorder.Tests/…                      # core unit suite (no Atv refs; Atv.LogicTests MTP/MSTest convention)
  AppTaskInfoCli.slnx                                  # + the two new projects
  .gitignore                                           # + tools/host-event-recorder/captures/
  docs/host-events/README.md                           # conventions + the pinned plumbing names (mirror)

Part B — Claude Code leg
  tools/host-event-recorder/hosts/claude-code/…        # conduit config TEMPLATE + stage/driver harness + cue script
  docs/host-events/claude-code.md                      # safe/skip matrix + stamped findings
```

## Acceptance criteria

**Gate A (Part A — all must hold before Part B begins):**

1. TDD core: byte-faithful capture (non-ASCII UTF-8, embedded quotes/newlines, malformed
   and non-JSON payloads round-trip exactly through the escaped `payload` string and back),
   the six envelope fields, one file per session id, no-tear concurrent appends (parallel
   writers; every line parses; no interleaving), mutex-name agreement (same file via
   different path spellings — case, relative vs absolute — resolves the same mutex), and the
   default-path fallback (no arg, no env → the log lands exe-adjacent regardless of the
   process's working directory).
2. Separation is structural and verifiable both ways: `HostEventRecorder` references no atv
   project/namespace/brand constant, and nothing under `src/` references the recorder.
3. Solution `dotnet build` stays 0 warn / 0 err with the new projects; an explicit NativeAOT
   publish of the recorder is clean, and the single exe appends a correct envelope when
   driven standalone (argv + stdin smoke).

**Part B (Claude Code leg — after Gate A; this is the core's proof):**

4. The Claude Code matrix exists in `docs/host-events/claude-code.md` BEFORE any capture
   runs, every candidate event classified on the one axis, skip reasons recorded.
5. Real Claude Code captures against the installed version: **one or more session JSONLs
   collectively cover the beat corpus plus the added subagent-permission beat** — scripted
   beats through the scripting surface where it genuinely reaches them, the
   interactive-constrained beats in the supervised cue-script session. The teardown event is
   captured (proving the sync-at-teardown posture against the phase-13 async-loss bug); every
   expected-but-absent event is recorded as an explicit did-not-fire finding citing its
   session id.
6. Findings distilled into `docs/host-events/claude-code.md` with host-version +
   capture-date stamps; LIFE-24 empirical items 2 and 3 answered there (or recorded as
   attempted, with what was observed, if genuinely unresolvable).

**Whole phase:**

7. No raw capture is committed: the captures directory is gitignored and the phase's diff
   contains only code, configs, and docs — no session JSONL.
8. No speculative host legs: the only `hosts/` tree in the diff is `claude-code`.
   `docs/host-events/README.md` records the conventions a future host leg follows
   (matrix-before-capture, version+date stamps, the template+stage pattern, the pinned
   plumbing names, the pi verbatim caveat) so those passes — tracked in INFRA-31 — start from
   the doc, not archaeology.

## Execution notes (gotchas, not decisions)

- AOT publish on this box needs `vswhere.exe` on PATH regardless of RID (documented in
  CLAUDE.md) — Gate A's AOT smoke inherits that.
- The repo-wide `Directory.Build.props` inheritance (warnings-as-errors, NBGV versioning,
  inert AOT levers, `InvariantGlobalization`) is benign for the recorder, as INFRA-24
  anticipated — nothing in it forces identity, CsWinRT, or a Windows TFM.
- `tests/HostEventRecorder.Tests/` follows the `Atv.LogicTests` MTP/MSTest convention
  (`EnableMSTestRunner`, exe output) while referencing no Atv project.

## Out of scope

- The Copilot CLI, Codex, and pi recorder legs (matrix, conduit, driver, capture) — each a
  future per-host phase, built when its host is actually testable (rollout model INFRA-30;
  tracked as INFRA-31, OPEN). Only Claude Code is testable here. The design those legs will
  instantiate is
  already pinned (INFRA-26/27/28); the pi conduit specifically is pinned in INFRA-27 (the
  in-process TS extension spawns the recorder per event, single serialization at delivery is
  the capture) — no pi conduit code is written this phase.
- Everything on the LIFE-24 / v2 line: the semantic verb contract (ERGO-31), the per-host
  translator tables, and LIFE-25 ("Should host hooks invoke the `atv` exe directly instead
  of via a shell?") — decided but planned with that line, not here. This phase produces the
  captures those consume.
- The deferred phase-13 Copilot CLI / Codex integration legs themselves — this phase readies
  their verification tool; it does not ship their artifacts.
- Any query/tail/analyze surface in the recorder (capture-only v1, INFRA-24; revisit only if
  the jq/PowerShell friction proves real).
- A checked-in fixture-regression corpus for translators (explicitly declined 2026-07-11 —
  the LIFE-24 relationship stays documentation-only, INFRA-29).
- LIFE-24 empirical item 1 (`NeedsAttention`'s taskbar group-badge priority) — an
  atv/taskbar experiment, not a hook capture; it belongs to the v2 line.
