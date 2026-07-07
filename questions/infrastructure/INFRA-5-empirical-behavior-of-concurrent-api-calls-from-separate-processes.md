# INFRA-5: Empirical behavior of concurrent API calls from separate processes
**Status:** DECIDED
**Decision:** The API does NOT serialize cross-process writes. Empirically (this
machine, Win11 26100, 2026-07-02): 4 processes x 100 concurrent `Create`s against
one identity yielded 37 of 400 tasks -- ~91% silent lost writes -- with NO file
corruption (tasks.json stayed valid JSON). Contention is on the whole shared
tasks.json (the API rewrites the entire file per op), so even different-task ops
clobber each other (last-writer-wins on the file). A global named mutex around
each write restored 400/400. Data loss, not corruption; the fix is serialization
(INFRA-6).
**Parent:** INFRA-1

What actually happens when multiple processes (each hosting OSClient.API.dll
in-proc) call Create/Update/Remove concurrently against the same package
identity? Does `tasks.json` corrupt, do updates get silently lost
(last-writer-wins), or do calls fail with errors we must surface or retry?
Needs experimentation covering both different-task and same-task concurrency.

Evidence (2026-07-02): probe harness in Program.cs (`probe burst` /
`probe burstlock`). Unlocked 4x100 -> 37/400 (each process reported 100 created,
exit 0 -- loss is silent). Locked (system-wide Mutex around Create) 4x100 ->
400/400. Unlocked bursts ran ~2.4-3.3s each (concurrent); locked ~7.2s each
(serialized: ~18ms/write) -- the lock trades throughput for correctness,
negligible at real hook write rates. tasks.json shape: {tasks:[...], version}.
Proven for Create; Update/Remove share the same whole-file-rewrite mechanism so
the behavior generalizes.
