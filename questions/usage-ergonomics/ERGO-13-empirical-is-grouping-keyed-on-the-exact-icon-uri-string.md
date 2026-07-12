# ERGO-13: Empirical: is grouping keyed on the exact icon URI string?
**Status:** DECIDED
**Plan:** phase-07
**Decision:** Operator assertion (2026-07-02): grouping is keyed on the icon URI
string/path, NOT image bytes. So byte-identical icons at two different paths
produce two separate taskbar icons -- the CLI can force separation while keeping
identical visuals by copying one icon to per-group paths. Not probed this
session; if a future build behaves differently, that is INFRA-13/INFRA-10.
**Parent:** ERGO-4

Grouping is keyed by IconUri, not Title (docs README). Is the key the URI
string/path itself -- i.e. do byte-identical icon files at two different paths
produce two separate taskbar icons? If yes, the CLI can force separation while
keeping identical visuals by copying an icon to per-group paths.
