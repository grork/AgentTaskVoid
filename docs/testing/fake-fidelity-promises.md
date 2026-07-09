# Fake fidelity promises (INFRA-15)

Single source of truth for what `FakeAppTaskStore`
(`tests/Atv.LogicTests/Store/FakeAppTaskStore.cs`) promises to mimic about the
real `Windows.UI.Shell.Tasks` platform — and, just as deliberately, what it does
NOT. Referenced by `FakeAppTaskStore`'s own doc comment and by the INFRA-13
new-build compatibility checklist.

The fake is **not** a stand-in for real API behavior — phase 03's real-adapter
suite (`tests/Atv.AdapterTests/`) proves the thin `AppTaskStore` adapter itself
drives the platform faithfully. Full parity is explicitly not the goal
(INFRA-15's decision detail); the bar is "enough fidelity that a logic test
depending on one of these promises means something." The list below is
therefore TIGHT and gated by "a specific logic test would test the wrong thing
without it" — not by how much of the real platform we could plausibly model.

## The four promises

### 1. Non-atomic whole-store clobber / last-writer-wins

**What the real platform does:** `OSClient.API.dll` does not serialize writes
to `tasks.json` across processes — every `Create`/`Update`/`Remove` reads the
whole file, mutates, and rewrites it, with no cross-process lock. Concurrent
writers clobber each other's writes (last-writer-wins on the *whole file*, not
just the touched task). Empirically (Windows 11 26100, 2026-07-02): 4 processes
× 100 creates each kept only 37/400 unlocked; the same test kept 400/400 once
every write went through a system-wide named mutex. See
`docs/windows-ui-shell-tasks/README.md`, "Concurrency: writes are not
serialized across processes."

**Why a logic test needs it:** it's the only thing that can prove the INFRA-6
mutex mitigation (`WriteGate`, phase 04) actually does something — without a
fake that can lose writes, a test asserting "the mutex prevents loss" has
nothing to regress against.

**Fake mechanism:** every mutating member (`Create`, `Update`, `UpdateState`,
`UpdateTitles`, `UpdateDeepLink`, `Remove`) goes through one chokepoint,
`FakeAppTaskStore.MutateWholeStore`, which snapshots the whole in-memory store,
invokes the test-settable `InterleaveHook` (default `null` = no-op), computes
its own new whole-store state from that now-possibly-stale snapshot, then
blindly commits it — discarding anything the hook wrote in between. This
reproduces "another writer committed between your read and your write" on a
single thread, deterministically, with no real concurrency needed.

**Confirming check:**
- Fake mechanism: `FakeAppTaskStoreTests.InterleaveHook_ProducesDeterministicLastWriterWinsLoss`
  (unprotected loss) and `..._NoInterleave_TwoSequentialCreates_BothSurvive`
  (control case, no hook set → no loss) — both in
  `tests/Atv.LogicTests/Store/FakeAppTaskStoreTests.cs` (phase 02).
- "Mutex-wrapped → no loss": phase 04's `WriteGate` tests, against this same
  hook (out of scope here — see `plan/phase-04-persistence.md` acceptance
  criterion 1).
- Real platform still clobbers as modeled: MANUAL, periodic, not a gate — the
  demoted 4×100 multi-process run, `tests/Atv.AdapterTests/PeriodicClobberTests.cs`
  (phase 03), excluded from default runs, tied to the INFRA-13 new-build
  checklist.

### 2. `HiddenByUser` surfacing

**What the real platform does:** each card in a taskbar flyout has an **X**
dismiss button that sets `AppTaskInfo.HiddenByUser = true`. This does not
remove the task from `FindAll()`/`tasks.json` — the app is expected to notice
(e.g. on the next `FindAll()`) and call `Remove()` itself, or the hidden entry
lingers forever. See `docs/windows-ui-shell-tasks/AppTaskInfo.md`.

**Why a logic test needs it:** it's what drives the ERGO-2 orphan sweep and the
ERGO-21 sidecar reconciliation's "hidden → `Remove()` + drop entry" rule — both
need a way to get a task into the hidden state without going through the
Shell.

