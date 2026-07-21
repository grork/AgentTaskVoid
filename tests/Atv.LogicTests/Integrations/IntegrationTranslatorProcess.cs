using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Codevoid.AgentTaskVoid.LogicTests.Integrations;

internal static class IntegrationTranslatorProcess
{
    internal sealed record StubInvocation(string[] Argv, string? Stdin);

    private static readonly object BuildLock = new();
    private static bool s_stubBuilt;

    internal static string RepoRoot([CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AppTaskInfoCli.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException($"Could not locate repo root above '{here}'.");
    }

    private static string StubProjectPath =>
        Path.Combine(RepoRoot(), "tests", "Atv.LogicTests", "Integrations", "TestAssets", "StubAtv", "StubAtv.csproj");

    private static string StubExePath =>
        Path.Combine(RepoRoot(), "tests", "Atv.LogicTests", "Integrations", "TestAssets", "StubAtv", "bin", "Debug", "net10.0", "stub-atv.exe");

    /// <summary>
    /// Builds a decoy "atv.exe" (a renamed copy of the compiled stub-atv) inside
    /// <paramref name="decoyDir"/>, for phase-20's broken-override precedence
    /// tests. Prepending <paramref name="decoyDir"/> to a run's PATH proves a
    /// buggy fall-through to "Get-Command atv" / "&amp; atv" would be caught by
    /// a real "atv" resolvable from that PATH -- one that physically cannot
    /// reach any live install, since this decoy is the ONLY "atv" on it. A
    /// .NET apphost locates its managed entry-point dll by a name baked into
    /// the apphost binary at build time, not by the apphost file's own current
    /// name (the same property build/Atv.TestIdentity.targets' own remarks rely
    /// on), so copying stub-atv.exe to atv.exe alongside its unrenamed
    /// stub-atv.dll/.deps.json/.runtimeconfig.json produces a fully functional,
    /// independent "atv.exe" launcher.
    /// </summary>
    internal static void CreateAtvDecoy(string decoyDir)
    {
        string stubExe = EnsureStubBuilt();
        string stubDir = Path.GetDirectoryName(stubExe)!;
        Directory.CreateDirectory(decoyDir);
        foreach (string ext in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
        {
            string source = Path.Combine(stubDir, "stub-atv" + ext);
            if (File.Exists(source))
                File.Copy(source, Path.Combine(decoyDir, "stub-atv" + ext), overwrite: true);
        }
        File.Copy(stubExe, Path.Combine(decoyDir, "atv.exe"), overwrite: true);
    }

    /// <summary>
    /// Prepends <paramref name="directory"/> to a fresh copy of this process's
    /// own PATH, for handing to <see cref="Run"/>'s <c>environment</c>
    /// override so the child powershell.exe (and anything it invokes, e.g.
    /// "atv" via Get-Command) resolves <paramref name="directory"/> first.
    /// </summary>
    internal static string PrependToPath(string directory) =>
        directory + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "");

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
                throw new TimeoutException("Building the stub-atv test executable timed out.");
            }
            if (proc.ExitCode != 0 || !File.Exists(StubExePath))
                throw new InvalidOperationException($"Failed to build stub-atv (exit {proc.ExitCode}).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            s_stubBuilt = true;
            return StubExePath;
        }
    }

    internal static List<StubInvocation> Run(
        string scriptPath,
        IReadOnlyList<string> scriptArguments,
        string payloadJson,
        IReadOnlyDictionary<string, string?>? environment = null)
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
            psi.ArgumentList.Add(scriptPath);
            foreach (string argument in scriptArguments)
                psi.ArgumentList.Add(argument);

            psi.EnvironmentVariables["ATV_TRANSLATOR_STUB_EXE"] = stubExe;
            psi.EnvironmentVariables["ATV_STUB_OUTPUT"] = outFile;
            if (environment is not null)
            {
                foreach ((string key, string? value) in environment)
                {
                    if (value is null)
                        psi.EnvironmentVariables.Remove(key);
                    else
                        psi.EnvironmentVariables[key] = value;
                }
            }

            using var proc = Process.Start(psi)!;
            byte[] payloadBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(payloadJson);
            proc.StandardInput.BaseStream.Write(payloadBytes, 0, payloadBytes.Length);
            proc.StandardInput.Close();

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(30_000))
            {
                proc.Kill(entireProcessTree: true);
                throw new TimeoutException($"{Path.GetFileName(scriptPath)} did not exit within 30 seconds.");
            }
            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"{Path.GetFileName(scriptPath)} exited {proc.ExitCode}; translators must always exit 0. STDOUT: {stdout} STDERR: {stderr}");

            var invocations = new List<StubInvocation>();
            if (File.Exists(outFile))
            {
                foreach (string line in File.ReadAllLines(outFile, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    using var doc = JsonDocument.Parse(line);
                    string[] argv = [.. doc.RootElement.GetProperty("Argv").EnumerateArray().Select(e => e.GetString() ?? "")];
                    JsonElement stdinElement = doc.RootElement.GetProperty("Stdin");
                    string? stdin = stdinElement.ValueKind == JsonValueKind.Null ? null : stdinElement.GetString();
                    invocations.Add(new StubInvocation(argv, stdin));
                }
            }
            return invocations;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
