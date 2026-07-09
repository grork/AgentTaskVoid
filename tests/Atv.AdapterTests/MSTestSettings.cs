// Explicitly serial (MSTEST0001 wants this declared, not merely left as the default).
// Deliberate, unlike tests/Atv.LogicTests' [assembly: Parallelize]: every test in this
// suite shares the ONE real tasks.json for this identity (INFRA-9, "hence SERIAL") --
// parallel test methods here would clobber each other exactly like
// PeriodicClobberTests demonstrates on purpose.
[assembly: DoNotParallelize]
