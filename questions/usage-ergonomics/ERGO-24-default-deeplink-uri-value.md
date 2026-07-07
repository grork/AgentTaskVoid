# ERGO-24: The default deepLink URI value
**Status:** DECIDED
**Decision:** Default `deepLink` = a `file:` URI to the tool's writable app-data folder
(`ApplicationData.Current.LocalFolder` / LocalState -- where the FAIL-3 log, the ERGO-26 config,
and the ERGO-21 sidecar live). Empirically (2026-07-05) a `file:` app-data URI opens File Explorer
cleanly on click -- no Store prompt, no "how do you want to open this", no terminal flash, no error
-- so it honors FAIL-1's ("Failure posture toward the host caller") never-disrupt spirit.
Consumers override per-invocation.

Detail (2026-07-05, combined DIST-9/ERGO-24 probe):
- INTER-1 ("What receives Shell activations") established there is no inert URI (unregistered
  scheme -> Store prompt). This probe confirms a registered `file:` scheme into our own app-data
  is the quiet, benign option: the operator clicked the probe card and Explorer opened at the
  folder with no prompt or flash.
- Points at LocalState (not the app-data root or SystemAppData) so a click lands on the
  diagnostics folder -- the FAIL-3 log, ERGO-26 ("Config file location and format") config, and
  ERGO-21 ("The sidecar store design") sidecar -- a coherent "what is this / where are the logs"
  default. The tool already writes there, so the folder exists by the time any card does.
- Value only; the full click-behavior design stays deferred with INTER-4 ("Default deep-link click
  behavior"). Exact path is computed at runtime from package app-data, not hardcoded.

ERGO-12 ("Defaults for parameters that are secretly required") decided the CLI always
fills `deepLink` and waved the value off as "a benign placeholder (exact value an
implementation detail)". INTER-1's ("What receives Shell activations") empirical findings
show that detail is not wavable: there is NO inert URI -- an unregistered custom scheme
(`atv://...`) pops a "look for an app in the Store" prompt on click, and any registered
scheme (https, file) visibly launches something. So the default-value question is really
"what does clicking a default card DO in v1", answered at the value level only (the full
click-behavior design stays deferred with INTER-4, "Default deep-link click behavior").

Candidates to weigh: a file/folder URI into our package app-data (opens Explorer near the
FAIL-3 log -- mildly useful, mildly weird), an https docs/readme URL (opens a browser),
or something empirically quieter if one exists. Constraints: must not error, must not
prompt, and must stay inside FAIL-1's ("Failure posture toward the host caller")
never-disrupt spirit.

Surfaced by the 2026-07-05 review pass (tension between ERGO-12's wave-through and
INTER-1's no-inert-URI finding).
