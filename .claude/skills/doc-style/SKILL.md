---
name: doc-style
description: Style rulebook for this repo's human-facing reference docs — README.md, CLAUDE.md, everything under docs/, and integrations/*/README.md. Read before writing or editing any of those files. Does not apply to progress.md, plan/, questions/, or brief.md (history-of-record — their narrative stays).
---

# Reference-doc style

## The stance

You are a technician writing for the next technician: someone who trusts the
tool, has a task in hand, and will follow a pointer. Nothing you write needs
to convince, impress, or prove anything. Write like an experienced, somewhat
cynical engineer who knows that simple language and easy reading beat
everything — never like a new grad performing for their boss.

## What good looks like

The bar: `README.md`, `docs/release.md`, `integrations/claude-code/README.md`,
`integrations/copilot-cli/README.md`. When unsure how a passage should read,
imitate those. Where an exemplar and a rule disagree, the rule wins — some
rules postdate the exemplar files.

A real before and after — `docs/release.md` section 2, abridged. The before
is a project-history report; the after states how the thing works today.

Before:

> ## 2. Why installing the real release msix on this dev box still isn't done here
>
> **Superseded 2026-07-10 (DIST-3 amendment, phase 12) — the collision this
> section originally warned about is fixed structurally. Kept here as the
> record of why, and of what still needs care.**
>
> A Package Family Name is `<Name>_<PublisherId>`, and `PublisherId` is a
> deterministic hash of nothing but the manifest's declared
> `Identity/@Publisher` string (Microsoft Learn, "An overview of Package
> Identity in Windows apps": "Publisher Id ... Derived from Publisher" — it
> does not depend on which certificate actually signs the package, only on
> the string declared in the manifest).
>
> **Original finding (2026-07-10, this pipeline's first real run):**
> dev-interactive and the dev-cert release were both stamped from the literal
> same `Identity/@Name` [...] computing the PublisherId hash confirmed their
> Package Family Names were IDENTICAL. [...]
>
> **Fix (ratified, see `questions/distribution/DIST-3-...`):**
> `AtvStampAppxManifest` is now build-kind-aware [...]

After (the full replacement):

> ## 2. Why the real release msix isn't installed on this dev box
>
> A Package Family Name is `<Name>_<PublisherId>`, where `PublisherId` is a
> deterministic hash of the manifest's declared `Identity/@Publisher` string,
> independent of which certificate signs the package. Dev-interactive,
> release, and `-reltest` each stamp a distinct `Identity/@Name`
> (`build/Atv.Package.targets`' `AtvStampAppxManifest` is build-kind-aware —
> see `CLAUDE.md`'s "Package identity" section and DIST-3), so all three
> produce structurally different PFNs and can coexist on one machine.

The after was written fresh from the facts, not edited out of the before.
Forty lines of history, quotes, and emphasis became two paragraphs of
current behavior, and the decision trail shrank to one pointer (DIST-3).
When a section has structural problems, rewrite it this way — sentence-level
fixes keep the old shape.

## The four rules

Few rules on purpose. The earlier approach — one rule per bad pattern found
— grew past a dozen and still leaked, because banned patterns came back in
new forms. These four are about intent; apply their tests to sentences no
list has seen. The examples under each rule calibrate the tests; they are
not coverage. Most of what the tests catch will resemble no example here.

### Rule 1: don't prove or defend the work

The reader already accepts the tool. Cut anything whose job is to convince:
evidence of how carefully something was verified, justification of a
decision's reasonableness, answers to objections nobody doing the reader's
task would raise, and words like "intentionally", "deliberately",
"by design", "explicitly".

Test: does this clause change what the reader does or expects? If it only
makes the work look good or defensible, cut it — a question ID (DIST-3,
ERGO-31) carries the rationale for whoever wants it.

- "Concurrent identical child prompts are intentionally not correlated." →
  "Concurrent identical child prompts are not correlated."
- "That marker is CORRECT, not a bug: ... Verified live 2026-07-10." →
  "That marker is expected: ..." — the explanation stays, because every
  reader sees the marker and needs it decoded; the defense and the
  credential go.

Keep: warnings about a mistake the reader is genuinely likely to make ("the
command name is `atv-reltest`, not `atv`"); caveats that say what is
untested and what to do about it; rationale the reader's own design choices
depend on.

### Rule 2: describe today, not the project

State how the thing works now. History — what changed, when, which phase,
what it replaced — lives in `progress.md`/`plan/`/`questions/`. Warning
signs: "now", "no longer", "originally", "superseded", phase or AC numbers,
"this pass/build/machine".

Test: does the sentence read the same to someone who cloned the repo
yesterday?

- "Unlike the phase-13 v1 artifact, `translate.ps1` never passes ..." →
  "`translate.ps1` never passes ...".
- "Discovered 2026-07-13 while staging the LIFE-24 empirical item 1 badge
  test." → delete; the cross-refs carry the trail.

Keep: dated observations of undocumented or unstable platform behavior —
the date tells the reader how stale the evidence is ("empirically,
2026-07-02 ..."). Keep current verification state that changes the reader's
risk ("x64 builds and is signed, but has not been verified on real
hardware").

### Rule 3: say each fact once

One owning place per fact; everywhere else, a pointer. Once a doc states a
fact, later sentences may use it but not restate or re-derive it. Don't
re-explain a term the doc set already defines.

A pointer that sends the reader somewhere is a markdown link, with the
repo-relative path as the link text (`README.md`'s existing convention:
`[docs/release.md](docs/release.md)`). A path mentioned as a fact —
"`build/Atv.Package.targets` stamps the manifest" — stays a backticked
path, no link. Question IDs (DIST-3, ERGO-31) stay plain text.

Test: if this fact changed, how many places would need the edit? And: did an
earlier sentence, or the word itself, already say this?

- "Artifacts (gitignored, never checked in)" → "Artifacts (gitignored)".
- The inline list of all 8 verbs → "the v2 semantic verb set (ERGO-31)",
  linking `docs/integration-api.md`.
- "Discovery runs only when a card is created — the first semantic-verb call
  against a handle with no live card, never on an update to an already-live
  card." → "Discovery runs when a card is created." Only-at-creation already
  implies never-on-update, card creation is defined where cards are, and the
  next sentence in that doc already covers editing mid-session.

Keep: full enumeration at the fact's own home; repeating one fact right
where forgetting it would be destructive (release.md 3.5 re-warns about the
bare wildcard next to the command that would run it).

### Rule 4: plain words, plain sentences

Bold only on things a reader scans for (keys, values, a warning), never for
emphasis — and CAPS-for-emphasis is bold too. When someone did the thing,
make them the subject ("DIST-2 rejected it", not "it was rejected").

Use the ordinary word over the impressive one. The check is speech: read
the sentence as if saying it to a colleague, and any word you would write
but not say fails — whatever the word is. "Observed" for "empirical" and
"use" for "utilize" are outputs of that check, not a list to hunt for. A
technical-sounding word earns its place only when it draws a distinction
the plain word can't. When such a word is followed by its own explanation,
keep the explanation and drop the word: "Every failure is fail-open: ...
Copilot continues normally and the integration falls back to ..." already
says everything "fail-open" was there to say. When jargon compresses a
behavior the reader needs, state the behavior instead: "if the hook crashes
or exits nonzero, Copilot blocks the tool call" beats "Copilot fails
closed".

Vocabulary has two tiers, and only one is protected. Standard engineering
terms an experienced engineer says out loud — PFN, upsert, mutex,
idempotent, fan-out, posture — are plain here and stay. Invented or
academic coinages fail the speech check no matter how consistently the repo
uses them; being established project vocabulary does not excuse a word a
competent outsider can't decode. Caught examples: "locus" → "the agent (or
main session) that raised it"; the section header "Projection legality" →
"Call verbs in any order"; "(altitude 2)" → deleted. If the source code
uses such a term internally, the docs still don't — a reader must never
need the code's private vocabulary. Section headers get the same test as
sentences.

- "One command, from repo root." → "Run from the repo root. Publishes ..."
- "decide on FRESH state, AFTER active work" → "decide on fresh state, after
  active work".
- "are empirical Copilot 1.0.71 contracts" → "are observed Copilot 1.0.71
  behavior".

Keep: actorless passives ("the file is gitignored"). Short imperatives as
runbook steps are instructions, not style.

## The last pass on every sentence

Once a sentence follows the rules, try deleting each qualifier,
parenthetical, and second verb. Keep every deletion where the reader loses
nothing they need at that point. Most sentences survive shorter; precision
belongs where the term is defined.

## Procedure per file

1. Name the reader in one line. Examples: `configuration.md` — someone whose
   setting didn't apply; `integration-api.md` — someone building a
   translator; `CLAUDE.md` — a fresh Claude session with no project history.
2. Structure read (rules 2 and 3): for each section, ask what this reader
   does with it. Delete or pointer-ize sections that only reassure; rewrite
   report-shaped sections fresh from their facts, as in "What good looks
   like"; collapse duplicated facts to their owner.
3. Sentence read (rules 1 and 4), then the last pass above. Apply the tests
   to what's in front of you; don't hunt for previously listed patterns.
4. Grep as a net afterwards, and re-read around any hit rather than
   spot-fixing it:
   `now |no longer|originally|superseded|phase[- ]?[0-9]|AC[0-9]`;
   `intentional|deliberate|by design|not a bug|explicitly`;
   `\*\*` outside tables and headings; `[A-Z]{4,}` (skip acronyms);
   ` (is|are|was|were) \w+ed by `; `genuinely|actually|naturally`.
5. Before cutting, check each rule's keep-list. Deleting operative content
   causes the same rework as leaving fluff in.
6. One file per commit. When a judgment call is genuinely unclear, keep the
   content and flag it in the commit message rather than guessing.

Doc-set notes:

- `docs/host-events/*`, `docs/windows-ui-shell-tasks/*`: dated-evidence docs
  — capture dates, host versions, and per-finding confidence are the
  content. Lightest touch.
- `docs/testing/fake-fidelity-promises.md`: the negative promises ("never
  throws") are contracts; keep them.
- `CLAUDE.md`: its reader is a session, so gotchas that prevent wasted work
  are its job.

Background on why these rules exist: [diagnosis.md](diagnosis.md).
