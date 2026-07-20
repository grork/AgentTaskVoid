# INFRA-33: Safe, known-state dev/agent runs — validate through `dotnet run`, tear down explicitly
**Status:** DECIDED (2026-07-19)
**Plan:** unplanned
**Decision (one line):** Validate against the real app through `dotnet run` (it re-registers your
current build, so no version skew), never through a bare alias; use the fake-backed logic suite
when you don't need the taskbar; a real-taskbar test uses a throwaway identity you register and
tear down explicitly, because cleanup can't be automatic.

## Facts established (verified 2026-07-19 against the tools, code, and docs)
- **`dotnet run` and `winapp run` are the same action.** The `Microsoft.Windows.SDK.BuildTools.WinApp`
  package redirects `dotnet run` (and F5) into `winapp run`. One operation, not two.
- **What registers a package (persistent machine state):** `dotnet run` / `winapp run` register;
  `dotnet test` on `tests/Atv.AdapterTests` registers a per-worktree test package. `dotnet build`,
  and `dotnet test` on `tests/Atv.LogicTests`, register nothing. Invoking a bare alias registers
  nothing — it launches the already-registered package.
- **`dotnet run` re-registers the current build every invocation** (`winapp run --help`: "Creates
  packaged layout, registers the Application, and launches"), so a run always launches the build
  you just made. (Exception: the first run in a brand-new clone launches unpackaged.)
- **Version skew:** `atv --version` is the version compiled into the exe (`Program.cs`); `atv
  doctor` is the registered package's PFN version (`DoctorVerb.cs`). A plain `dotnet build`
  restamps and rebuilds but does not re-register, so after a build-without-run the alias still runs
  the previously-registered build and the two numbers diverge.
- **`--unregister-on-exit` exists on `winapp run`** ("Unregister the development package after the
  application exits. Only removes packages registered in development mode"). **There is no MSBuild
  property that makes `dotnet run` pass it** — the WinApp targets map properties only for
  `--with-alias`, `--no-launch`, `--debug-output`, `--args`. To use it you call `winapp run`
  directly with the flag.
- **Unregistering the package drops the taskbar cards** (DIST-9). `atv <verb>` creates its card and
  exits in under a second, so `--unregister-on-exit` drops the card almost immediately and keeps no
  state across invocations — it cannot observe a persistent card.
- **`atv doctor` / `list` / `--version` are read-only** (no watchdog spawn, no writes).
- **`atv-dev` does not exist yet** (its phase is unbuilt). When a dev package is registered, bare
  `atv` is the dev-interactive build's alias — the operator's live install.

## Decision (the rules)
1. **Behavior check, no taskbar needed → `tests/Atv.LogicTests`.** Real `IconService`/`SemanticEngine`
   over the fake store, temp dirs, no identity, no registration. The default for agent-driven checks.
2. **Right version → validate through `dotnet run`, not a bare alias.** `dotnet run` re-registers the
   current build; a bare alias can run a stale one, and may carry the operator's live cards. This is
   the fix for the version skew, and it belongs in `CLAUDE.md`'s Dev-loop section.
3. **Real-taskbar test → register a throwaway identity you control, then tear down explicitly.** Use
   the `-reltest` variant (`docs/release.md` §3, alias `atv-reltest`, its own Name/PFN) or a one-off
   `winapp run` register — never the operator's `atv`. Registering guarantees the just-built version.
   Drive and observe the cards across as many invocations as needed, then remove the package by its
   exact Name (`Remove-AppxPackage`, which drops the cards). Never a bare `*Codevoid.AgentTaskVoid*` wildcard.
   Cleanup is a deliberate teardown; it can't be automatic (see rule 4).
4. **`--unregister-on-exit` is only for a self-contained programmatic check that leaves no residue** —
   one process that creates and asserts within its own lifetime. It cannot watch a card or hold state
   across invocations, because unregistering on exit drops the card.

## The incident this resolves
An orchestrator told a sub-agent to run `dotnet run … doctor` to reach an isolated `atv-dev` alias.
`atv-dev` does not exist yet, and the orchestrator did not know `dotnet run` installs a package, so
it could not predict the machine-state effect. Forensics: the sub-agent ran only `dotnet build`
(0.1.73) + `dotnet test`; the 0.1.71 registration it observed pre-existed and was not created by the
run. Under these rules the check would have used the logic suite (rule 1, nothing installed), and any
real-taskbar need would have gone through `dotnet run` on a throwaway identity with explicit teardown
(rules 2–3) — right version, cleaned up, never the operator's `atv`.

## Correction note
An earlier draft of this decision proposed a `winapp run … --unregister-on-exit` "ephemeral gate" as
the standard way to do live taskbar validation. That is wrong: unregister-on-exit drops the card on
process exit, so it cannot observe a persistent card (see fact above). Rules 3–4 replace it.

## Build-time details / known caveats (not blockers)
- A live watchdog holding the exe can make Windows defer (un)registration (`ERROR_PACKAGES_IN_USE`,
  DIST-6) — a teardown or re-register may not take until the watchdog exits.
- Whether `winapp run` registers the `bin/` layout in place or a copy is unconfirmed; either way the
  registered PFN version is the authoritative "what is registered" number.

## Relationship
Builds on INFRA-17 ("Dogfood/run ergonomics without a load-bearing script") and INFRA-19/20
(inner-loop watchdog handling). Feeds phase-20 (DIST-12): its live acceptance steps should validate
through `dotnet run` and assert the registered version matches the build under test. Related to
DIST-13 ("Easy-to-use scripts…").
