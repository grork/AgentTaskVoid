# LIFE-17: Watchdog spawn mechanics
**Status:** DECIDED
**Decision:** Every write-path invocation (`start`/`step`/`state`/...) ensures a watchdog is
live and spawns one only if absent. The liveness check is a cheap `OpenMutex` on the LIFE-18
("Watchdog single-instance enforcement") named object, placed in the INVOKER as the primary
gate -- so a busy session's stream of `step`s never spawns a doomed process per call -- with
the spawned watchdog's acquire-or-exit as the correctness backstop for the check->spawn race.
Process = the same `atv` exe in a hidden `watchdog` mode (one binary, per DIST-1, "The
end-user distribution vehicle"), spawned windowless (`CreateNoWindow`) and detached (new
process group, survives the parent exiting). INTER-1's ("What receives Shell activations")
console-flash finding does NOT apply to a CLI-spawned child -- we control its spawn flags; it
only bites an OS-launched process (e.g. LIFE-20's ("Logoff/reboot recovery") startup task).
Spawn failure is non-disruptive (FAIL-1, "Failure posture toward the host caller"): log +
continue; cleanup falls to the next invocation.
**Parent:** LIFE-6

Who spawns the watchdog and when (first `start`/create for a given LIFE-16 scope?);
what process it is -- the same `atv` exe in a hidden watchdog mode vs a second exe
(DIST-1's single-MSIX packaging favors fewer exes; INTER-1's finding that a console
exe flashes a terminal window applies if the watchdog ever doubles as the activation
receiver); how the launch detaches (survives the parent CLI exiting, spawns no
console window of its own); and spawn failure staying non-disruptive per FAIL-1
("Failure posture toward the host caller"). Watchdog-mode debuggability (launch
profiles) is already raised in INFRA-18.
