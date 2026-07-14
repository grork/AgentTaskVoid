using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Atv.LogicTests.Integrations;

/// <summary>
/// Phase 18 AC1 + AC7: static, artifact-shape teeth on the Claude Code v2
/// plugin (<c>integrations/claude-code/</c>) that supersedes the phase-13
/// <c>settings.hooks.json</c> fragment. Verifies the hooks declaration is
/// well-formed and matches every LIFE-25 discipline (program+args exec form,
/// no embedded one-liners, no <c>shell</c> selection,
/// <c>${CLAUDE_PLUGIN_ROOT}</c>-rooted), only ERGO-31 verbs are ever invoked
/// by translate.ps1, no <c>--strict</c> anywhere, <c>SessionEnd</c> is the
/// sole synchronous hook, and every non-terminal call forwards
/// <c>--cwd ${CLAUDE_PROJECT_DIR}</c>. Runs no PowerShell -- purely file/JSON
/// inspection; see <see cref="ClaudeCodeTranslatorTests"/> for the runtime
/// (AC2) coverage.
/// </summary>
[TestClass]
public sealed class ClaudeCodePluginArtifactTests
{
    /// <summary>The 8 ERGO-31 semantic verbs -- the only verbs translate.ps1 may ever pass as argv[0] to atv.</summary>
    private static readonly HashSet<string> ErgoV2Verbs = new(StringComparer.Ordinal)
    {
        "working", "activity", "blocked", "ready", "broken", "agent-started", "agent-stopped", "session-ended",
    };

    /// <summary>The retired v1 lifecycle verb set (ERGO-27) -- must never appear as an actual invocation anywhere in the plugin artifact.</summary>
    private static readonly string[] RetiredV1Verbs = ["start", "step", "state", "attention", "done", "fail"];

    private static readonly string[] ExpectedHookEvents =
    [
        "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest",
        "Notification", "Stop", "StopFailure", "SubagentStart", "SubagentStop", "SessionEnd",
    ];

    private static string RepoRoot([CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AppTaskInfoCli.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException($"Could not locate the repo root (AppTaskInfoCli.slnx) above {here}");
        return dir.FullName;
    }

    private static string IntegrationsRoot => Path.Combine(RepoRoot(), "integrations", "claude-code");
    private static string PluginRoot => Path.Combine(IntegrationsRoot, "plugins", "atv-integration");
    private static string HooksPath => Path.Combine(PluginRoot, "hooks", "hooks.json");
    private static string TranslatorPath => Path.Combine(PluginRoot, "translate.ps1");
    private static string MapPath => Path.Combine(PluginRoot, "map.json");
    private static string PluginManifestPath => Path.Combine(PluginRoot, ".claude-plugin", "plugin.json");
    private static string MarketplacePath => Path.Combine(IntegrationsRoot, ".claude-plugin", "marketplace.json");

    // ---- basic presence / supersession -----------------------------------

    [TestMethod]
    public void RetiredV1Fragment_NoLongerExists()
    {
        Assert.IsFalse(File.Exists(Path.Combine(IntegrationsRoot, "settings.hooks.json")),
            "The phase-13 settings.hooks.json fragment must be removed -- superseded by the plugin.");
    }

    [TestMethod]
    public void PluginArtifactFiles_AllExist()
    {
        Assert.IsTrue(File.Exists(MarketplacePath), $"Expected {MarketplacePath}");
        Assert.IsTrue(File.Exists(PluginManifestPath), $"Expected {PluginManifestPath}");
        Assert.IsTrue(File.Exists(HooksPath), $"Expected {HooksPath}");
        Assert.IsTrue(File.Exists(TranslatorPath), $"Expected {TranslatorPath}");
        Assert.IsTrue(File.Exists(MapPath), $"Expected {MapPath}");
        Assert.IsTrue(File.Exists(Path.Combine(IntegrationsRoot, "README.md")), "Expected integrations/claude-code/README.md");
    }

    // ---- marketplace.json / plugin.json well-formedness -------------------

    [TestMethod]
    public void MarketplaceJson_IsWellFormed_AndPointsAtThePlugin()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(MarketplacePath));
        Assert.AreEqual(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.IsTrue(doc.RootElement.TryGetProperty("name", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("owner", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("plugins", out var plugins));
        Assert.AreEqual(JsonValueKind.Array, plugins.ValueKind);
        Assert.HasCount(1, plugins.EnumerateArray().ToArray());

        var entry = plugins.EnumerateArray().First();
        Assert.AreEqual("atv-integration", entry.GetProperty("name").GetString());
        Assert.AreEqual("./plugins/atv-integration", entry.GetProperty("source").GetString());
    }

    [TestMethod]
    public void PluginManifest_IsWellFormed_WithRequiredName()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(PluginManifestPath));
        Assert.AreEqual(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.AreEqual("atv-integration", doc.RootElement.GetProperty("name").GetString());
    }

    // ---- hooks.json shape ---------------------------------------------------

    [TestMethod]
    public void HooksJson_IsWellFormed_WithExactlyTheExpectedEventKeys()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(HooksPath));
        Assert.AreEqual(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.IsTrue(doc.RootElement.TryGetProperty("hooks", out var hooks));
        Assert.AreEqual(JsonValueKind.Object, hooks.ValueKind);

        var actualKeys = hooks.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(ExpectedHookEvents, actualKeys.ToArray());
    }

