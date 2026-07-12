# Phase 14: Host-event behavior recorder + findings corpus

**Depends on:** nothing in the atv build — the recorder is deliberately
atv-independent (INFRA-24). Sequenced after the shipped phase-13 Claude Code leg,
whose live dogfood supplied the lessons (sync-at-teardown, doc-derived mappings
keep being wrong live) this phase generalizes into tooling.
**Unblocks:** the deferred phase-13 Copilot CLI / Codex integration legs — no host
mapping counts as verified without live capture (LIFE-24 mapping rule 7), and this
tool, extended with a per-host recorder leg when each host becomes testable, is
how those legs' mappings get verified — and the LIFE-24 v2 line (per-host
translator tables, open empirical items).

## Goal

Build the standalone host-event diagnostics recorder ratified in INFRA-23 ("The
host-event behavior recorder"): per-host hook configurations camp on every
safe-to-camp event and pipe each firing — full raw payload, verbatim — through one
shared append core into a per-session JSONL log. Captures are analyzed after the
fact; confirmed findings, including explicit "did not fire" results, persist to
`docs/host-events/` as the token-cheap durable reference future sessions read
instead of re-running experiments (the `docs/windows-ui-shell-tasks/` pattern).

**Build scope: the shared core plus the Claude Code leg ONLY** (operator,
2026-07-12) — Claude Code is the one host installed and testable here. The
per-host answers INFRA-26..28 record for Copilot/Codex/pi stay recorded (in the
question files and the decision sections below) for the future per-host passes
that build those legs when each host is testable; the verbatim core needs no
change to admit them (INFRA-29's stable/churn line).

**Invariant note:** standing invariant #2 deliberately INVERTS for this phase —
the recorder consumes NO brand constant, no `Atv.*` reference, no package identity
(INFRA-24; the absence of identity machinery is part of the separation).
Invariants #3–#6 concern atv's own runtime and don't apply. #1 (TDD) applies in
full to the append core.

## Decisions implemented

### The tool (INFRA-24, "Recorder tool architecture & repo placement")

- Compiled C# console tool at `tools/host-event-recorder/`, project name
  `HostEventRecorder` (a plain name, deliberately not `Atv.*`), exe
  `host-event-recorder`. A member of `AppTaskInfoCli.slnx` so it builds with
  `dotnet build` and cannot rot (the `tools/Atv.TestIdentityTool` placement
  precedent) — but it references no atv project and consumes no
  `$(AtvBrandName)`. No manifest, no `winapp`, no identity: a vanilla console exe.
- NativeAOT single-file on explicit `dotnet publish -p:PublishAot=true` only; the
  solution build and dev loop stay a normal managed build. AOT-safe by
  construction: source-gen JSON (`JsonSerializerContext`) for the fixed 6-field
  envelope. TFM is plain `net10.0` — named mutexes need no Windows TFM, and the
  absence of `-windows10.0.26100.0` reinforces the structural separation
  criterion 2 checks.
- Invoked once per event: host tag + event name ride argv (static per hook line,
  e.g. `host-event-recorder --host claude-code --event PostToolUse`); the raw
  payload arrives on stdin, read as raw bytes and decoded UTF-8 — exact byte
  control is why this is compiled C# (kills the PS-5.1 mojibake class LIFE-24
  called out).
- Capture-only v1: no query/tail/analyze surface in the recorder; analysis stays
  ad hoc (jq / PowerShell over the JSONL).

### The log (INFRA-25, "Host-event log envelope schema")

- One JSONL file per capture session, session id in the filename; one object per
  line: `{ts, host, event, pid, session, payload}`. `ts` = ISO-8601 UTC,
  sub-millisecond, wall clock at write time; `pid` = the recorder invocation's
  own; `payload` = the host's bytes as a **JSON-escaped string**, never re-parsed
  or re-emitted (re-serialization silently normalizes key order / whitespace /
  number formatting — byte-faithful and losslessly reversible instead; malformed
  and non-JSON payloads are captured just the same).
- Appends serialized by a named `System.Threading.Mutex` scoped to the log file
  (independent of any atv package-identity mutex), held only for the one small
  append — records structurally cannot tear under parallel hook fan-out. File
  line order is the authoritative sequence; there is no seq field. The mutex
  name derives from the log file's NORMALIZED full path (canonicalized,
  case-folded, hashed — named-mutex names can't contain path separators, and
  NTFS paths are case-insensitive): two processes handed the same file via
  different path spellings must resolve the same mutex.
