# INFRA-22: GUI-subsystem exe + AttachConsole for flash-free OS-launched instances
**Status:** DEFERRED
**Deferred:** A console-subsystem (`/SUBSYSTEM:CONSOLE`) exe launched by the OS -- not by us --
allocates a console and can flash a window: this bites LIFE-20's ("Logoff/reboot recovery")
boot-recovery startup item and, post-v1, INTER-1's ("What receives Shell activations")
activation receiver. The clean fix is to build the single exe as `/SUBSYSTEM:WINDOWS` and call
`AttachConsole(ATTACH_PARENT_PROCESS)` at startup so it is windowless when OS-launched yet still
a working console CLI from a terminal. Deferred: the operator has implemented this before and
found it VERY messy (stdout/stderr reattach, pipe/redirection handling, exit-code and newline
quirks), and the payoff is not needed for v1 -- LIFE-20 accepts a brief flash on the rare
crash-recovery boot, and INTER-1's interaction round-trip (the other beneficiary) is already
post-v1. Revisit when the round-trip lands or the flash becomes a real annoyance.
**Parent:** LIFE-20
