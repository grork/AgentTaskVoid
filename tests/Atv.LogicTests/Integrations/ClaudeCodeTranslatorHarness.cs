using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Atv.LogicTests.Integrations;

/// <summary>
/// Drives <c>integrations/claude-code/plugins/atv-integration/translate.ps1</c>
/// as a genuinely separate <c>powershell.exe -File</c> process (exactly how
/// Claude Code's exec-form hooks invoke it -- LIFE-25), feeding it a raw
/// UTF-8 payload on real OS-level stdin and pointing
/// <c>$env:ATV_TRANSLATOR_STUB_EXE</c> at a compiled stand-in for atv.exe
/// (never the real atv.exe, never a live host -- AC2's "stub atv"). The stub
/// is a genuine native-ish exe, not a PowerShell script, specifically
/// because piping into a script invoked via "powershell.exe -File" breaks on
/// a bare "-" token anywhere in argv (a real PowerShell-CLI parser quirk,
/// irrelevant to atv.exe, discovered while building this harness) -- see
/// TestAssets/StubAtv/Program.cs.
/// </summary>
internal static class ClaudeCodeTranslatorHarness
{
    internal sealed record StubInvocation(string[] Argv, string? Stdin);

    private static readonly object BuildLock = new();
    private static bool s_stubBuilt;

    internal static string RepoRoot([CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AppTaskInfoCli.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException($"Could not locate the repo root (AppTaskInfoCli.slnx) above {here}");
        return dir.FullName;
    }

    internal static string TranslatePath =>
        Path.Combine(RepoRoot(), "integrations", "claude-code", "plugins", "atv-integration", "translate.ps1");

    internal static string PluginRoot =>
        Path.Combine(RepoRoot(), "integrations", "claude-code", "plugins", "atv-integration");

    private static string StubProjectPath =>
        Path.Combine(RepoRoot(), "tests", "Atv.LogicTests", "Integrations", "TestAssets", "StubAtv", "StubAtv.csproj");

    private static string StubExePath =>
        Path.Combine(RepoRoot(), "tests", "Atv.LogicTests", "Integrations", "TestAssets", "StubAtv", "bin", "Debug", "net10.0", "stub-atv.exe");

    internal static string EnsureStubBuilt()
    {
        lock (BuildLock)
        {
            if (s_stubBuilt && File.Exists(StubExePath))
                return StubExePath;

            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(StubProjectPath)!,
            };
            psi.ArgumentList.Add("build");
            psi.ArgumentList.Add(StubProjectPath);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("Debug");
            psi.ArgumentList.Add("--nologo");

            using var proc = Process.Start(psi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(120_000))
            {
                proc.Kill(entireProcessTree: true);
                throw new TimeoutException("Building the stub-atv test exe timed out.");
            }
            if (proc.ExitCode != 0 || !File.Exists(StubExePath))
                throw new InvalidOperationException($"Failed to build the stub-atv test exe (exit {proc.ExitCode}).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            s_stubBuilt = true;
            return StubExePath;
        }
    }

    /// <summary>
    /// Runs translate.ps1 once under Windows PowerShell 5.1 (real
    /// powershell.exe, never pwsh -- the phase-18 in-scope target) for the
    /// given Claude Code hook event name, feeding <paramref name="payloadJson"/>
    /// on real OS stdin as raw UTF-8 bytes (no PowerShell pipeline object
    /// involved -- a faithful stand-in for how Claude Code's own hook host
    /// spawns the exec-form command and writes the event JSON to its stdin).
    /// Returns every stub-atv invocation translate.ps1 made, in order.
    /// </summary>
    internal static List<StubInvocation> RunTranslator(string eventName, string payloadJson, string? projectDir = null)
    {
        string stubExe = EnsureStubBuilt();
        string tempDir = Path.Combine(Path.GetTempPath(), "atv-translator-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string outFile = Path.Combine(tempDir, "stub-out.jsonl");

            var psi = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(TranslatePath);
            psi.ArgumentList.Add("-Event");
            psi.ArgumentList.Add(eventName);
            if (projectDir is not null)
            {
                psi.ArgumentList.Add("-ProjectDir");
                psi.ArgumentList.Add(projectDir);
            }
            psi.EnvironmentVariables["ATV_TRANSLATOR_STUB_EXE"] = stubExe;
            psi.EnvironmentVariables["ATV_STUB_OUTPUT"] = outFile;

            using var proc = Process.Start(psi)!;
            byte[] payloadBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(payloadJson);
            proc.StandardInput.BaseStream.Write(payloadBytes, 0, payloadBytes.Length);
            proc.StandardInput.Close();

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(30_000))
            {
                // Exact-PID kill of the process THIS call spawned -- never a
                // broad name/pattern kill (safety constraint).
                proc.Kill(entireProcessTree: true);
                throw new TimeoutException($"translate.ps1 -Event {eventName} did not exit within 30s.");
            }

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"translate.ps1 -Event {eventName} exited {proc.ExitCode} (must always exit 0 -- FAIL-1). STDOUT: {stdout} STDERR: {stderr}");

            var invocations = new List<StubInvocation>();
            if (File.Exists(outFile))
            {
                foreach (string line in File.ReadAllLines(outFile, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    using var doc = JsonDocument.Parse(line);
                    string[] argv = [.. doc.RootElement.GetProperty("Argv").EnumerateArray().Select(e => e.GetString() ?? "")];
                    JsonElement stdinEl = doc.RootElement.GetProperty("Stdin");
                    string? stdin = stdinEl.ValueKind == JsonValueKind.Null ? null : stdinEl.GetString();
                    invocations.Add(new StubInvocation(argv, stdin));
                }
            }
            return invocations;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
