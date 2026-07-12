# INFRA-24: Recorder tool architecture & repo placement
**Status:** DECIDED
**Plan:** phase-14
**Parent:** INFRA-23
**Decision:** A compiled C# console tool at `tools/host-event-recorder/`, a member of
`AppTaskInfoCli.slnx` but referencing no atv project and carrying no brand/identity
coupling; NativeAOT single-file exe, no package identity; invoked per event with host
tag + event name on argv and the raw payload on stdin.

## Question
Where does the host-event recorder live and how is it built/run?

Operator direction (2026-07-11, discovery):
- **Discrete tool, no `atv` dependency or brand coupling** — clarified in the answer
  session (2026-07-12): the point is **not** "runs on machines where atv is absent"; it
  is **"don't build this INTO atv."** No inter-dependency in either direction — the
  recorder neither references atv's code/brand/identity nor is referenced by it.
- **Capture-only for v1** — no query/tail/analyze surface bundled into the recorder
  itself; analysis stays ad hoc (jq/PowerShell over JSONL, per INFRA-25's schema) rather
  than a recorder feature. Revisit only if that friction proves real.
- **Shared infra, separate per host** resolves to: **one shared recorder core** (a single
  exe that does the actual append) invoked by **per-host hook configuration** — only the
  *conduit* (how each host's hooks are registered/wired, necessarily per-host per LIFE-24's
  conduit/translator/engine split) differs per host. The write path is one piece of shared
  code, not N reimplementations.

## Decision (operator + Claude Code, answer session, 2026-07-12)
1. **Form: compiled C#, not a script.** The clarified intent (discrete/separate, not
   atv-less-machine support) removes the "in-box PS-5.1 only" pressure that governs the
   *translators* (LIFE-24). Compiled C# is chosen for exact control over UTF-8/verbatim
   bytes — killing the PS-5.1 mojibake risk LIFE-24 called out — and a clean
   `System.Threading.Mutex`-guarded append (INFRA-25). The compiled choice and the mutex
   choice reinforce each other.
2. **Placement: `tools/host-event-recorder/`, own `.csproj`, a slnx member.** In the
   solution so it builds with `dotnet build` and cannot rot (INFRA-29 wants it durable/
   maintained) — matching the `tools/Atv.TestIdentityTool` placement precedent — but it
   **references no atv project**. Repo-wide `Directory.Build.props` (warnings-as-errors,
   NBGV versioning, inert AOT levers) is shared build hygiene, not product coupling; the
   tool simply never consumes `$(AtvBrandName)`.
3. **No brand coupling, non-atv name.** The project is named to signal separateness (a
   plain name like `HostEventRecorder`, **not** an `Atv.*` prefix). No `Branding.cs`-style
   parameterization — it has no identity, alias, or package to rebrand.
4. **No package identity.** It never touches `AppTaskInfo`, so it needs no MSIX/manifest/
   `winapp` — a vanilla console exe. The absence of identity machinery *is* part of the
   separation.
5. **Runtime form: NativeAOT single-file.** A drop-in native exe (no .NET prereq on a
   capture box) with a fast cold start — a mild but genuine plus for a spawn-per-event
   tool. The AOT friction is not meaningful here: the `vswhere`/MSVC toolchain (CLAUDE.md)
   is already required and set up for atv's own release build on the same box; AOT runs
   only on explicit `dotnet publish -p:PublishAot=true`, so the dev loop and solution build
   stay a normal managed build. The only real constraint is keeping the code AOT-safe —
   source-gen JSON (`JsonSerializerContext`, no reflection), trivial for the fixed 6-field
   envelope whose `payload` is an escaped string.
6. **Invocation (cheap, per-event).** Host tag + event name ride **argv** (static per
   hook, e.g. `host-event-recorder --host claude-code --event PostToolUse`); the raw
   payload arrives on **stdin**, read as raw bytes and decoded UTF-8. Same conduit shape as
   LIFE-24's translators; INFRA-25 owns the envelope the core then writes.
