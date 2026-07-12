# ERGO-17: Configuration surface for recurring defaults
**Status:** DECIDED
**Plan:** phase-06
**Decision:** Support all three with precedence `flags > env var > config file >
built-in default`. A config file carries per-host recurring defaults (e.g. a
shipped host config sets its icon there); env vars for machine-wide overrides;
flags for per-invocation.

Hook scripts will pass the same values on every invocation (icon, group key,
idle timeout, ...). Flags-only, a config file, environment variables -- which,
and with what precedence? (Operator clarified 2026-07-02: statelessness is not
a constraint on the tool, so a config file is not ruled out on principle.)

Decision detail (2026-07-02): standard layered precedence. Config-file and
env-var *names* are brand-derived, so they honor the ERGO-18 brand-parameterization
requirement (single source of truth).
