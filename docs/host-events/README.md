# Host-event capture: conventions

Token-cheap durable reference for the host-event diagnostics recorder
(INFRA-23/24/25, `plan/phase-14-host-event-recorder.md`) -- the pattern
`docs/windows-ui-shell-tasks/` set for the WinRT surface, applied here to
host hook behavior. Future host legs (Copilot CLI, Codex, pi -- INFRA-31,
OPEN) start from this doc, not archaeology.

## What this is

`tools/host-event-recorder/` (project `HostEventRecorder`, exe
`host-event-recorder`) is a standalone, atv-INDEPENDENT, host-AGNOSTIC
compiled C# console tool. Any host's hooks spawn it once per event; it
reads the event's raw payload from stdin and appends a byte-faithful record
to a per-session JSONL file. It understands no host and no payload -- it is
a dumb, verbatim append core.

**Standing invariant #2 (brand parameterization) deliberately INVERTS
here**: this tool consumes no brand constant, no `Atv.*` reference, no
`$(AtvBrandName)`, no package identity. Nothing under `src/` references it,
and it references no atv project. See `CLAUDE.md` and phase-14's "Invariant
note" for why.

## Pinned plumbing names (cross-artifact contract)

These are pinned once, in code, at `tools/host-event-recorder/Constants.cs`
-- this section mirrors them verbatim. If they ever change, change the code
first and update this file in the same commit.

| Name | Kind | Value | Purpose |
|---|---|---|---|
| `HOSTREC_SESSION` | env var | -- | Capture session id. The driver mints one id per capture run and exports this so every hook-spawned recorder invocation inherits it. `--session <id>` (argv) overrides it. |
| `HOSTREC_CAPTURE_DIR` | env var | -- | Capture directory. The driver ALWAYS sets this explicitly, pointing at the gitignored `tools/host-event-recorder/captures/`. `--capture-dir <dir>` (argv) overrides it. |
| (fallback) | resolution basis | `AppContext.BaseDirectory` + `captures/` | Used only when neither the env var nor the argv flag supplies a capture directory. Exe-adjacent, **never** the process's current working directory -- hooks spawn the recorder with an arbitrary cwd, so a cwd-relative default would drop raw payloads (prompts, paths, possibly secrets) into whatever un-gitignored directory a stray invocation runs in. |
| `session-{sessionId}.jsonl` | filename format | -- | One JSONL file per capture session; the session id is embedded in the filename (sanitized for filesystem-invalid characters). |
| `adhoc-{yyyy-MM-dd}` | session id fallback | -- | Used only when neither `--session` nor `HOSTREC_SESSION` is supplied (manual, non-driver invocations). Dated (UTC), not random, so repeated manual runs on the same day land deterministically in the same file. |

Precedence, both path and session id: **explicit argv flag > explicit env
var > fallback.** The driver always sets both env vars explicitly, so the
fallbacks only ever engage for manual/ad-hoc invocations.

## Envelope

One JSON object per line, exactly six fields, in this order:

```json
{"ts":"2026-07-12T18:03:44.1912837Z","host":"claude-code","event":"PostToolUse","pid":41232,"session":"2026-07-12-demo","payload":"{\"tool\":\"Bash\"}"}
```

- `ts` -- ISO-8601 UTC, sub-millisecond, wall-clock at write time.
- `host` / `event` -- verbatim from the recorder's `--host`/`--event` argv.
- `pid` -- the recorder invocation's own process id (not the host's).
- `session` -- the resolved session id (see precedence above).
- `payload` -- the host's stdin bytes, decoded UTF-8, stored as a
  JSON-escaped string. Never re-parsed or re-emitted as JSON: re-serializing
  would silently normalize key order, whitespace, and number formatting.
  Malformed-JSON and non-JSON payloads are captured identically -- they are
  still valid UTF-8 text and round-trip losslessly through the escaped
  string. The envelope uses relaxed JSON escaping
  (`UnsafeRelaxedJsonEscaping`), so inner quotes render as `\"` and non-ASCII
  stays literal (`café`) rather than `"`/`é` -- a lossless
  representation choice for greppability (INFRA-25). Serialization is
  single-sourced through `EnvelopeSerialization.Serialize`.

File line order is the authoritative sequence; there is no `seq` field.

## Guarded append

Appends are serialized by a named `System.Threading.Mutex` scoped to the
log file, held only for the one small append. The mutex name is derived
from the log file's path: canonicalize (`Path.GetFullPath`), case-fold
(`ToUpperInvariant` -- NTFS is case-insensitive), hash (SHA-256, hex), and
prefix `Local\`. Two processes handed the same file via different path
spellings (case, relative vs. absolute, slash direction, trailing
separators) resolve to the identical mutex name, so records structurally
cannot tear under parallel hook fan-out.

## Raw captures are never committed

`tools/host-event-recorder/captures/` is gitignored. Raw payloads may carry
prompts, cwd paths, tool inputs, possibly secrets. Only **distilled
findings** (safe/skip matrices, confirmed event behavior, did-not-fire
results) reach a per-host doc under `docs/host-events/` (e.g.
`claude-code.md`), each carrying a host-version + capture-date stamp.

## Conventions a future host leg follows (INFRA-31)

1. **Matrix before capture**: a safe/skip classification for every
   candidate event, with skip reasons recorded, exists before any capture
   run against that host.
2. **Version + date stamps**: every finding in a host's doc cites the host
   version and capture date it was confirmed against; re-capture is organic
   (no CI, no cadence) -- a session about to trust a stale-looking mapping
   re-runs the capture rather than trusting an old stamp.
3. **Template + stage pattern**: the checked-in conduit config for a host is
   a template with a placeholder for the recorder exe's absolute path (the
   exe has no alias, installer, or PATH story); a driver harness stages a
   throwaway scratch capture project, substitutes the real path, mints and
   exports the session id. The substituted config never lands in the repo.
   The project-scoped conduit is never installed user-wide during a capture
   run (that would camp on the operator's real sessions); if the operator
   has a real, installed integration for the host, disabling and
   re-enabling it are the driver/cue script's first and last steps.
4. **These pinned plumbing names** are shared by every host leg verbatim --
   a new `hosts/<name>/` tree does not mint its own env var or filename
   convention.
5. **The pi conduit caveat**: pi's conduit is in-process (a TypeScript
   extension spawning the recorder per event, single serialization at
   delivery is the capture) rather than a shell-hook conduit -- see
   INFRA-27. Its driver/stage mechanics will differ from Claude Code's
   process-per-hook model even though the recorder core and envelope stay
   identical.

## Status

Phase-14 is **complete**. Part A (the recorder core) is built and gated (Gate A).
The Claude Code leg is built AND proven by a real supervised capture (INFRA-30):
the safe/skip matrix, conduit template, and stage/driver/cue harness live under
`docs/host-events/claude-code.md` and `tools/host-event-recorder/hosts/claude-code/`,
and that doc's **Findings** section holds the confirmed results from four real
captures (2026-07-12/13, Claude Code 2.1.207), including the `SessionEnd`
sync-at-teardown proof and answers to LIFE-24 empirical items 2 and 3. Future host
legs (Copilot CLI, Codex, pi -- INFRA-31, OPEN) follow the conventions above from
this doc.