- Capture location: the driver ALWAYS sets the path env var (a recorder-named
  variable, not `ATV_*`) explicitly, pointing at the canonical gitignored
  directory `tools/host-event-recorder/captures/` (add to `.gitignore`). The
  recorder's own fallback (no arg, no env) is **exe-adjacent**
  (`AppContext.BaseDirectory`-relative `captures/`), NEVER cwd-relative — hooks
  spawn the recorder with an arbitrary working directory, so a cwd-relative
  default would drop raw payloads into whatever un-gitignored directory a
  stray invocation happens to run in. Raw payloads carry prompts, cwd paths,
  tool inputs — possibly secrets — so they are never committed; only distilled
  findings reach `docs/host-events/`.
- Session-id plumbing (owned by this phase): the driver (INFRA-28) mints the id
  and exports it into the host process's environment, so every hook-spawned
  recorder invocation inherits it; an argv flag overrides; absent both, a dated
  ad-hoc fallback id so manual captures still land deterministically.
- The exact plumbing names — the session env var, the path-override variable,
  the JSONL filename format, the default-path resolution basis (exe-adjacent,
  never cwd) — are cross-artifact contracts (driver ↔ recorder ↔
  tests ↔ every `hosts/*` tree, present and future). Pin them in ONE place (the recorder's
  constants file, mirrored in `docs/host-events/README.md`) as the first act of
  execution, before any per-host tree is written.

### Coverage & posture (INFRA-26, "Safe per-event hook coverage pass"; INFRA-27, "Observer posture vs teardown blocking budget")

- Before any capture against a host: a safe/skip matrix in
  `docs/host-events/<host>.md`, first derived from that host's own hook docs,
  then confirmed by the capture run. One classification axis: does camping this
  event suppress or replace a default host action? Passive log-and-exit-0 is
  safe even on decision-capable events (declining to decide changes nothing);
  the **replacement class** (`WorktreeCreate`-style, where the hook must do the
  replaced work) is SKIPPED in v1 with the reason recorded — camp-with-care
  collapses into skip.
- An unsafe camp found mid-session: pull/downgrade that event's row and record
  the observed reason in the findings doc. Runs are supervised and the config is
  version-controlled; no heavier process.
