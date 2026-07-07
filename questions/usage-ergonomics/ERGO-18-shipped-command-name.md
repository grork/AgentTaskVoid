# ERGO-18: The shipped command name
**Status:** DECIDED
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
