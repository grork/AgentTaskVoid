using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// Test-only stand-in for atv.exe. Records the argv it was invoked with plus
// whatever text (if any) was piped to its stdin, appending one JSON record
// per invocation to the file named by ATV_STUB_OUTPUT. See
// tests/Atv.LogicTests/Integrations/ClaudeCodeTranslatorTests.cs.

string? outPath = Environment.GetEnvironmentVariable("ATV_STUB_OUTPUT");
if (string.IsNullOrEmpty(outPath))
{
    return 0;
}

string? stdinText = null;
if (Console.IsInputRedirected)
{
    using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
    stdinText = reader.ReadToEnd();
}

var record = new StubRecord(args, stdinText);
string json = JsonSerializer.Serialize(record, StubJsonContext.Default.StubRecord);

var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
File.AppendAllText(outPath, json + "\n", utf8NoBom);

return 0;

internal sealed record StubRecord(string[] Argv, string? Stdin);

[JsonSerializable(typeof(StubRecord))]
internal partial class StubJsonContext : JsonSerializerContext
{
}
