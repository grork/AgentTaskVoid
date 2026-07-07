# INFRA-4: Code-level testability architecture
**Status:** EXPANDED
**Expanded into:** INFRA-8, INFRA-9, INFRA-10, INFRA-11

What should testability look like at a code level -- how should we architect /
split components to enable our testability aims? If we move beyond a simple
wrapper that doesn't maintain state itself (difficult, I suspect), I want the
tests (unit, integration) etc to be vast, and cover as many scenarios as
possible. I also want to enable agents to 'hill climb' with as much non-human
intervention as possible -- sure, with the actual taskbar itself, that might be
hard, but we have `tasks.json` to verify state, et al for when we cross into
'integration' territory.