- Posture: async everywhere except teardown-adjacent events. Async adds ~0 to
  the host's event loop; a ~100 ms synchronous spawn per in-turn event is real
  perturbation — the opposite of a faithful observer. But async teardown hooks
  lose the process-exit race (the exact phase-13 `SessionEnd` bug), so those —
  and only those — are synchronous under a tight timeout (the shipped
  integration's `timeout: 10` precedent). Per host: Claude Code and Copilot
  async with their session-end equivalent sync; Codex all-async (it has no
  session-end hook to protect); pi's conduit in-process with no teardown race
  (`session_shutdown` is delivered in-process) — recorder invocation pinned in
  the next bullet.
- **pi conduit, pinned** (resolves an ambiguity between INFRA-27's "no spawn
  cost" phrasing and INFRA-24/25's one-shared-write-path rule; INFRA-27 carries
  a matching dated amendment): the TS extension does NOT append JSONL itself —
  that would reimplement the guarded write path in a runtime with no
  named-mutex primitive. It spawns the recorder exe per event like every other
  host: fire-and-forget for in-turn events, awaiting exit only on
  `session_shutdown` (the same teardown-only blocking rule). And for pi,
  "verbatim" is definitionally different: events arrive as in-process objects
  with no host-produced bytes, so the conduit's single serialization at
  delivery IS the capture — serialized once, never re-serialized after.
  `docs/host-events/README.md` records this pi caveat. This is a design pin for
  the future pi pass; NO pi conduit is built this phase.
- Because the replacement class is skipped, the recorder never does work inside
  a hook; the teardown race is the only reason blocking is ever accepted.

### Scenarios & drivers (INFRA-28, "Capture scenario design & session driver")

- The shared, host-agnostic beat corpus (LIFE-24's list, ratified): fresh start →
  first prompt → tool calls → parallel subagent fan-out (≥2 concurrent) → a real
  permission prompt → a user interrupt → an idle wait past the notification
  threshold → clean exit. Per-host subtractions where a surface doesn't exist
  (Codex: no session-end signal; pi: no built-in permission system, no subagent
  events; Copilot: name-only subagent resolution).
- The corpus is a floor, not a ceiling: a host's scenario ADDS beats a specific
  empirical item needs. Claude Code adds one — a subagent task requiring a
  not-pre-authorized tool, so a **subagent-originated** permission prompt fires
  (LIFE-24 empirical item 2's does-`Notification`-carry-`agent_id`
  sub-question; the corpus's permission beat is main-thread, and its fan-out
  beat raises no prompt).
- A per-host **thin driver harness** over each host's own scripting surface
  (Claude Code `-p`/print or SDK; Codex `exec --json`/app-server; pi
  `--mode json`/rpc; Copilot's equivalent). No universal driver framework —
  four structurally different hosts, per-host is the right grain. Driver tech is
  unconstrained: this is dev-box diagnostics, not a shipped artifact, so DIST-4's
  in-box-runtime posture does not apply here.
- **A host's capture may span multiple session JSONLs.** Scripted beats run
  through the scripting surface where it genuinely reaches them; the
  interactive-constrained beats run in a supervised interactive session whose
  "driver" is a **cue script plus pre-staged prompt sequence**. Claude Code
  specifically: `-p` is one-shot (the process exits after the response — no
  idle window for `idle_prompt`, no interactive permission dialog, "interrupt"
  degenerates to a kill), and whether SDK-managed permissions (`canUseTool`)
  even fire the `Notification` hook is itself unverified — so idle-wait,
  interrupt, AND live-permission are all expected to land in the supervised
  session. Findings aggregate across a host's sessions, each citing its
  session id(s).
- Never fake the signal under test — the driver makes the host genuinely reach
  each state: a tool the session isn't pre-authorized for (real permission
  prompt), literally waiting out the idle threshold, a prompt explicitly
  requesting ≥2 parallel subagents. The interactive-constrained beats — user
  interrupt, the live permission approval, and (on hosts whose scripting
  surface can't hold a session open) the idle wait — run **supervised** (a
  human at the terminal following the cue script); no PTY automation in v1.
- The driver only exercises events the matrix has cleared; it never drives a
  skip-classified event.
- **Conduit staging** (how hooks find the exe — it has no alias, installer, or
  PATH story): checked-in conduit configs are **templates** with a placeholder
  for the recorder exe's absolute path. Each driver harness begins with a stage
  step: build/publish the recorder, create a throwaway scratch capture project
  directory, substitute the real exe path into the config, mint + export the
  session id. The substituted config exists only in the scratch dir, never in
  the repo.
- **Hook environment:** the recorder conduit is **project-scoped in the scratch
  dir**, never user-wide (user-wide would camp on every real session the
  operator runs). The operator's installed user-wide atv hooks (the shipped
  phase-13 integration) are **temporarily disabled during capture runs** — a
  clean observer baseline; co-firing atv hooks add per-event spawns and their
  own sync `SessionEnd`, which could shift timing. The disable and re-enable
  are the cue script's FIRST and LAST steps, never a remembered manual edit to
  the user settings — forgotten-before contaminates the baseline,
  forgotten-after leaves the operator's real sessions cardless. The findings
  doc records each session's hook environment either way, so a
  representative-conditions re-run stays cheap if ever wanted.

### Lifecycle & findings (INFRA-29, "Recorder lifecycle & maintenance model")

- Durable, maintained. Every finding in `docs/host-events/<host>.md` carries a
  **host version + capture date** stamp (the `integrations/claude-code/README.md`
  "Verified against:" convention). No CI, no cadence: re-capture is organic — a
  session about to trust a host mapping checks the stamp against the installed
  host version and re-runs the capture if stale.
- The stable/churn line follows LIFE-24's three-layer split: the verbatim core
  never changes on a host update (it does not understand payloads — that is why
  it is dumb); what churns per host is the conduit/hook config, the
  scenario/driver, and the findings doc.
- The LIFE-24 relationship stays documentation-only: these captures are the
  empirical ground truth rule 7 requires before a mapping counts as verified;
  the checked-in regression-fixture corpus was explicitly declined (operator,
  2026-07-11).

## Host execution scope (this phase)

**Claude Code only.** This phase builds the shared core plus the complete Claude
Code leg: safe/skip matrix, conduit config template, stage/driver harness, cue
script, live capture, stamped findings — including the added subagent-permission
beat, with the capture expected to split into a scripted leg plus one supervised
cue-script session (see Scenarios & drivers). First consumer: LIFE-24's open
empirical items 2 (which events fire inside subagents; `agent_id` uniqueness
across parallel spawns; whether `Notification: permission_prompt` carries
`agent_id`) and 3 (does `idle_prompt` fire after a user interrupt, on what
timing/repetition).

Copilot CLI, Codex, and pi get NO artifacts this phase — no matrix, conduit,
driver, or capture. Each is a future per-host pass, built when its host is
actually testable; Copilot's rides the machine that runs the deferred phase-13
Copilot leg (host not installed here — LIFE-24 empirical item 4) and precedes
that leg's integration work.

## Files affected

```
tools/host-event-recorder/HostEventRecorder.csproj  # new project; slnx member, no Atv refs
tools/host-event-recorder/*.cs                      # append core: argv/stdin, envelope, mutex
tools/host-event-recorder/hosts/claude-code/…       # conduit config TEMPLATE + stage/driver harness + cue script
tests/HostEventRecorder.Tests/…                     # core unit suite (no Atv refs either)
docs/host-events/README.md                          # conventions: matrix format, stamps, capture→findings flow
docs/host-events/claude-code.md                     # safe/skip matrix + stamped findings (only findings doc this phase)
AppTaskInfoCli.slnx                                 # + the two new projects
.gitignore                                          # + the captures directory
```

## Acceptance criteria

1. TDD core: unit tests prove byte-faithful capture (non-ASCII UTF-8, embedded
   quotes/newlines, malformed and non-JSON payloads round-trip exactly through
   the escaped `payload` string and back), the six envelope fields, one file per
   session id, no-tear concurrent appends (parallel writers; every line
   parses; no interleaving), mutex-name agreement (two writers handed the
   same log file via different path spellings — case, relative vs absolute —
   resolve the same mutex), and the default-path fallback (no arg, no env →
   the log lands exe-adjacent regardless of the process's working directory).
2. Separation is structural and verifiable both ways: `HostEventRecorder`
   references no atv project/namespace/brand constant, and nothing under `src/`
   references the recorder.
3. Solution `dotnet build` stays 0 warn / 0 err with the new projects; an
   explicit NativeAOT publish of the recorder is clean, and the single exe
   appends a correct envelope when driven standalone (argv + stdin smoke).
4. The Claude Code matrix exists in `docs/host-events/claude-code.md` BEFORE any
   capture runs, every candidate event classified on the one axis, skip reasons
   recorded.
5. Real Claude Code captures against the installed version: **one or more
   session JSONLs collectively cover the beat corpus plus the added
   subagent-permission beat** — scripted beats through the scripting surface
   where it genuinely reaches them, the interactive-constrained beats in the
   supervised cue-script session. The teardown event is captured (proving the
   sync-at-teardown posture against the phase-13 async-loss bug); every
   expected-but-absent event is recorded as an explicit did-not-fire finding
   citing its session id.
6. Findings distilled into `docs/host-events/claude-code.md` with host-version +
   capture-date stamps; LIFE-24 empirical items 2 and 3 answered there (or
   recorded as attempted, with what was observed, if genuinely unresolvable).
7. No raw capture is committed: the captures directory is gitignored and the
   phase's diff contains only code, configs, and docs — no session JSONL.
8. No speculative host legs: the only `hosts/` tree in the diff is
   `claude-code`. `docs/host-events/README.md` records the conventions a future
   host leg follows (matrix-before-capture, version+date stamps, the
   template+stage pattern, the pinned plumbing names, the pi verbatim caveat)
   so those passes start from the doc, not archaeology.

## Execution notes (gotchas, not decisions)

- AOT publish on this box needs `vswhere.exe` on PATH regardless of RID
  (documented in CLAUDE.md) — criterion 3's AOT smoke inherits that.
- The repo-wide `Directory.Build.props` inheritance (warnings-as-errors, NBGV
  versioning, inert AOT levers, `InvariantGlobalization`) is benign for the
  recorder, as INFRA-24 anticipated — nothing in it forces identity, CsWinRT,
  or a Windows TFM.
- `tests/HostEventRecorder.Tests/` follows the `Atv.LogicTests` MTP/MSTest
  convention (`EnableMSTestRunner`, exe output) while referencing no Atv
  project.

## Out of scope

- Everything on the LIFE-24 / v2 line: the semantic verb contract (ERGO-31), the
  per-host translator tables, and LIFE-25 ("Should host hooks invoke the `atv`
  exe directly instead of via a shell?") — decided but planned with that line,
  not here. This phase produces the captures those consume.
- The Copilot CLI, Codex, and pi recorder legs (matrix, conduit, driver,
  capture) — each is a future per-host pass, built when its host is actually
  testable (operator, 2026-07-12). Only Claude Code is testable here, so only
  its leg is built.
- The deferred phase-13 Copilot CLI / Codex integration legs themselves — this
  phase readies their verification tool; it does not ship their artifacts.
- Any query/tail/analyze surface in the recorder (capture-only v1, INFRA-24;
  revisit only if the jq/PowerShell friction proves real).
- A checked-in fixture-regression corpus for translators (explicitly declined,
  2026-07-11 — the relationship stays documentation-only, INFRA-29).
- LIFE-24 empirical item 1 (`NeedsAttention`'s taskbar group-badge priority) —
  an atv/taskbar experiment, not a hook capture; it belongs to the v2 line.
