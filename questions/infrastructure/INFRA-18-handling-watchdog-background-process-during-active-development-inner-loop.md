# INFRA-18: Handling 'Watchdog' background process during active development & inner loop
**Status:** EXPANDED
**Expanded into:** INFRA-19, INFRA-20, INFRA-21

Expansion (ratified 2026-07-04): bundled several independent suppression mechanisms,
and the last paragraph buried a second question -- debuggability of watchdog mode
itself. Parameterized by LIFE-16 ("Watchdog granularity / scope") and LIFE-17
("Watchdog spawn mechanics").

In production scenarios, and background watchdog/sweeper will kick in and clean things up
(apptaskinfo, sidecars). However in *development* scenarios, this is going to be problematic.
Fast inner loops will spawn them *all over the place* causing inner loop problems with locked
exe's etc. In the case of F5 debug sessions, you'll terminate the process with no clean shutdown
(shift-f5) that might have allowed an orderely shutdown of the watchdog. What is the process for
cleaning these up as part of those scenarios?

Some possible -- not exhaustive, there may be more:
- a command line switch or other config that disables the watchdog, and potentially brings it 'inproc'
- detecting a debugger is attached and not spawning the watchdog or bringing it in proc?
- configurable idle-expiry time / forced short when debugger is attached
- Leaning on some of the watchdog behviour that needs to actively shutdown when the sidecar is removed
  naturally as part of the task being remove by `atv remove`.

These also lead to the question of 'ok, if we do any of those, how do I debug the actual watchdog mode'?
There may be value in leveraging the VS/VS Code/Dotnet "Launch Profiles" / launchsettings.json. Consider
what options approaches might be relevant here to ensure an easily-debuggable watchdog process mode.
