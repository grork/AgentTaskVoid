# INFRA-8: The seam between CLI logic and the WinRT API for unit testing
**Status:** DECIDED
**Plan:** all-phases
**Decision:** A hand-rolled `IAppTaskStore` seam -- a Repository over the platform's
durable task store: id-addressed, DTO-in/DTO-out so no projected WinRT type crosses
it. One real adapter (`AppTaskStore`) is the SOLE importer of
`Windows.UI.Shell.Tasks`; the fake (`FakeAppTaskStore`) is in-memory and emits a
simulated `tasks.json` (INFRA-11). Everything else -- the ERGO-10 validator, the
ERGO-8 advance model, state mapping, handle resolution, the write-orchestration --
lives ABOVE the seam as plain code unit-tested against the fake (so INFRA-10 can test
the validator; the seam does NOT own it). The cross-process lock is NOT abstracted:
the write path (and the watchdog) receive a `System.Threading.Mutex` from the
composition root (production = named `Local\<brand>-<PFN>-tasks-write`; tests =
unnamed/unique), acquire/bounded-wait/AbandonedMutex/release written once inline.
**Parent:** INFRA-4

What abstraction isolates `Windows.UI.Shell.Tasks` behind an interface so the
bulk of the CLI (argument parsing, validation, state mapping, ID handling) can
be unit-tested against a fake, with no package identity and no API availability
required?

Decision detail (2026-07-03):
- Why hand-rolled, not the CsWinRT-vended interfaces: those are sealed classes +
  `internal` marshaling interfaces + `static` activation-factory entry points, and
  their signatures return the sealed types a fake can't fabricate. Reusing them
  (InternalsVisibleTo + hand-pulling statics off the activation factory) is fragile
  (couples us to CsWinRT's internal generated shape across versions) and buys no fake.
  Not worth it. (Evidence: CsWinRT 2.2.0 generated projection.)
- Repository, NOT a shadow of `IAppTaskInfo`: an id-addressed CRUD surface keyed by the
  platform's own `AppTaskInfo.Id`, not a per-object handle -- the handle topology is the
  un-fakeable one. Caller-handle (ERGO-6) -> Id mapping sits ABOVE the seam in the
  ERGO-7 sidecar. Its statefulness mirrors the platform's real durable state
  (`tasks.json`); the fake swaps the backing store, and the fidelity obligation that
  creates is INFRA-15.
- Lock: the only prod/test difference is the `Mutex` instance (named vs unnamed), so no
  interface/helper earns its keep. `AbandonedMutexException` stays unit-testable in-proc
  (an unnamed `Mutex` can be abandoned). Shared acquire logic, if two call sites need
  it, is a plain `static` function.
- Sidecar seam/testing is ERGO-21; INFRA-8 only fixes that sidecar + API writes sequence
  under the one injected mutex.
- Name `IAppTaskStore` chosen over Gateway/Broker/Manager/Facade/Repository: `AppTask`
  prefix disambiguates from `System.Threading.Tasks.Task`. Impls `AppTaskStore` /
  `FakeAppTaskStore`; DTOs `AppTaskView` / `AppTaskContentDto`.
