// Deliberately sequential (not method-level parallel like Atv.LogicTests):
// every test in this suite drives the real D2D/DWrite/WIC software pipeline
// through the process-lifetime factories in Interop.cs. Those factories are
// created MTA/multi-threaded (safe for concurrent access from ANY thread),
// but running this whole suite single-threaded is simply cheaper insurance
// against COM apartment/thread-affinity surprises than proving out full
// concurrent test-runner safety buys back in a suite this small and fast.
[assembly: DoNotParallelize]
