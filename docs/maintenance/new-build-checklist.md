# New-build checklist (INFRA-13)

`Windows.UI.Shell.Tasks.AppTaskInfo` is `[Experimental]` — undocumented Shell
rendering behavior, no compatibility guarantee across Windows builds. This
checklist is the short list of empirically-discovered platform facts baked
into this codebase as data (never re-derived at runtime) that a new
Windows 11 build could silently invalidate. None of it is exercised by CI or
the default test run — it's "dark matter": low-probability of changing,
expensive to re-verify by hand, and safe to defer until there's a concrete
reason to suspect drift (a new Windows 11 build/Insider ring landing on a
dev or dogfood machine, a bug report that smells like a rendering
regression, or a scheduled periodic check if one is ever set up). This doc
exists so that when someone does the check, they know exactly what to run
and where the result has to be updated.

## The rule

**A failed check updates the matrix data and the fake together, in the same
change.** Both live at a single source of truth per plan/README.md standing
invariant #8 ("Empirical platform knowledge lives as data in one place") —
never patch call-site logic around a stale fact; correct the fact where it's
recorded, and everything downstream (the validator, the fake, this
checklist) stays consistent by construction.

## 1. The state × content crash matrix (ERGO-10)

**What it is:** which (content shape, state, mutator) combinations
`AppTaskInfo.Update()` accepts and renders correctly vs. silently renders
wrong vs. crashes `explorer.exe`. Full matrix + the exact
`STATUS_STACK_BUFFER_OVERRUN` fault signature:
[`docs/windows-ui-shell-tasks/state-content-compatibility.md`](../windows-ui-shell-tasks/state-content-compatibility.md).
The safe subset this codebase uses is encoded as data in
[`src/Atv/Operations/SafeCombinationMatrix.cs`](../../src/Atv/Operations/SafeCombinationMatrix.cs)
(`SafeCells`, cross-checked against the doc's table at authoring time) — the
`Validator` is the only consumer; nothing else re-derives what's safe.

**How to re-check on a new build:** manually drive each safe cell in
`SafeCombinationMatrix.SafeCells` (7 cells: `SequenceOfSteps` at
Running/Completed/Paused/Error/NeedsAttention-with-question,
`TextSummaryResult` at Completed/Error) via `atv start`/`step`/`state`/
`done`/`fail`/`attention` and eyeball the taskbar card each time — confirm it
still renders as expected, no crash. Optionally also spot-check a couple of
the documented-unsafe cells (e.g. `TextSummaryResult` + a question, `Running`
with no mutators) to confirm `explorer.exe` still crashes the same way — if a
previously-crashing cell now renders fine, that's a widening of the safe set,
worth adding to `SafeCells` (not required, but no longer worth guarding
against).

**If something changed:** update `SafeCombinationMatrix.SafeCells`,
`docs/windows-ui-shell-tasks/state-content-compatibility.md`'s table, and
`docs/testing/fake-fidelity-promises.md`'s "must not mimic" note on this
matrix (it currently says the fake does not model Shell-render-only behavior
— re-read that note before deciding whether a crash-matrix change should
change the fake, since INFRA-10 already decided it shouldn't).

## 2. Grouping keyed on `IconUri`, not `Title` (ERGO-13)

**What it is:** taskbar-icon grouping is keyed by `IconUri` — tasks sharing
the exact same icon URI merge into one taskbar icon (each still its own card
in the flyout); tasks with different icon URIs get separate taskbar icons,
even with an identical title. This directly contradicts the Microsoft docs
remark on `AppTaskInfo.Title` ("tasks are grouped based on the title").
Documented in
[`docs/windows-ui-shell-tasks/README.md`](../windows-ui-shell-tasks/README.md#taskbar-grouping-mechanics).
The per-handle icon model (`src/Atv/Icons/IconService.cs`) depends on this:
each handle gets its own rendered icon copy specifically so cards never
accidentally merge just because two callers picked the same title.

**How to re-check on a new build:** `atv start a --title "Same Title" --icon
Robot`, `atv start b --title "Same Title" --icon Robot` (same icon token —
should merge into one taskbar icon with two cards in its flyout); then `atv
start c --title "Same Title" --icon Bug` (different icon — should get its
own taskbar icon despite the identical title). Eyeball both outcomes.

**If something changed:** update the README's "Taskbar grouping mechanics"
section — this is prose documentation with no second file to keep in sync,
but re-read whether `IconService`'s per-handle icon design, which exists
because of this fact, still makes sense.

## 3. The fake's four fidelity promises (INFRA-15)

Single source of truth:
[`docs/testing/fake-fidelity-promises.md`](../testing/fake-fidelity-promises.md).
`FakeAppTaskStore` (`tests/Atv.LogicTests/Store/FakeAppTaskStore.cs`) promises
to mimic exactly four real-platform behaviors — no more, gated by "a
specific logic test would test the wrong thing without it." Three of the
four have fully automated confirming checks (unit + adapter tests, already
part of the default suite). Two are manual — this checklist's dark matter:

### 3a. The 4×100 clobber run (promise 1: non-atomic whole-store clobber)

Confirms the real platform still has no cross-process write serialization —
`tasks.json` writes from concurrent processes still clobber each other
(last-writer-wins on the whole file) absent the `WriteGate` mutex. Run:

```
tests/Atv.AdapterTests/PeriodicClobberTests.cs
```

— excluded from the default adapter run (gated off). Empirical baseline
(Windows 11 26100, 2026-07-02): 4 processes × 100 creates each, unlocked,
kept only 37/400; the same test through the mutex kept 400/400. Re-running
should reproduce "well under 400 survive unlocked, 400/400 locked" — the
exact surviving count isn't the contract, only that loss still happens
without the mutex and doesn't with it.

**If something changed** (e.g. a new Windows build serializes writes at the
OS/API level and the unlocked run now keeps ~400/400 too): re-read
`docs/testing/fake-fidelity-promises.md` promise 1 and
`docs/windows-ui-shell-tasks/README.md`'s "Concurrency: writes are not
serialized across processes" section — both would need updating, and the
`WriteGate` mitigation (still correct, just possibly no longer load-bearing)
would need a note explaining it's now redundant-but-harmless rather than
removing it outright (removing a mutex on a hunch that one build happens not
to need it is a bad trade against re-regression risk).

### 3b. The `HiddenByUser` real-gesture check (promise 2)

Confirms the real Shell's taskbar-icon **X** dismiss button still sets
`AppTaskInfo.HiddenByUser = true` without removing the task from
`FindAll()`/`tasks.json` — i.e., that a hidden task still needs the app (our
ERGO-2 sweep) to notice and `Remove()` it, or it lingers forever. This is
the one fidelity promise with no automated check at all — no automated
harness can drive a real Shell click.

**How to re-check:** `atv start x --title "Hide me"`; eyeball the card on
the taskbar; click its **X** dismiss button in the flyout; confirm (a) the
icon disappears from the taskbar immediately (Shell-side) and (b) `atv list
--json` still reports the handle (i.e. `HiddenByUser` is now `true` but the
task is still live) until the next sweep-triggering verb (`start`/`remove` on
any handle) or the watchdog's own reconciliation removes it.

**If something changed** (e.g. the Shell now auto-removes on hide, or no
longer surfaces `HiddenByUser` at all): update
`docs/windows-ui-shell-tasks/AppTaskInfo.md`'s `HiddenByUser` row,
`docs/testing/fake-fidelity-promises.md` promise 2, and
`FakeAppTaskStore.SetHiddenByUser`'s doc comment together.

## Also worth a glance while you're in there

Not a fourth "promise," but adjacent dark matter worth a look on the same
pass: `AppTaskInfo.IsSupported()`'s `CLASS_E_CLASSNOTAVAILABLE` behavior
(`Capability.Check`, wrapped per INFRA-13) — confirm it's still either a
plain `true`/`false` or the same documented COM exception on the new build;
the wrapper doesn't handle a third failure shape.

## Summary table

| # | What | Automated? | Where the data lives | Where to re-check |
|---|---|---|---|---|
| 1 | State × content crash matrix | Partially — safe cells only, no crash-reproduction test | `SafeCombinationMatrix.cs` + `state-content-compatibility.md` | Manual taskbar eyeball, all 7 safe cells |
| 2 | Grouping keyed on `IconUri` | No | `windows-ui-shell-tasks/README.md` (prose) | Manual: same-icon merge vs. different-icon split |
| 3a | Non-atomic clobber / last-writer-wins | Yes, but gated off by default | `fake-fidelity-promises.md` promise 1 | `tests/Atv.AdapterTests/PeriodicClobberTests.cs` (opt-in run) |
| 3b | `HiddenByUser` real gesture | No | `fake-fidelity-promises.md` promise 2 | Manual: real taskbar **X** click |
