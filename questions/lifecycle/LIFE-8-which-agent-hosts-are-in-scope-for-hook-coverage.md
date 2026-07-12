# LIFE-8: Which agent hosts are in scope for hook coverage
**Status:** DECIDED
**Plan:** phase-13
**Decision:** v1 targets exactly three hosts -- Claude Code, GitHub Copilot CLI,
Codex (Codex lowest priority). Criterion for adding a host later: (a) it exposes
a usable hook/notification surface the host-agnostic CLI can wire to without
host-specific logic (LIFE-10) -- absent that, it is a LIFE-3 wire-transport
problem, not a cheap add; and (b) enough demand to justify maintaining an
integration artifact (LIFE-11).
**Parent:** LIFE-2

Claude Code, GitHub Copilot CLI, and Codex are named; 'etc.' is unbounded.
Which hosts do we explicitly target for v1, and what is the criterion for
adding more later?
