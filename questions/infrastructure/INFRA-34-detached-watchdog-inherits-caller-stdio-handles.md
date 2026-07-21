# INFRA-34: The detached watchdog inherits the caller's stdio handles — redirected callers never see EOF
**Status:** OPEN

A CLI invocation that spawns the watchdog appears to hang forever when its stdout is a pipe
rather than a console. The CLI's own work finishes and its output is printed; the caller just
never observes end-of-stream. Twice now an agent has read this as "`atv working` hangs" and gone
looking for a deadlock in the write path, which is the wrong place.

## Facts established (verified 2026-07-20 by code read)

- **`ProcessHost.Start` spawns with `UseShellExecute = false`, `CreateNoWindow = true`, and no
  stream redirection** (`src/Atv/Watchdog/ProcessHost.cs`). Under those settings the child
  inherits the parent's stdin/stdout/stderr handles — `CreateNoWindow` suppresses a console
  window, it does not detach handles.
- **The watchdog outlives its spawner by design.** It runs until the task set empties
  (`WatchdogLoop`), which can be minutes or hours after the invoking process exits.
- **Only the spawning invocation is affected.** `EnsureWatchdog.Run` opens the LIFE-18 mutex first
  and returns early if a watchdog already holds it, so no spawn happens and no handle is
  inherited. This is why the symptom is sporadic rather than universal: it lands on whichever
  invocation happens to start the watchdog, not on every call.
- **Call sites:** `CompositionRoot` wires `ensureWatchdog` into `Dispatcher` (the lifecycle verbs)
  and into `RunVerb` (`src/Atv/Cli/Verbs/RunVerb.cs:87`).
- **`ATV_WATCHDOG_MODE=off` suppresses it** at the first branch of `EnsureWatchdog.Run`. This is
  the workaround in use, and it is why `tests/Atv.AdapterTests/AssemblySetup.cs` and the dev
  launch profiles set it.

## Observed

- Post-phase-19 dogfood (2026-07-15): direct scripted CLI use from a tool-redirected terminal hung
  indefinitely; worked around with `ATV_WATCHDOG_MODE=off`. Logged as OPEN loose end #1 in
  `progress.md`. Not reproduced from a normal interactive terminal.
- Phase-20 execution (2026-07-20): an agent's `atv working` call hung ~8 minutes from a redirected
  shell before being terminated by PID.

## Hypothesis (not yet confirmed empirically)

Handle inheritance is the mechanism: the surviving watchdog holds the caller's pipe write-end
open, so the pipe never reaches EOF and the reader blocks until the watchdog exits. A console has
no EOF semantics to wait on, which would explain the interactive/redirected split. Confirming this
wants a handle enumeration against a live watchdog, not just a source read.

## The crux: is the blast radius really "agent and scripted use only"?

`progress.md` recorded the guess that hook-driven production never hits this because hooks "run
with a real terminal, not a redirected pipe." **That guess deserves direct scrutiny before it is
relied on to close this question.** Hosts generally capture hook stdout, which means hook
invocations may well be redirected too — in which case real sessions could be paying an
unnoticed cost (a hook that appears to complete but whose process handle lingers, host-side
timeouts, latency attributed elsewhere). The reason it has not surfaced as a visible hang may
simply be that hosts read with a timeout or do not wait on EOF at all.

Two sub-questions follow, and the answer to the first largely determines which option below is
right:

1. Do the real hosts (Claude Code, Copilot CLI) redirect hook stdout, and do they wait on EOF?
   The phase-14 recorder captures are the existing instrument for answering this.
2. Does `atv run` compound it? `RunVerb` both pumps a child's output and calls `EnsureWatchdog`,
   so a redirected `atv run` could inherit the problem on the invocation that spawns.

## Options

- **A — Document the workaround, change nothing.** Correct only if the blast radius really is
  agent/scripted use. Cheapest, and it keeps a working spawn path untouched. Costs: the trap stays
  live for every future agent session, and it has already burned two.
- **B — Redirect the three streams in `ProcessHost` and discard them.** Small, no new interop. Risk:
  a child that writes with nobody reading blocks once a pipe buffer fills; redirect-then-close
  trades that for write failures in the watchdog. Needs care about what the watchdog actually
  writes.
- **C — P/Invoke `CreateProcess` with `DETACHED_PROCESS` and `bInheritHandles = false`.** The precise
  fix: the child gets no inherited handles and no console. `src/Atv.IconRendering` already
  establishes CsWin32 precedent in this repo, though `src/Atv` itself does not currently use it.
  Costs: interop in the spawn path, and it must keep
  `tests/Atv.AdapterTests/WatchdogProcessHostTests.cs` meaningful (it proves a real detached spawn,
  the mutex appearing/persisting/disappearing, and self-exit on an empty set).
- **D — Auto-suppress the watchdog when stdout is redirected** (`Console.IsOutputRedirected`).
  **Listed to be rejected explicitly:** if hosts do redirect hook stdout, this silently disables the
  watchdog in exactly the production path that needs it. It would convert a visible hang into an
  invisible loss of expiry behavior.

Note that `UseShellExecute = true` is not a viable base for any option: it detaches handles but
cannot carry environment variables, and `ProcessHost` takes an `extraEnvironment` dictionary that
production and tests both use.

## Recommendation (a recommendation, not a decision)

Answer sub-question 1 first — it is cheap against the existing recorder captures and it decides
everything else. If hook-driven use is unaffected, **A** is defensible and this stays a documented
agent-discipline rule alongside INFRA-33's. If hook-driven use is affected, go to **C**: the
handle inheritance is real regardless, and B's buffer-blocking failure mode is worse in a process
meant to run unattended for hours.

## Relationship

Sits with INFRA-19 ("Inner-loop watchdog suppression") and INFRA-20 ("Reaping stale dev
watchdogs") as watchdog-lifecycle work, and complements INFRA-33 ("Safe, known-state dev/agent
runs"), which codified how agents drive the CLI safely but did not cover this failure. LIFE-17
owns the spawn mechanics this question would change.