**Fake mechanism:** `FakeAppTaskStore.SetHiddenByUser(id, hidden)` — a
test-only, out-of-band setter (there's no `IAppTaskStore` write path for this;
the real platform's only writer is the Shell, not the app) — surfaced through
`Find`/`FindAll` exactly like the real `HiddenByUser` property.

**Confirming check:**
- Fake mechanism: `FakeAppTaskStoreTests.HiddenByUser_DefaultsFalse_AndSurfacesThroughFindAndFindAll`
  (phase 02).
- Real platform, READ path (our code surfacing a flag already present in
  `tasks.json`): automatable via hand-authored `tasks.json` fixtures (INFRA-9)
  — phase 03/09 territory, not built yet.
- Real platform, SETTER-on-gesture (the real user X-click firing the real
  setter): MANUAL "dark matter" only — no automated harness can drive a real
  Shell click (INFRA-13, low priority).

### 3. Out-of-band drift: vanish, seed-entryless, clean not-found

**What the real platform does:** a task can disappear without this process
having removed it (another writer's clobber, promise 1; a concurrent process
calling `Remove()`), and conversely `tasks.json` can contain a task this
process's sidecar (phase 04) never recorded a handle for — e.g. after a crash
between the API `Create()` landing and the sidecar file write. Every
`AppTaskInfo` operation on a stale/unknown Id is expected to behave cleanly,
never throw.

**Why a logic test needs it:** it's what the ERGO-21 sidecar reconciliation
matrix (keep / drop / sweep / leave-alone) is tested against, and it's the
basis for every "unknown-Id ops don't throw" assertion made anywhere above the
seam.

**Fake mechanism:**
- `FakeAppTaskStore.SimulateVanish(id)` deletes a task directly, bypassing
  `Remove()` and its whole-store-clobber hook.
- `FakeAppTaskStore.SeedEntrylessTask(...)` inserts a task directly (still
  through the whole-store commit path, still minting an opaque Id per promise
  4) with no corresponding caller-side handle — the "API knows it, sidecar
  doesn't" entryless state.
- Every `IAppTaskStore` member (`Find`, `Update`, `UpdateState`,
  `UpdateTitles`, `UpdateDeepLink`, `Remove`) returns a clean not-found
  (`null`/`false`) for an unknown/vanished Id by construction — no separate
  hook needed; it's the default behavior of the underlying lookup.

**Confirming check:**
- Fake mechanism: `FakeAppTaskStoreTests.SimulateVanish_RemovesTask_BehindLogicsBack`,
  `..._SeedEntrylessTask_AppearsInFindAll_WithOpaqueMintedId`,
  `..._UnknownId_Find_ReturnsNull_NoThrow`,
  `..._UnknownId_MutatingOps_ReturnFalse_NoThrow` (all phase 02).
- Real platform still behaves this way: AUTOMATED in phase 03's
  `tests/Atv.AdapterTests/AdapterFidelityTests.cs` — unknown-Id / removed-Id
  behavior, and the negative whole-content-replacement check (an `Update`
  replaces content wholesale; nothing merges) (INFRA-9, INFRA-15).

### 4. Platform mints an opaque `Id` on create

**What the real platform does:** `AppTaskInfo.Create(...)` returns a new
instance whose `Id` is auto-generated by the platform — nothing about its
shape is documented or guaranteed.

**Why a logic test needs it:** so the handle → Id mapping (the sidecar, phase
04) and everything above the seam can never accidentally come to depend on an
assumed Id format.

**Fake mechanism:** `FakeAppTaskStore.MintId()` produces
`fake-task-{counter:x8}-{guid:N}` — a shape that shares nothing with whatever
the real platform actually uses, so a test asserting on Id *content* (rather
than just "non-empty, distinct, opaque") would visibly break.

**Confirming check:**
- Fake mechanism: `FakeAppTaskStoreTests.Create_MintsNonEmptyDistinctIds`
  (phase 02).
- Real platform: implicitly confirmed by every phase 03 create round-trip test
  (no dedicated check needed — using the platform normally already proves this;
  there's nothing else to verify beyond "don't assume a format").

## Must NOT mimic (anti-overshoot guardrails)

- The state × content crash matrix — invisible in `tasks.json`, Shell-render-only
  (INFRA-10 already decided the fake must not model this; see
  `docs/windows-ui-shell-tasks/state-content-compatibility.md`).
- Shell rendering / grouping-by-`IconUri` (ERGO-13).
- `explorer.exe` file-watcher coalescing / live re-render behavior (INFRA-7).
- Latency, timing, or exact `COMException` codes (INFRA-12 measures latency on
  the real thing, not the fake).
- A convenience content merge/append on write — every write is whole-content
  replacement, exactly like the platform (ERGO-8). This is a negative
  obligation, not something to build: the DTO-in/DTO-out seam (INFRA-8) makes
  replacement free: there is simply no merge code to add.
- A generic error-injection mode — not a platform behavior to mimic, and
  explicitly deferred (INFRA-15: "revisit at test-build time" if the FAIL-1
  non-disruptive path needs it later).

## Changing this list

This is the one place the four promises are enumerated. If a future phase
needs the fake to model a fifth behavior, add it here first (with its
rationale and confirming check), then implement it on `FakeAppTaskStore` — not
the other way around.
