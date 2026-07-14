namespace Atv.Cli;

/// <summary>ERGO-27's global options, layered flag &gt; env &gt; config &gt; default (phase 06) once resolved by <see cref="Config.SettingsLoader"/>. <see cref="WatchdogModeRaw"/> is the raw, UNPARSED <c>--watchdog-mode</c> value (if any) -- <see cref="CompositionRoot"/> feeds it into <see cref="Config.SettingsLoader.Load"/>'s <c>flags</c> layer rather than parsing it here, so there is exactly one parser for that tunable (the same one env/config already share).</summary>
public sealed record GlobalOptions(
    bool Json,
    bool Strict,
    bool Verbose,
    bool Unsafe,
    bool WaitForDebugger,
    string? WatchdogModeRaw);

/// <summary>
/// One parsed invocation: the recognized help/version pseudo-verbs, the verb
/// token (lowercased, or <see langword="null"/> for a bare invocation),
/// positional arguments in order, per-verb value flags keyed by their bare
/// name (e.g. <c>"title"</c>, not <c>"--title"</c>), the <c>--reset</c>
/// boolean, <c>clear</c>'s <c>--include-recycle-bin</c> boolean,
/// <c>run</c>'s <see cref="ChildArgs"/> (everything after a bare <c>--</c>,
/// verbatim -- empty when no <c>--</c> was present), the resolved
/// <see cref="GlobalOptions"/>, and a non-null <see cref="Error"/> for
/// anything <see cref="CommandLine.Parse"/> itself couldn't make sense of
/// (an unknown option, or a value-flag missing its value) -- verb-NAME
/// validity (e.g. a typo'd verb) is deliberately NOT an <see cref="Error"/>
/// here; that is <c>Dispatcher</c>'s job, so it can route even an
/// unrecognized verb through the same non-disruptive posture pipeline as
/// everything else.
/// </summary>
public sealed record ParseResult(
    bool ShowHelp,
    bool ShowVersion,
    string? Verb,
    IReadOnlyList<string> Positionals,
    IReadOnlyDictionary<string, string> Flags,
    bool Reset,
    bool IncludeRecycleBin,
    IReadOnlyList<string> ChildArgs,
    GlobalOptions Global,
    string? Error);

/// <summary>
/// AOT-safe, hand-rolled argv tokenizer (INFRA-2's binary-size budget rules
/// out a reflection-driven binder). Global options (ERGO-27's "Global
/// options") are recognized at ANY token position; per-verb flags/positionals
/// are only collected once a verb token has been consumed (ERGO-27 C6,
/// "global-flag placement") -- a <c>--flag</c>-shaped token seen BEFORE any
/// verb is therefore always an <see cref="ParseResult.Error"/> (there is no
/// verb yet for it to belong to). A bare <c>--</c> AFTER the verb (ERGO-27's
/// <c>run --title T -- &lt;command...&gt;</c>) stops all further
/// flag/global-option recognition and captures every remaining token
/// verbatim into <see cref="ParseResult.ChildArgs"/> -- checked BEFORE the
/// global-option switch below so a child's own <c>--verbose</c>/<c>--json</c>
/// etc. is never swallowed as atv's own flag.
/// </summary>
public static class CommandLine
{
    private static readonly HashSet<string> PerVerbValueFlags = new(StringComparer.Ordinal)
    {
        // Identity flags (ERGO-31 §1 -- every verb except session-ended).
        // --icon-file (ERGO-29, phase 16): a dedicated bring-your-own-image
        // flag, unambiguous against --icon's token/emoji space at parse time.
        // Supplying both on one call is a usage error -- argument-SHAPE
        // validation, so it's the Dispatcher's job (this tokenizer captures
        // both flags uneventfully, same "verb-name validity isn't a parse
        // Error" precedent as ParseResult's own doc comment).
        "--title", "--subtitle", "--icon", "--icon-file", "--deep-link",
        // v2 semantic-verb flags (ERGO-31 §1-3): free-text-eligible ("-" stdin
        // sentinel, resolved by the Dispatcher, not this tokenizer) and plain
        // closed-vocabulary/attribution tokens alike all ride the same
        // generic per-verb value-flag slot -- this parser does not
        // distinguish them (matches the existing "flags aren't verb-scoped"
        // precedent, phase 08 note).
        "--goal", "--label", "--kind", "--agent", "--name",
        "--question", "--summary", "--reason", "--detail",
    };

    public static ParseResult Parse(IReadOnlyList<string> args)
    {
        bool json = false, strict = false, verbose = false, unsafeBypass = false, waitForDebugger = false;
        bool showHelp = false, showVersion = false, reset = false, includeRecycleBin = false;
        string? watchdogModeRaw = null;
        string? verb = null;
        string? error = null;
        var positionals = new List<string>();
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyList<string> childArgs = [];

        for (int i = 0; i < args.Count && error is null; i++)
        {
            string tok = args[i];

            if (verb is not null && tok == "--")
            {
                childArgs = [.. args.Skip(i + 1)];
                break;
            }

            switch (tok)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    continue;
                case "--version":
                    showVersion = true;
                    continue;
                case "--json":
                    json = true;
                    continue;
                case "--strict":
                    strict = true;
                    continue;
                case "--verbose":
                    verbose = true;
                    continue;
                case "--unsafe":
                    unsafeBypass = true;
                    continue;
                case "--wait-for-debugger":
                    waitForDebugger = true;
                    continue;
                case "--watchdog-mode":
                    if (i + 1 >= args.Count) { error = "--watchdog-mode requires a value (spawn|inproc|off)."; break; }
                    watchdogModeRaw = args[++i];
                    continue;
            }
            if (error is not null) break;

            if (verb is null)
            {
                if (tok.StartsWith("--", StringComparison.Ordinal))
                {
                    error = $"Unknown option '{tok}' before a verb.";
                    break;
                }
                verb = tok.ToLowerInvariant();
                continue;
            }

            // After the verb: per-verb flags, then bare positionals.
            if (tok == "--reset") { reset = true; continue; }
            if (tok == "--include-recycle-bin") { includeRecycleBin = true; continue; }

            if (PerVerbValueFlags.Contains(tok))
            {
                if (i + 1 >= args.Count) { error = $"{tok} requires a value."; break; }
                flags[tok[2..]] = args[++i];
                continue;
            }

            if (tok.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unknown option '{tok}' for verb '{verb}'.";
                break;
            }

            positionals.Add(tok);
        }

        var global = new GlobalOptions(json, strict, verbose, unsafeBypass, waitForDebugger, watchdogModeRaw);
        return new ParseResult(showHelp, showVersion, verb, positionals, flags, reset, includeRecycleBin, childArgs, global, error);
    }
}