    [TestMethod]
    public void HooksJson_NeverSelectsAShell_AndNeverPassesStrict()
    {
        string raw = File.ReadAllText(HooksPath);
        Assert.DoesNotContain("\"shell\"", raw, "LIFE-25: hook lines must never select a shell -- exec form (args) makes this unnecessary and the phase-13 footgun (\"shell\":\"powershell\") must not recur.");
        Assert.DoesNotContain("--strict", raw, "A hook line must never pass --strict (FAIL-1).");
    }

    [TestMethod]
    public void HooksJson_EveryHookLine_IsPlainProgramArgsExecForm_RootedAtPluginRoot()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(HooksPath));
        var hooks = doc.RootElement.GetProperty("hooks");

        int hookLineCount = 0;
        foreach (var eventProp in hooks.EnumerateObject())
        {
            foreach (var matcherGroup in eventProp.Value.EnumerateArray())
            {
                foreach (var hookLine in matcherGroup.GetProperty("hooks").EnumerateArray())
                {
                    hookLineCount++;
                    Assert.AreEqual("command", hookLine.GetProperty("type").GetString(), $"{eventProp.Name}: hook type must be 'command'.");
                    Assert.AreEqual("powershell.exe", hookLine.GetProperty("command").GetString(), $"{eventProp.Name}: must invoke powershell.exe directly, never an embedded one-liner.");

                    Assert.IsTrue(hookLine.TryGetProperty("args", out var args), $"{eventProp.Name}: exec form requires an 'args' array (LIFE-25 -- never shell form).");
                    Assert.AreEqual(JsonValueKind.Array, args.ValueKind);
                    string[] argv = [.. args.EnumerateArray().Select(a => a.GetString() ?? "")];

                    Assert.Contains("-File", argv, $"{eventProp.Name}: must invoke translate.ps1 via -File (a real script, never an embedded snippet).");
                    string? filePathArg = argv.SkipWhile(a => a != "-File").Skip(1).FirstOrDefault();
                    Assert.IsNotNull(filePathArg, $"{eventProp.Name}: -File must be followed by a path.");
                    Assert.StartsWith("${CLAUDE_PLUGIN_ROOT}", filePathArg!, $"{eventProp.Name}: the translate.ps1 path must be rooted at ${{CLAUDE_PLUGIN_ROOT}}, never a versioned MSIX path.");
                    Assert.EndsWith("translate.ps1", filePathArg!, $"{eventProp.Name}: must invoke translate.ps1 specifically.");

                    Assert.Contains("-Event", argv, $"{eventProp.Name}: must pass -Event.");
                    string? eventArg = argv.SkipWhile(a => a != "-Event").Skip(1).FirstOrDefault();
                    Assert.AreEqual(eventProp.Name, eventArg, $"the -Event value must match the hooks.json event key it's declared under.");
                }
            }
        }

        Assert.AreEqual(ExpectedHookEvents.Length, hookLineCount, "Expected exactly one hook line per event.");
    }

    [TestMethod]
    public void HooksJson_SessionEndIsTheSoleSynchronousHook_WithTimeout10()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(HooksPath));
        var hooks = doc.RootElement.GetProperty("hooks");

        foreach (var eventProp in hooks.EnumerateObject())
        {
            var hookLine = eventProp.Value.EnumerateArray().First().GetProperty("hooks").EnumerateArray().First();
            bool isAsync = hookLine.TryGetProperty("async", out var asyncEl) && asyncEl.ValueKind == JsonValueKind.True;

            if (eventProp.Name == "SessionEnd")
            {
                Assert.IsFalse(isAsync, "SessionEnd must be the SOLE synchronous hook (INFRA-27 teardown-race lesson).");
                Assert.IsTrue(hookLine.TryGetProperty("timeout", out var timeoutEl), "SessionEnd must declare an explicit timeout.");
                Assert.AreEqual(10, timeoutEl.GetInt32(), "SessionEnd's timeout must be 10 (the phase-13/14 precedent).");
            }
            else
            {
                Assert.IsTrue(isAsync, $"{eventProp.Name} must be async -- SessionEnd is the ONLY synchronous hook.");
            }
        }
    }

    [TestMethod]
    public void HooksJson_NotificationMatcher_IsRestrictedToIdlePrompt()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(HooksPath));
        var notification = doc.RootElement.GetProperty("hooks").GetProperty("Notification");
        var matcherGroup = notification.EnumerateArray().First();
        Assert.IsTrue(matcherGroup.TryGetProperty("matcher", out var matcherEl), "Notification must scope itself with a matcher.");
        Assert.AreEqual("idle_prompt", matcherEl.GetString(), "Only idle_prompt should route through the conduit -- permission_prompt is a no-op by design (PermissionRequest owns Blocked attribution, phase-14 finding 5).");
    }

    [TestMethod]
    public void HooksJson_EveryNonTerminalEvent_ForwardsProjectDir()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(HooksPath));
        var hooks = doc.RootElement.GetProperty("hooks");

        foreach (var eventProp in hooks.EnumerateObject())
        {
            var hookLine = eventProp.Value.EnumerateArray().First().GetProperty("hooks").EnumerateArray().First();
            string[] argv = [.. hookLine.GetProperty("args").EnumerateArray().Select(a => a.GetString() ?? "")];

            if (eventProp.Name == "SessionEnd")
            {
                Assert.DoesNotContain("-ProjectDir", argv, "session-ended takes no --cwd (no upsert, no identity flags -- ERGO-31 SS2).");
            }
            else
            {
                Assert.Contains("-ProjectDir", argv, $"{eventProp.Name} must forward the ERGO-30 --cwd anchor.");
                string? dirArg = argv.SkipWhile(a => a != "-ProjectDir").Skip(1).FirstOrDefault();
                Assert.AreEqual("${CLAUDE_PROJECT_DIR}", dirArg, $"{eventProp.Name}: -ProjectDir must be the literal ${{CLAUDE_PROJECT_DIR}} placeholder Claude Code substitutes -- never resolved/parsed here.");
            }
        }
    }

    // ---- translate.ps1 / map.json content teeth ------------------------------

    [TestMethod]
    public void Translator_ParsesCleanly_UnderWindowsPowerShell51()
    {
        // Shells out to the real Windows PowerShell 5.1 parser (never pwsh) --
        // avoids adding a System.Management.Automation package reference to
        // this project just for a syntax check, and is more faithful to the
        // actual DIST-4 in-box-runtime target than referencing the SDK
        // assembly directly would be. Writes the check itself to a temp -File
        // script (the SAME invocation shape proven throughout
        // ClaudeCodeTranslatorHarness) rather than -Command, whose string
        // form does not reliably bind trailing CLI args to $args the way
        // -File does.
        string checkScript = Path.Combine(Path.GetTempPath(), "atv-parse-check-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(checkScript,
            "param([string]$Target)\n" +
            "$e = $null\n" +
            "[void][System.Management.Automation.Language.Parser]::ParseFile($Target, [ref]$null, [ref]$e)\n" +
            "if ($e.Count -gt 0) { $e | ForEach-Object { $_.Message }; exit 1 } else { exit 0 }\n");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(checkScript);
            psi.ArgumentList.Add("-Target");
            psi.ArgumentList.Add(TranslatorPath);

            using var proc = System.Diagnostics.Process.Start(psi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            Assert.IsTrue(proc.WaitForExit(30_000), "PowerShell parse check timed out.");
            Assert.AreEqual(0, proc.ExitCode, $"translate.ps1 must parse cleanly under Windows PowerShell 5.1. {stdout} {stderr}");
        }
        finally
        {
            try { File.Delete(checkScript); } catch { /* best-effort cleanup */ }
        }
    }

    [TestMethod]
    public void Translator_OnlyInvokesErgoV2Verbs_NeverARetiredV1Verb()
    {
        string content = File.ReadAllText(TranslatorPath);
        // Matches the literal verb this script passes as argv[0] to atv --
        // anchored on "$sid" as the array's second element (every real call
        // shape is @("<verb>", $sid, ...)) so flag-value array literals like
        // @("--cwd", $ProjectDir) or @("--agent", $agentId) never false-match.
        var matches = Regex.Matches(content, @"@\(""([a-z-]+)"",\s*\$sid\b");
        Assert.IsNotEmpty(matches, "Expected at least one Invoke-Atv verb literal in translate.ps1 -- the extraction regex may be stale.");

        var invokedVerbs = matches.Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var unknown = invokedVerbs.Where(v => !ErgoV2Verbs.Contains(v)).ToArray();
        Assert.IsEmpty(unknown, $"translate.ps1 invokes verb(s) outside the ERGO-31 v2 surface: {string.Join(", ", unknown)}");

        foreach (string retired in RetiredV1Verbs)
            Assert.DoesNotContain(retired, invokedVerbs, $"translate.ps1 must never invoke the retired v1 verb '{retired}'.");
    }

    [TestMethod]
    public void Translator_NeverPassesStrict()
    {
        // Checks for the QUOTED literal form -- the shape an actual argv
        // element would take -- rather than a bare substring match, since the
        // script's own doc comments legitimately describe the FAIL-1
        // discipline in prose ("--strict is never passed").
        string content = File.ReadAllText(TranslatorPath);
        Assert.DoesNotContain("\"--strict\"", content, "translate.ps1 must never pass --strict to atv (FAIL-1).");
    }

    [TestMethod]
    public void Translator_NeverPassesIdentityFlags_PreservesRepoBrandingPrecedence()
    {
        // Deliberate design decision (see integrations/claude-code/README.md's
        // "Deliberately no identity flags" section): SemanticEngine.ApplyRepoDefaults
        // resolves title/subtitle/icon as flag > env > repo (.atv.json) > user
        // config > default -- a hard-coded --title/--subtitle/--icon on every
        // call would permanently block phase 17's repo-branding feature for
        // every repo using this plugin. Locks the decision in against
        // accidental regression.
        string content = File.ReadAllText(TranslatorPath);
        foreach (string flag in new[] { "\"--title\"", "\"--subtitle\"", "\"--icon\"", "\"--icon-file\"" })
            Assert.DoesNotContain(flag, content, $"translate.ps1 must never pass {flag} -- it would always beat a repo's .atv.json (flag > repo precedence), blocking AC6's premise.");
    }

    [TestMethod]
    public void Translator_AlwaysExitsZero_StructurallyGuarded()
    {
        string content = File.ReadAllText(TranslatorPath);
        // The whole event-dispatch body is wrapped in try/catch and the script
        // ends with an unconditional exit 0 -- both structurally present.
        Assert.Contains("} catch {", content);
        Assert.EndsWith("exit 0", content.TrimEnd());
    }

    [TestMethod]
    public void Translator_ReadsItsOwnStdin_AsUtf8()
    {
        string content = File.ReadAllText(TranslatorPath);
        Assert.Contains("[System.Text.Encoding]::UTF8", content);
        Assert.Contains("StreamReader", content);
    }

    [TestMethod]
    public void Translator_NeverRefersToAV1LifecycleVerbAsAnInvocation()
    {
        // Distinct from the ERGO-v2-verb-set check above: a broader sweep for
        // any retired verb appearing as a quoted CLI-argument-shaped token
        // (word-boundary matched, so "agent-started"/"agent-stopped" -- which
        // legitimately contain "start"/"stop" as substrings, not matches for
        // the retired "start"/"step"/"state"/"attention"/"done"/"fail" set --
        // never false-positive).
        string content = File.ReadAllText(TranslatorPath);
        foreach (string verb in RetiredV1Verbs)
        {
            var quoted = new Regex($"\"{Regex.Escape(verb)}\"");
            Assert.IsFalse(quoted.IsMatch(content), $"translate.ps1 must not reference the retired v1 verb '{verb}' as a quoted token.");
        }
    }

    [TestMethod]
    public void MapJson_IsWellFormed()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(MapPath));
        Assert.AreEqual(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.IsTrue(doc.RootElement.TryGetProperty("toolKind", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("toolLabelField", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("brokenReason", out _));
    }

    // ---- AC7 supersession-clean sweep across the whole integrations/ tree -----

    [TestMethod]
    public void NoV1VerbReference_AsAQuotedCliToken_AnywhereUnderIntegrations()
    {
        // "Reference" here means an actual-invocation shape ("<verb>" as a
        // quoted CLI token, or "atv <verb>"/"& atv <verb>"), not the
        // historical "supersedes v1 (start/step/state/attention/done/fail)"
        // prose every phase-15+ doc (including the already-shipped
        // docs/integration-api.md) legitimately carries. That distinction
        // matches this repo's own established precedent.
        string[] artifactFiles =
        [
            HooksPath, TranslatorPath, MapPath, PluginManifestPath, MarketplacePath,
        ];

        var invocationPattern = new Regex(@"&\s+atv\s+([a-z-]+)|atv\s+([a-z-]+)\s", RegexOptions.IgnoreCase);

        foreach (string file in artifactFiles)
        {
            string content = File.ReadAllText(file);
            foreach (Match m in invocationPattern.Matches(content))
            {
                string verb = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToLowerInvariant();
                Assert.IsFalse(RetiredV1Verbs.Contains(verb), $"{file} invokes retired v1 verb '{verb}'.");
            }
        }
    }
}
