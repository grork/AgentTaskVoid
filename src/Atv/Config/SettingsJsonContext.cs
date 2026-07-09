using System.Text.Json.Serialization;

namespace Atv.Config;

/// <summary>
/// Source-generated (AOT/trim-safe, ERGO-26) JSON metadata for the config
/// file's on-disk shape: a flat object of tunable-name to raw string value,
/// e.g. <c>{"watchdog-mode":"inproc","idle-running":"00:45:00"}</c>. Every
/// value stays a string on disk -- <see cref="SettingsLoader"/> runs exactly
/// ONE parser per field across all three override layers, because flag/env/
/// file raw values are indistinguishable strings to it. No custom converters
/// needed: a flat string dictionary requires none.
/// </summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class SettingsJsonContext : JsonSerializerContext
{
}
