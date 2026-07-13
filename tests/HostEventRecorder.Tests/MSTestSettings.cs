// Each test owns its own TempDirectory (unique per instance) or an
// explicitly isolated Mutex/file, so method-level parallelism is safe by
// construction -- mirrors tests/Atv.LogicTests/MSTestSettings.cs.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
