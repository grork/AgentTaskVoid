# Phase 02: Core seam — `IAppTaskStore`, real adapter, fake, logic test suite

**Depends on:** phase 01 (solution layout, CsWinRT projection)
**Unblocks:** phases 03, 04, 05 (all logic is written above this seam)

## Goal

Introduce the single testing seam between CLI logic and the WinRT API: a hand-rolled
`IAppTaskStore` repository, one real adapter that is the sole importer of
`Windows.UI.Shell.Tasks`, and an in-memory fake with documented fidelity promises.
Bootstrap the fake-backed logic test suite that every later phase adds tests to.

## Decisions implemented

- INFRA-8 ("The seam between CLI logic and the WinRT API for unit testing"):
  - `IAppTaskStore` is a Repository over the platform's durable task store:
    id-addressed CRUD keyed by the platform's own `AppTaskInfo.Id`, DTO-in/DTO-out —
    no projected WinRT type crosses the seam. Names are settled: interface
    `IAppTaskStore`, impls `AppTaskStore` / `FakeAppTaskStore`, DTOs `AppTaskView` /
    `AppTaskContentDto`.
  - Do NOT reuse the CsWinRT-vended interfaces (sealed classes + internal marshaling
    types; a fake can't fabricate them).
  - Caller-handle → Id mapping sits ABOVE the seam (the sidecar, phase 04). The
    ERGO-10 validator, advance model, state mapping, handle resolution, and write
    orchestration all live above the seam as plain code.
  - The cross-process lock is NOT abstracted: write paths receive a
    `System.Threading.Mutex` from the composition root (production = named, tests =
    unnamed/unique). Acquire/bounded-wait/AbandonedMutex/release is written once
    inline (a plain static helper if two call sites need it). Wiring happens in
    phase 04; the constructor seams are shaped here.
- INFRA-15 ("The bounded set of platform behaviors the fake must mimic"): the fake
  mimics EXACTLY four behaviors, documented as a "fidelity promises" contract on
  `FakeAppTaskStore` (each item tagged with its confirming check, referenced by the
  INFRA-13 new-build checklist):
  1. **Non-atomic whole-store clobber / last-writer-wins** with a deterministic
     interleave hook ("another writer committed between your read and your write") —
     unprotected write → deterministic loss; mutex-wrapped → no loss. (INFRA-5
     empirical: 4×100 unlocked creates → 37/400 survived; locked → 400/400.)
  2. **`HiddenByUser` surfacing** — a per-task flag tests can set, visible through
     enumeration (drives the ERGO-2 sweep and reconciliation).
  3. **Out-of-band drift hooks** — delete a task behind the logic's back; seed a task
     the logic never created (entryless); ops on a vanished/unknown Id return a clean
     not-found, never throw.
  4. **Platform mints an opaque `Id` on create** — so nothing above the seam can
     assume an id format.
  MUST NOT mimic: the crash matrix, Shell rendering/grouping, file-watcher behavior,
  timing/latency, exact COMException codes. The fake must NOT offer a convenience
  merge/append — writes are whole-content replacement, like the platform (ERGO-8).
  No generic error-injection mode for now.
- INFRA-11 ("Test strategy for machines where the API is unavailable") + INFRA-9
  ("Integration-test harness over tasks.json"), the logic half: the fake-backed LOGIC
  suite runs everywhere, always, in parallel; each test owns a `FakeAppTaskStore` and
  asserts via its `FindAll()`. No `tasks.json` emission from the fake is needed for
  logic tests (watchdog raw-file reading is tested with hand-authored fixtures,
  phase 09).
- INFRA-13 ("Windows build compatibility strategy"), the adapter half: the real
  adapter wraps `IsSupported()` for the `CLASS_E_CLASSNOTAVAILABLE` COMException
  (runtime capability detection, never version pinning). Blast radius of any future
  API change stays localized to this one adapter.

## Files affected

```
src/Atv/Store/IAppTaskStore.cs
src/Atv/Store/AppTaskView.cs           # + AppTaskContentDto (content shapes: sequence-of-steps, text-summary, question, state enum)
src/Atv/Store/AppTaskStore.cs          # SOLE importer of Windows.UI.Shell.Tasks; IsSupported wrapper
src/Atv/Store/FakeAppTaskStore.cs      # in main project or the test project — pick one; it ships nowhere (test-only visibility preferred: put it in the logic test project or a shared test utilities project)
tests/Atv.LogicTests/Atv.LogicTests.csproj   # framework: pick one compatible with Microsoft.Testing.Platform (the phase-03 suite MUST be MTP; use the same framework here for consistency)
tests/Atv.LogicTests/Store/FakeAppTaskStoreTests.cs
docs/testing/fake-fidelity-promises.md # the INFRA-15 contract, one source of truth
```

The DTO content model must cover what the v1 verbs need (ERGO-9): sequence-of-steps
(executing step + completedSteps list), text-summary result, question (NeedsAttention),
state, title/subtitle/iconUri/deepLink. Consult
`docs/windows-ui-shell-tasks/` for the API's actual shapes.

## Acceptance criteria

1. `AppTaskStore` compiles as the only file importing `Windows.UI.Shell.Tasks`
   (enforce with a test or an architecture assertion grepping the source tree).
2. Fake suite green, covering: CRUD round-trip through the fake; opaque-Id minting;
   unknown-Id ops return not-found (no throw); the interleave hook produces
   deterministic last-writer-wins loss; `HiddenByUser` surfaces through enumerate;
   drift hooks (vanish / seed-unknown) behave as specified.
3. `docs/testing/fake-fidelity-promises.md` lists the four promises, each with its
   confirming check (which are automated in phase 03's real suite vs manual/periodic).
4. The whole logic suite runs with no package identity and no API present (verify by
   running it — it must not touch the real adapter).
5. NativeAOT publish still clean (the seam introduces no reflection).

## Out of scope

Real-API tests (phase 03), the mutex wiring and sidecar (phase 04), any verb logic
(phase 05+).
