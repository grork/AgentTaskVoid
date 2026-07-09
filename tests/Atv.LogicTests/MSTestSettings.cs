// The fake-backed logic suite runs everywhere, always, in parallel
// (INFRA-11, "Test strategy for machines where the API is unavailable"; INFRA-9,
// "Integration-test harness over tasks.json") -- each test method owns its own
// FakeAppTaskStore instance, so method-level parallelism is safe by construction.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
