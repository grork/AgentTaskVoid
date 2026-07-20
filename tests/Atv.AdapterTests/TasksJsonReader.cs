using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codevoid.AgentTaskVoid.AdapterTests;

/// <summary>
/// Raw reader for the current package's <c>tasks.json</c> -- the durable file
/// <c>OSClient.API.dll</c> itself writes on every <c>Create</c>/<c>Update</c>/
/// <c>Remove</c> (see docs/windows-ui-shell-tasks/README.md, "How it works"). Used
/// by AdapterFidelityTests to assert on-disk fidelity directly, independent of (and as
/// a cross-check against) what <see cref="Codevoid.AgentTaskVoid.Store.AppTaskStore"/>'s own
/// <c>FindAll()</c>/<c>Find()</c> report back through the seam.
///
/// Shape verified empirically against a real <c>Create()</c> (2026-07-08, Windows 11
/// 26100, ARM64): <c>{"tasks":[{...}],"version":"1.0"}</c>, one object per task. Field
/// names below are the exact on-disk names, not the DTO/WinRT names -- notably
/// <c>iconPath</c> (an absolute local file path the platform resolves FROM the
/// <c>ms-appx:///</c> <c>iconUri</c> passed to <c>Create</c>, not the URI itself), and
/// <c>dataJson</c> (a nested <c>{"template": "...", "data": {...}}</c> envelope for
/// the <c>AppTaskContent</c> shape -- left as a raw <see cref="JsonElement"/> here
/// rather than strongly typed, since only <c>SequenceOfSteps</c>'s shape
/// (<c>completedSteps</c>/<c>currentStep</c>) has been confirmed on disk; deliberately
/// not guessing the other content shapes' on-disk field names).
///
/// This is intentionally the only place in the test suite that parses <c>tasks.json</c>
/// directly -- everything else goes through <see cref="Codevoid.AgentTaskVoid.Store.IAppTaskStore"/>,
/// matching INFRA-9's "raw tasks.json reader" alongside <c>FindAll()</c> assertions.
/// </summary>
internal static class TasksJsonReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// <c>%LOCALAPPDATA%\Packages\&lt;PackageFamilyName&gt;\SystemAppData\AppTasks\tasks.json</c>
    /// for the CURRENT process's package identity. Verified empirically: this is keyed
    /// by <b>Package Family Name</b> (<c>Package.Current.Id.FamilyName</c>), not the
    /// full name (which additionally embeds the version and architecture) -- using the
    /// full name here would silently never find the file.
    /// </summary>
    public static string GetPathForCurrentPackage()
    {
        string familyName = Windows.ApplicationModel.Package.Current.Id.FamilyName;
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Packages", familyName, "SystemAppData", "AppTasks", "tasks.json");
    }

    /// <summary>
    /// Reads and parses the current package's <c>tasks.json</c>. Returns an empty
    /// <see cref="RawTasksFile"/> (not a throw) if the file doesn't exist yet -- e.g.
    /// before this identity's very first <c>Create()</c>, which is itself a valid,
    /// clean state to assert against (AC4's "leaves a clean tasks.json").
    /// </summary>
    public static RawTasksFile Read()
    {
        string path = GetPathForCurrentPackage();
        if (!File.Exists(path))
            return new RawTasksFile([], "");

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RawTasksFile>(json, JsonOptions)
            ?? new RawTasksFile([], "");
    }
}

internal sealed record RawTasksFile(
    [property: JsonPropertyName("tasks")] IReadOnlyList<RawTask> Tasks,
    [property: JsonPropertyName("version")] string Version);

internal sealed record RawTask(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("subtitle")] string Subtitle,
    [property: JsonPropertyName("deepLink")] string DeepLink,
    [property: JsonPropertyName("iconPath")] string IconPath,
    [property: JsonPropertyName("taskState")] int TaskState,
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("startTime")] long StartTime,
    [property: JsonPropertyName("endTime")] long? EndTime,
    [property: JsonPropertyName("hiddenByUser")] bool HiddenByUser,
    [property: JsonPropertyName("dataJson")] JsonElement DataJson);
