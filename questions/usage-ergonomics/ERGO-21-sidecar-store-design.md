# ERGO-21: The sidecar store design
**Status:** DECIDED
**Plan:** phase-04
**Decision:** The sidecar is an INDEX, not an allowlist. A directory of PER-HANDLE
files in package app-data, each mapping `handle -> {id, lastUpdate, schemaVersion}`,
written by atomic temp-file+rename. Invocation-time reconciliation against
`FindAll()` is NON-DESTRUCTIVE: keep live entries, drop our own stale entries, sweep
`HiddenByUser` (ERGO-2, "Garbage collection of orphaned / user-hidden entries"), and
NEVER touch live-but-unknown API tasks. Orphan/entryless reaping -- the "allowlist"
behavior -- is the watchdog's job, handed to LIFE-7 ("Idle-expiry policy and what
expiry does"). SUPERSEDED (2026-07-05): LIFE-23 ("Entryless-orphan reaping and the
mass-deletion guard") decided NO guard -- entryless tasks are reaped unconditionally
and audibly (logged). No journaling.

Decision detail (2026-07-03):
- Semantics (the crux): the sidecar's concern is ONLY the `handle -> Id` index and
  liveness stamp; it is authoritative for NOTHING about task existence (the API
  stays source of truth, ERGO-7, "Whether the CLI keeps persistent state"). So the
  invocation hot path never force-purges: a stray call can't nuke live tasks, and a
  wiped/corrupt sidecar degrades gracefully (tasks become un-addressable-by-handle,
  not deleted) instead of triggering mass deletion.
- Schema (DP2): per-handle file holds `{id, lastUpdate, schemaVersion}` and nothing
  more -- no cached content (titles/steps/state live in the API, ERGO-8). The ERGO-7
  "group/owner/cwd" metadata is DROPPED for v1: `group` killed by ERGO-14 ("no
  grouping knob"), `owner` by ERGO-16 ("no ownership layer"), and `cwd`'s only
  consumer (INTER-4, focus-terminal) is deferred. Add fields when a feature needs
  them; `schemaVersion` keeps that cheap.
- Topology (DP1): per-handle files, chosen over a single `sidecar.json` and over the
  registry. A single-file map recreates the INFRA-5 ("concurrent writes") whole-file
  clobber hazard IN OUR OWN STORE -- every write is a read-modify-write of the whole
  map, so two different handles clobber -- and leans on the INFRA-6 mutex to paper
  over it. Per-handle files remove that structurally (a write touches one file,
  atomic by rename), shrink torn-write blast radius to one handle, and match LIFE-5's
  per-handle `lastUpdate` polling. Registry rejected: breaks the "beside tasks.json,
  inspectable, file-as-interface" property and enumerates clumsily. Build detail:
  handles are caller-supplied strings (ERGO-6, "The identifier a caller holds"), so
  the filename needs a REVERSIBLE percent-encoding, not the raw string and not a
  one-way hash [ratified 2026-07-07: `list` and expiry-time recycle-bin records must
  recover the handle from an entry].
- Location: package app-data (`ApplicationData.Current.LocalFolder`), beside
  tasks.json and the FAIL-3 ("Diagnosability") log.
- Reconciliation rules (under the INFRA-6 mutex -- a read-modify-write across both
  stores) [SCOPED 2026-07-07: the full pass runs on `start`/`remove`/`clear` and
  watchdog ticks; update-class verbs resolve only their own handle -- no `FindAll()`,
  no sweep, per ERGO-19]:
  - entry present, API knows `id`, not hidden -> keep;
  - entry present, API no longer knows `id` -> drop entry (our stale mapping);
  - API `id` is `HiddenByUser` -> `Remove()` + drop entry (the ERGO-2 sweep);
  - API `id` with NO entry (entryless / live-but-unknown) -> leave alone; watchdog
    territory (LIFE-7).
  Consequence: a `step <handle>` whose entry was dropped is a clean unknown-handle
  no-op/fail (no upsert-on-step in v1, ERGO-8, "Update verbs").
- Atomicity / ordering (DP4): per-handle atomic replace means the sidecar file never
  tears. For `create`, API-first is forced (need the returned `Id` before writing the
  file). No journaling / two-phase commit. The residual "API create landed, file
  write crashed" divergence is just an entryless orphan -- subsumed by the
  reconciliation rule above and reaped by LIFE-7, not specially handled.
- Watchdog handoff: reaping is two mechanisms, only one of which is "allowlist". (1)
  Known-but-cold -- a task WITH an entry whose `lastUpdate` went stale -- is the
  common turd (unclean session death, LIFE-12/13/14 "no reliable session-end") and is
  reaped SAFELY by idle expiry (LIFE-7); this is liveness expiry on a
  supervised task, not allowlist behavior. (2) Entryless -- a live API task with no
  entry -- is the ONLY allowlist case; it catches just the rare DP4 orphan and CANNOT
  be distinguished from "a live task whose entry I lost", so it carries the
  sidecar-loss mass-deletion risk. That risk RELOCATES to the watchdog under this
  split; it does not vanish. LIFE-7 owns whether/how entryless-reap happens and MUST
  carry a guard (e.g. refuse to reap when the sidecar dir is absent/empty or the
  unknown-fraction is suspiciously high -- log instead). [Superseded by LIFE-23
  ("Entryless-orphan reaping and the mass-deletion guard"): no guard -- unconditional,
  audible reap.]
- Testing seam (closes the INFRA-8, "The seam between CLI logic and the WinRT API"
  handoff): the sidecar needs NO interface -- inject a directory path (prod =
  LocalFolder, test = temp dir), plain-unit-testable, mirroring INFRA-8's
  mutex-injection logic. The fake-backed logic suite (INFRA-9, "Integration-test
  harness over tasks.json") asserts these reconciliation rules using its drift hooks
  (task-vanished / HiddenByUser / unknown-id).

ERGO-7 decided the CLI keeps a persisted `handle -> AppTaskInfo.Id` (+ metadata)
sidecar; later decisions loaded real mechanics onto it that were asserted, not
designed. Consolidate here so nothing is buried:
- Schema: handle -> Id, plus metadata (group/owner/cwd -- ERGO-7) and a per-handle
  `lastUpdate` timestamp the watchdog polls for its idle expiry (LIFE-5). Format
  (JSON?), single file vs per-handle files.
- Location: package app-data (`ApplicationData.Current.LocalFolder`), beside
  tasks.json (the FAIL-3 log lives there too).
- Reconciliation: ERGO-7's "reconcile against `FindAll()` each invocation" --
  define the exact rules and drift/edge cases (task removed out from under us,
  HiddenByUser, a sidecar Id the API no longer knows, and the reverse).
- Atomicity with the API write: INFRA-6 asserts the API write + sidecar write run
  under one mutex "atomically". True atomicity is subtle -- if the API write lands
  but the sidecar write fails (crash/disk), they diverge. Decide the write
  ordering and whether reconciliation is a sufficient backstop (likely) or we need
  journaling (likely overkill).
Cross-process concurrency is already covered by the INFRA-6 global mutex.
