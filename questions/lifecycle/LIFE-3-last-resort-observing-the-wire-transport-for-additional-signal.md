# LIFE-3: Last resort -- observing the wire transport for additional signal
**Status:** DEFERRED
**Deferred:** Not needed -- hook coverage proved sufficient. The host research
(LIFE-12/13/14) showed all three in-scope hosts expose good hook systems (session
ids, tool events, needs-user events); only session-end is weak, which the watchdog
(LIFE-4) handles without touching the wire. Revisit only if a future in-scope host
has NO usable hook surface (the LIFE-8 add-a-host criterion) -- and even then it is
an explicit last resort.

If none of the hooks give us a sufficiently broad view and we end up with weird
hacks, should we consider (oh god) also sitting on the wire transport to the
backend, and building knowledge of those transactions to add more signal? (This
seems very bad, and only a last resort if any other hook-based approach is
untenable.)
