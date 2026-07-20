# ERGO-18: The shipped command name
**Status:** DECIDED
**Plan:** all-phases
**Decision:** Command/binary name: `atv`. Product/brand: "Agentaskvoid" (a
contraction of "agent task" + the operator's standard "void" suffix). Not a
one-way door -- reversible later. Hard requirement: the brand is PARAMETERIZED
through the system (single source of truth), not baked into many locations, so a
future rename is cheap.

AppTaskInfoCli is a working name. The command name is what every hook config
and script embeds, so it needs deciding before integration artifacts (LIFE-11)
or docs exist. What is the tool called, and what does the user type?

Decision detail (2026-07-02): the brand surfaces in many places -- package
identity, command name, config file/dir + env-var names (ERGO-17), sidecar + log
paths, integration-artifact names, and the (deferred) URI scheme (INTER-1). All
must derive from one central brand constant so a rename touches one place. Treat
this as an architectural constraint on code structure (relates to INFRA-8 and the
general build-out), not merely a label.

Amendment (2026-07-19): rebranded, exercising the reversibility above. Package
identity name (and winget package id): `Codevoid.AgentTaskVoid`. Human-facing
product name: "Agent Task Void". Root namespace: `Codevoid.AgentTaskVoid` (was
`Atv`). Command stays `atv`; file/folder/project/MSBuild names keep their `Atv`
spelling (command-derived, not brand-derived). `Branding.cs` split its single
`Name` constant into `IdentityName` + `DisplayName` to carry the identity/display
distinction. This paragraph is deliberately the only place in the repo that
records the superseded names -- brand/identity "Agentaskvoid", winget id
"Agentaskvoid.Atv", manifest Application Id "Agentaskvoid" (now "Atv"), startup
TaskId "AgentaskvoidBootRecovery" -- so a search for the old name lands here.
Every other file, including history-of-record docs, was mechanically swept to the
new names on 2026-07-19; pre-rebrand text is verbatim in git history.
