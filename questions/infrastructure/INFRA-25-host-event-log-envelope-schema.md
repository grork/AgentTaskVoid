# INFRA-25: Host-event log envelope schema
**Status:** DECIDED
**Plan:** unplanned
**Parent:** INFRA-23
**Decision:** One JSONL file per capture session, named-mutex-serialized appends (line
order is the authoritative sequence — no seq field); each record is `{ts, host, event,
pid, session, payload}` with `payload` stored as a JSON-escaped string (byte-faithful,
never re-parsed); captures are gitignored/local, only distilled findings reach
`docs/host-events/`.

## Question
What is the concrete shared log format that every host source writes through, via
INFRA-24's shared recorder core? INFRA-23's ratified paradigm already fixes the format's
spirit — raw/verbatim payload, no normalization or interpretation at capture time — this
question is the literal schema and file/append mechanics.

## Decision (operator + Claude Code, answer session, 2026-07-12)

### Envelope fields (one JSON object per line)
- `ts` — ISO-8601 UTC, sub-millisecond, wall-clock at write time.
- `host` — host tag (`claude-code` / `copilot` / `codex` / `pi`), from argv.
- `event` — host event name, from argv.
- `pid` — the recorder invocation's process id (disambiguates concurrent writers).
- `session` — capture-session id, ties records to one INFRA-28 scripted run.
- `payload` — the host payload **as a JSON-escaped string**, verbatim.

**Why `payload` is an escaped string, not a re-parsed nested object.** Re-parsing the
host's JSON and re-emitting it would silently normalize key order, whitespace, and number
formatting — a violation of the ratified "raw/verbatim, no normalization at capture time"
paradigm. An escaped string is byte-faithful and losslessly reversible; the compiled C#
core (INFRA-24) reads stdin as raw bytes → decodes UTF-8 → JSON-escapes → embeds, so the
captured bytes survive intact. Non-JSON or malformed payloads are captured just the same.

### File shape & location
- **One JSONL file per capture session** (the unit of analysis, INFRA-28) — trivial to
  diff, review, or discard a run — with the session id in the filename.
- **No separate sequence number.** Under the serialized append (below), file line-order
  *is* the authoritative total order of writes; `ts` records the fire-ish moment. Perfect
  ordering of near-simultaneous async hooks is neither recoverable nor needed.
- **Captures land in a gitignored local directory** by default (path overridable by
  arg/env). Raw payloads carry prompts, cwd paths, and tool inputs — possibly secrets —
  so they are never committed; only the **distilled findings** land in `docs/host-events/`
  (INFRA-23's capture-raw → interpret → persist-findings model).

### Concurrency-safety: named mutex, not OS-atomicity betting
Appends are serialized by a **named system mutex** (`System.Threading.Mutex`) held only
for the duration of one small append. Records therefore **cannot tear** even under real
parallel hook fan-out — a structural guarantee, chosen over betting on empirically-
uncertain NTFS single-write atomicity, because faithful capture is the tool's entire
purpose. Contention is negligible (events don't fire that fast; each append is a few KB).
The mutex is scoped to the log file, independent of any atv package-identity mutex.
