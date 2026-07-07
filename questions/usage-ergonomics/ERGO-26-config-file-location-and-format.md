# ERGO-26: Config file location and format
**Status:** DECIDED
**Decision:** Config lives in package app-data (`ApplicationData.Current.LocalFolder`); format is
JSON via `System.Text.Json` (source-generated). TOML was preferred for editability but rejected:
no in-box .NET TOML support, and a TOML library conflicts with INFRA-2 ("Minimizing the on-disk
size") -- `System.Text.Json` is in-box, AOT-safe, and adds ~zero size.

Detail (2026-07-05):
- Location -- package app-data, for automatic per-pool isolation (DIST-3, "Dev vs release identity
  (PFN) divergence"): release, dev-interactive, and each test worktree get separate config, so a
  dev's personal config can never perturb a test run (INFRA-16, "Test-time identity provisioning
  and deep isolation") with zero env-pin plumbing. Accepted trade-offs: the path
  (`%LOCALAPPDATA%\Packages\<PFN>\...`) is edit-hostile, and config vanishes on uninstall (DIST-9,
  "Uninstall behavior with live tasks") -- the latter is fine (clean uninstall).
- Because the path is non-obvious, the tool must surface it (e.g. `doctor` / a `config path`
  helper) so a human can find the file to edit -- exact surface folds into ERGO-27 ("The
  consolidated v1 command surface").
- Format -- JSON (`System.Text.Json`, source-gen for AOT/trim + zero-cost). Operator constraint
  (2026-07-05): TOML only if in-box; it isn't, and no bloat library -- so JSON.
- File/dir names derive from the ERGO-18 ("The shipped command name") brand constant (standing
  requirement).

ERGO-17 ("Configuration surface for recurring defaults") decided the precedence chain
(flags > env > config file > built-in default) but not where the config file LIVES or its
format. Location is design-relevant, not build detail, because of the identity pools
(DIST-3, "Dev vs release identity (PFN) divergence"):
- Package app-data (`ApplicationData.Current.LocalFolder`) inherits per-pool isolation --
  release, dev-interactive, and each test worktree see DIFFERENT config. Good for test
  hermeticity (INFRA-16, "Test-time identity provisioning and deep isolation" -- a dev's
  personal config can never perturb a test run), but the user-facing path
  (`%LOCALAPPDATA%\Packages\<PFN>\...`) is hostile to hand-editing and vanishes on
  uninstall (DIST-9, "Uninstall behavior with live tasks").
- A conventional shared path (`%APPDATA%\<brand>\...`) is discoverable and durable, but
  leaks one config across ALL pools -- tests would need an explicit ignore-config /
  env-pinned mode to stay hermetic.

Also decide the format (TOML / JSON / INI -- agents and humans both edit it), and confirm
file/dir names derive from the ERGO-18 ("The shipped command name") brand constant
(already a standing requirement).

Surfaced by the 2026-07-05 review pass.
