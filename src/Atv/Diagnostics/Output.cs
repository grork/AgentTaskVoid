using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Codevoid.AgentTaskVoid.Diagnostics;

/// <summary>FAIL-2/ERGO-27 C5's mutating-verb `--json` shape: <c>{"ok":bool,"reason":str}</c>.</summary>
public sealed record MutatingResultDto(bool Ok, string Reason);

/// <summary>
/// FAIL-2's stdout/stderr discipline. Takes explicit writers rather than
/// touching the static <see cref="Console"/> -- the logic suite runs its
/// tests in parallel (assembly-level method-scope parallelism), so any code
/// that swapped <see cref="Console.Out"/>/<see cref="Console.Error"/>
/// globally would race across tests. A composition root (Program.cs, phase
/// 08) supplies the real <see cref="Console.Out"/>/<see cref="Console.Error"/>;
/// tests supply a <see cref="StringWriter"/>.
///
/// stdout = data (<see cref="Data"/>); stderr = diagnostics
/// (<see cref="Diagnostic"/>). Mutating verbs never call <see cref="Data"/> on
/// their happy path (FAIL-2: "no id returned") -- their only stdout output is
/// the opt-in <see cref="MutatingResult"/> `--json` shape.
/// </summary>
public sealed class Output
{
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;

    public Output(TextWriter stdout, TextWriter stderr, bool json)
    {
        _stdout = stdout;
        _stderr = stderr;
        Json = json;
    }

    /// <summary><see langword="true"/> when <c>--json</c> was passed. Exit code is governed separately by <see cref="Posture"/>/<c>--strict</c> -- orthogonal concerns.</summary>
    public bool Json { get; }

    /// <summary>Human-readable data line to stdout (e.g. one `list` row). No-op under <see cref="Json"/> -- callers emit <see cref="WriteJson{T}"/> instead in that mode, so stdout is never a mix of prose and JSON.</summary>
    public void Data(string line)
    {
        if (!Json) _stdout.WriteLine(line);
    }

    /// <summary>Diagnostic line to stderr: errors (any mode, written by <see cref="Posture"/>) and, when the caller opts in, `--verbose` detail.</summary>
    public void Diagnostic(string line) => _stderr.WriteLine(line);

    /// <summary>
    /// FAIL-2/ERGO-27 C5's mutating-verb shape: <c>{"ok":bool,"reason":str}</c>.
    /// Emits ONLY when <see cref="Json"/> is set -- mutating verbs print
    /// nothing on stdout otherwise, success or failure alike.
    /// </summary>
    public void MutatingResult(bool ok, string reason)
    {
        if (!Json) return;
        _stdout.WriteLine(JsonSerializer.Serialize(new MutatingResultDto(ok, reason), OutputJsonContext.Default.MutatingResultDto));
    }

    /// <summary>
    /// General escape hatch for a verb's own `--json` shape (`list`'s task
    /// array, `doctor`'s report -- both out of this phase's scope, phases
    /// 08/10) using ITS OWN source-generated <see cref="JsonTypeInfo{T}"/>.
    /// No-op unless <see cref="Json"/> is set.
    /// </summary>
    public void WriteJson<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        if (!Json) return;
        _stdout.WriteLine(JsonSerializer.Serialize(value, typeInfo));
    }
}

/// <summary>Source-generated (AOT/trim-safe) JSON metadata for <see cref="MutatingResultDto"/>. camelCase property names match FAIL-2/C5's documented shape (<c>{"ok":bool,"reason":str}</c>) literally.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MutatingResultDto))]
internal partial class OutputJsonContext : JsonSerializerContext
{
}
