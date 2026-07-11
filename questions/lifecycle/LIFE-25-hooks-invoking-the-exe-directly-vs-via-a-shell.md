# LIFE-25: Should host hooks invoke the `atv` exe directly instead of via a PowerShell wrapper?
**Status:** OPEN

## Question
The shipped Claude Code hooks run a **PowerShell one-liner** that reads the host's JSON payload
from stdin, extracts fields (`session_id`, `tool_name`, `message`, …), and then calls `atv`.
Should the hook instead invoke the `atv` executable **directly** (exec form, an `args` array,
no shell) — fewer moving parts, no PowerShell startup per hook, no shell-selection footgun?

## Why this surfaced
Operator, 2026-07-10, reviewing the phase-13 artifact. Two phase-13 findings motivate it: (a)
each hook must set `"shell": "powershell"` because this machine has Git Bash, so the default
hook shell is `bash` and the PowerShell one-liner would otherwise fail — a portability
footgun; (b) every hook spins up a full PowerShell process. The async hook flag hides the
latency from the session, but it's still process churn on every tool call.

## What makes it non-trivial (constraints)
- **Something must parse the host's stdin JSON and map fields → `atv` args.** Today PowerShell
  does that inline. To call `atv.exe` directly with a *fixed* args template, the dynamic
  fields (session id, tool name, message) can't be pulled from the payload — unless **`atv`
  itself** reads the host payload and extracts them. Putting host-specific payload parsing into
  `atv` violates the load-bearing invariant that the CLI carries **no host-specific logic**
  (LIFE-10, "The host-agnostic CLI abstraction hook events map onto"; LIFE-11) — all host
  specifics live in the artifact.
- **The `atv` alias is itself a launcher stub.** On PATH, `atv` resolves to the
  `AppExecutionAlias` stub, not a raw exe path; "call the exe directly" still goes through the
  alias/activation. A true bypass would need the package install location, which is not stable
  to hard-code.
- **Shell alternatives each have a cost:** bash-form would need a JSON parser (`jq` may not be
  installed); `cmd` can't parse JSON; keeping PowerShell keeps the startup cost but is
  self-contained.
- **`atv` is NativeAOT** — adding a generic, host-agnostic "hook adapter" subcommand is
  feasible, but it must express the mapping *declaratively from the artifact* (which JSON
  path → which verb/arg), not bake in any one host's schema, to stay within LIFE-10.
- **Exit-0 posture (FAIL-1)** must survive whichever mechanism — a fail-closed host must never
  see a nonzero from the hook.

## Options to explore later (NOT deciding now)
1. Status quo: per-host PowerShell (or host-native shell) wrapper does the parsing; async hides
   latency. Simple, self-contained, already verified live.
2. A **host-agnostic `atv hook` adapter subcommand** that reads stdin JSON + a *declarative*
   field-mapping supplied on the command line by the artifact (e.g. `atv hook start
   --id-from session_id --title-from … `), so `atv` does the parsing generically and the
   host-specific field names stay in the artifact's args — direct-exec, no shell, invariant
   intact. (Effectively moves the JSON-plucking from PowerShell into a generic `atv` mode.)
3. A tiny per-host compiled shim exe that parses that host's payload and calls `atv` — direct,
   fast, but a new build/dist artifact per host.
4. Keep the shell wrapper but standardize the shell portably and minimize its work.

## Scope note
Filed OPEN (operator, 2026-07-10); does not change the current build (the PowerShell wrapper is
verified working). Weigh option 2 carefully against LIFE-10's "no host-specific logic in `atv`"
invariant — it's the crux. Related: LIFE-10/11 (host abstraction + artifacts), LIFE-12/13/14
(host inventories), FAIL-1 (exit-0 posture), and the "basic hook" ergonomics behind ERGO-30.
