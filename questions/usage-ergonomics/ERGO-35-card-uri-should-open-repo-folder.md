# ERGO-35: Card URI opening the ATV folder is confusing to the user; it should open to the repo root
**Status:** DECIDED (2026-07-19)
**Plan:** phase-22
**Amends:** ERGO-24 ("The default deepLink URI value") — its default value (a `file:` URI to the
tool's app-data `LocalState`) is superseded by the anchor-derived value below. ERGO-24's phase-08
stamp stays; this is a new decision on the same surface, not a re-opening.

**Decision:** The built-in default `deepLink` becomes a `file:` URI to **the resolved anchor
directory** — the folder the session is anchored in (`--cwd`, i.e. `${CLAUDE_PROJECT_DIR}` for
Claude Code, else process cwd) — instead of the tool's app-data folder. Clicking a default card
opens Explorer where the work actually is, not the diagnostics folder.

- **Anchor dir, not repo root** (operator, 2026-07-19). The concern that pushed the original
  filing toward "repo root — which we can reliably discover" was cwd ambiguity; ERGO-30 ("A
  repo-scoped defaults file the tool auto-discovers") already resolved that — the anchor is now a
  clean, reliable value. So "the folder the agent is working in" is reliably available, and it is
  strictly more useful than the repo root: for a monorepo subproject it lands you at the
  subproject (`packages/web`), not up at the monorepo root; when anchor == repo root (the common,
  single-repo case) the two are identical. No `.git` boundary is required for this — the anchor
  directory is used directly (this is where it diverges from ERGO-34's icon, which keys on the
  repo root for shared repo identity; the deep-link is about *where you land*, so it tracks the
  anchor).
- **Resolved on the create branch, from the ERGO-30 anchor.** Today the default is a
  process-global constant (`CompositionRoot` injects `new Uri(Paths.Root)` into the dispatcher's
  `_defaultDeepLink`, applied in `TryResolveDeepLink` when no `--deep-link` is passed). This moves
  the default's *resolution* into the engine's create branch, alongside ERGO-33/34's defaults,
  reusing the same already-resolved anchor — no second discovery, no hot-path cost.
- **The old app-data value becomes the floor.** When no anchor resolves (a case ERGO-30's process-
  cwd fallback makes nearly impossible), the default falls back to ERGO-24's app-data `LocalState`
  URI, so a card always has a valid, benign `file:` target.
- **ERGO-30 is unchanged.** A repo's `.atv.json` still cannot *set* the deep-link (a checked-out
  repo must not decide what your card launches). Only the ENGINE's built-in default now derives a
  folder — the tool computing a benign default, not repo-authored launch intent. The precedence
  chain stays two-layer for deep-link: `--deep-link` flag > built-in default (no env/repo/user
  layer).
- **INTER-4 ("Default deep-link click behavior") stays deferred.** This is a value-only change,
  within the same boundary ERGO-24 held: a `file:` folder URI opens File Explorer cleanly — no
  Store prompt, no "how do you want to open this", no flash (empirically confirmed for a local
  folder URI, 2026-07-05) — honoring FAIL-1's ("Failure posture toward the host caller")
  never-disrupt spirit. The full click-behavior design remains INTER-4's.

## Post-review correction (2026-07-19) — the default must survive updates, not just create
An independent review caught a structural gap in the "resolved on the create branch … no
hot-path cost" framing above: **`deepLink` is not a create-only field.** On every content-claim
update, `SemanticEngine.ApplyClaimCore` calls `_store.UpdateDeepLink(entry.Id, deepLink)`
**unconditionally** (`SemanticEngine.cs:700`) with the value the dispatcher resolved, and the
dispatcher defaults it to the app-data root whenever `--deep-link` is absent
(`Dispatcher.cs:395`). **No translator ever passes `--deep-link`.** So: `working` creates the
card with the anchor URI → the next `activity`/`blocked`/… reverts it to app-data — and the
translator fires `activity` once per tool call, so the card points at the app-data folder almost
immediately. Create-branch-only resolution does **not** persist.

**Forced fix:** thread a `deepLinkExplicit` flag from the dispatcher (mirroring the existing
`iconExplicit`). The engine resolves the anchor default at **create**, and on **update**
**preserves the live card's existing `deepLink`** when the caller passed no `--deep-link` — i.e.
skip the `UpdateDeepLink` call, exactly as `ApplyIdentityIfClaimed` already skips title/subtitle
when unclaimed. Cheap: `AppTaskView.DeepLink` is readable, so preservation needs no re-discovery
and keeps the "no hot-path cost" property. Only an explicit `--deep-link` writes on update.

**Also fold in (verified minors):**
- **Loss of ERGO-24's always-exists guarantee.** ERGO-24 pointed at app-data *because* the tool
  always writes there, so the folder exists by click time. The anchor dir can be deleted, moved,
  or offline (removable/UNC) by the time a card is clicked → Explorer "path not found," a mild
  disruption ERGO-24 never had. Accepted, but the create-time resolution should **floor to the
  app-data URI when the anchor dir doesn't exist**, keeping the FAIL-1 spirit.
- **URI-representability guard.** Floor to app-data not only when "no anchor resolves" but when
  the anchor resolves to a path `new Uri(path)` can't represent cleanly (trailing space, `#`,
  …) — the original wording floors only on the former.

## Question
Because of INTER-1/2/3 choice to defer the two-way communications stack, we have effectively a
placeholder URI currently -- and the placeholder URI points to the ATV data directory. When a
card is clicked, this URI opens, which given the destination of it (Atv data directory), this
is very confusing, and not super helpful. It should open a folder that is at least somewhat
relevant to the user -- ideally this would be the folder where the agent started, but given
conversations in the past about the 'current directory' have been real ambiguous, maybe it should
just open the repo root -- which we can reliabily discover.

## Scope note
Filed OPEN (operator, dogfooding); DECIDED 2026-07-19. Related: ERGO-24 ("The default deepLink URI
value" — the app-data default this supersedes), ERGO-30 ("A repo-scoped defaults file the tool
auto-discovers" — the `--cwd` anchor this reuses, and the rule that a repo can't set the
deep-link), ERGO-33/34 (the create-branch defaults this joins), INTER-1/INTER-4 ("What receives
Shell activations" / "Default deep-link click behavior" — the deferred two-way stack this stays
within).
