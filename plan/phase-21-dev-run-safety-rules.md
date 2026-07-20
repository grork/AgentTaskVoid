# Phase 21: Dev-run safety rules in the docs

**Depends on:** no build output of any phase (doc-only). Sequenced immediately after
phase 20 because the rules must be written against the **post-phase-20 pool map** (bare
`atv` = the operator's installed retail daily driver, `atv-dev` = the working copy) — and
because phases 22–23 run live verification on a machine that now carries that daily
install, so these rules need to be in `CLAUDE.md` before their executors start.
**Unblocks:** nothing structurally. Phases 22–23's LIVE acceptance criteria cite these
rules as their conduct contract.

## Goal

Encode INFRA-33's dev-run rules into `CLAUDE.md` so a fresh session (human or agent) can
predict the machine-state effect of every dev command and never validates against a stale
build or the operator's live install. The facts are already established and verified
(2026-07-19, against the tools/code/docs); this phase writes the missing rules down where
sessions actually read them.

## Decisions implemented

### INFRA-33 ("Safe, known-state dev/agent runs — validate through `dotnet run`, tear down explicitly")

The decision is four rules plus supporting facts. `CLAUDE.md` **already carries** part of
it (from the fact-establishing pass that preceded the decision): the machine-state effects
table, the "validate through `dotnet run`, not a bare alias" paragraph with the
version-skew explanation (`--version` = compiled, `doctor` = registered), and the "use
`tests/Atv.LogicTests` when you don't need the taskbar" line. Do not duplicate those —
tighten in place if needed.

**What is missing and must land (the substance of this phase):**

1. **Rule 3 — real-taskbar tests use a throwaway identity with explicit teardown.** A live
   taskbar check registers an identity the tester controls — the `-reltest` variant
   (`docs/release.md` §3, alias `atv-reltest`, its own Name/PFN) or a one-off `winapp run`
   registration — **never the operator's `atv`** (and, post-phase-20, never a
   state-changing verb against bare `atv` at all — that discipline is phase 20's; cite it,
   don't restate it). Registering guarantees the just-built version. Drive and observe
   cards across as many invocations as needed, then tear down deliberately:
   `Remove-AppxPackage` by the **exact Name** — never a bare `*Codevoid.AgentTaskVoid*`
   wildcard (it would sweep the retail/dev/test pools too; derive the text from the brand
   constant per standing invariant #2, don't hand-type it). The removal-drops-cards fact
   is already in `CLAUDE.md` (the DIST-9 sentence under the machine-state table) — link
   the teardown guidance to that sentence, don't restate it. Cleanup cannot be automatic
   (see rule 4's limits).
2. **Rule 4 — `--unregister-on-exit` is only for self-contained programmatic checks.**
   `winapp run --unregister-on-exit` exists, but (a) **no MSBuild property passes it
   through `dotnet run`** — the WinApp targets map only `--with-alias`, `--no-launch`,
   `--debug-output`, `--args`, so it requires invoking `winapp run` directly; and (b)
   unregistering drops the package's taskbar cards immediately, so it **cannot observe a
   persistent card or hold state across invocations** — it fits only a one-process
   create-and-assert check that must leave no residue.
3. **The `ERROR_PACKAGES_IN_USE` caveat:** a live watchdog holding the exe can make
   Windows defer (un)registration (DIST-6) — a teardown or re-register may not take until
   the watchdog exits. Keep it one brief DIST-6-cited sentence near the teardown
   guidance; the full story stays in `docs/release.md`.
4. **Reconcile the pre-existing dev-loop prose with the post-phase-20 pool map.** The
   existing validate-through-`dotnet run` paragraph still describes a bare alias as "a
   worktree's own alias" that, on a machine with live dev cards, "drives the real
   taskbar" — after phase 20 the working copy's alias is `atv-dev` and bare `atv` is the
   retail daily driver. Phase 20's `CLAUDE.md` edit is scoped to the Package-identity
   section and deliberately leaves this paragraph alone, so **this phase owns the
   rewrite**: restate the paragraph's alias language against the four-pool map (its
   version-skew and build-does-not-re-register facts are unchanged and stay).

## Files affected

```
CLAUDE.md    # Dev-loop section: rules 3–4 + the ERROR_PACKAGES_IN_USE caveat; rewrite the stale bare-alias prose (item 4)
```

No code, no tests, no other docs. `CLAUDE.md` is governed by the doc-style rulebook —
read `.claude/skills/doc-style/SKILL.md` before editing and follow it.

## Acceptance criteria (written first)

1. **Rule 3 present.** `CLAUDE.md`'s dev-loop material states that a real-taskbar test
   registers a throwaway identity (`-reltest` or one-off `winapp run`), never drives the
   operator's `atv`, and tears down by exact Name — with the no-bare-wildcard warning.
2. **Rule 4 present.** `--unregister-on-exit` documented with both verified facts: not
   reachable through `dotnet run` (no property mapping — direct `winapp run` only), and
   drops cards on exit so it cannot verify anything persistent.
3. **Caveat present.** The `ERROR_PACKAGES_IN_USE` / live-watchdog deferral note appears
   once, as a brief DIST-6-cited sentence near the teardown guidance.
4. **Consistent with phase 20 — including the pre-existing prose.** The text reflects the
   four-pool alias map (`atv` retail daily / `atv-dev` working copy / `atv-reltest` /
   per-worktree test) and points to — rather than restates — phase 20's
   never-drive-bare-`atv` discipline. **No sentence anywhere in `CLAUDE.md` still
   describes bare `atv` as a worktree/dev alias or as something dev work drives**
   (grep-provable; the item-4 rewrite is a firm deliverable, not an "if needed").
5. **No duplication.** The existing machine-state table and validate-through-`dotnet run`
   paragraph are extended or referenced, not repeated; each fact appears once
   (doc-style rule).
6. **Doc-only diff.** No source, test, build, or manifest file changes; solution still
   builds and the logic suite still passes untouched.

## Out of scope

- Any code or verb change; any new script or MSBuild target.
- The phase-20 migration checklist itself (its INFRA-33 version-skew assertion was folded
  into `plan/phase-20-daily-driver-identity-and-plugin-override.md` at planning time).
- DIST-13's kit documentation (phase 23 owns its own READMEs).
- Re-litigating the rejected "ephemeral gate" (`--unregister-on-exit` as the standard live
  path) — INFRA-33's correction note already retired it.
