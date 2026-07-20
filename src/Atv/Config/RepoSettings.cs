using System.Text.Json;

namespace Codevoid.AgentTaskVoid.Config;

/// <summary>Where the create-time anchor directory came from (ERGO-30's "the load-bearing bit"): the caller-supplied <c>--cwd</c> flag, or (direct human use) a fallback to the CURRENT PROCESS's own working directory. Never anything else -- in particular, never a host env var (see <see cref="RepoSettings.Discover"/>'s own remarks; <c>tests/Atv.LogicTests/Config/RepoSettingsTests.cs</c>'s structural test greps this very file's source for that).</summary>
public enum AnchorSource
{
    CwdFlag,
    ProcessCwd,
}

/// <summary>How the discovered <c>.atv.json</c> (if any) parsed.</summary>
public enum RepoConfigParseStatus
{
    /// <summary>No <c>.atv.json</c> was found anywhere between the anchor and the search boundary (a <c>.git</c> root or the filesystem root).</summary>
    NotFound,

    /// <summary>Found, read, and parsed as a flat string-&gt;string JSON object.</summary>
    Ok,

    /// <summary>Found but exceeded <see cref="RepoSettings.MaxFileBytes"/> -- rejected before being read into memory.</summary>
    TooLarge,

    /// <summary>Found but could not be parsed as a flat string-&gt;string JSON object (bad JSON, the wrong shape/nesting, or unreadable).</summary>
    Malformed,
}

/// <summary>
/// Everything one <see cref="RepoSettings.Discover"/> call resolved: the
/// anchor + its source, the nearest <c>.atv.json</c> (if any) and its parse
/// outcome, the ALREADY allowlist-filtered key-&gt;value map it supplied (plus
/// every key it supplied that ISN'T allowlisted, so a caller can log a
/// disallowed-key warning -- ERGO-30: "ignored AND logged, never silently
/// dropped"), and the repo root / branch this SAME walk discovered (used for
/// the <c>{repo}</c>/<c>{branch}</c> title-template tokens and the
/// grouping-intent key -- see <see cref="Codevoid.AgentTaskVoid.Semantics.SemanticEngine"/>'s
/// repo-defaults application).
/// </summary>
public sealed record RepoDiscoveryResult(
    string AnchorPath,
    AnchorSource AnchorSource,
    string? ConfigPath,
    string SearchedUpTo,
    RepoConfigParseStatus ParseStatus,
    IReadOnlyDictionary<string, string> AllowedValues,
    IReadOnlyList<string> DisallowedKeys,
    string? RepoRootDir,
    string? RepoName,
    string? Branch);

/// <summary>
/// ERGO-30's repo-scoped presentation-defaults discovery: a <c>.atv.json</c>
/// the engine auto-discovers by walking UP from a caller-supplied
/// <c>--cwd</c> anchor (never the process's own cwd in the hook case --
/// direct human use with no <c>--cwd</c> falls back to it, see
/// <see cref="Discover"/>), restricted to a five-key presentation-only
/// ALLOWLIST that is itself the entire trust mechanism: a checked-out repo
/// (possibly attacker-controlled, e.g. a malicious PR checked out locally)
/// can change how a card LOOKS and nothing else -- no deep-link, no
/// operational knobs. Phase 17.
///
/// This type owns discovery MECHANICS only (anchor, walk, parse, allowlist
/// filter, cheap git branch read). It knows nothing about flag/env/user-file
/// PRECEDENCE -- that is <see cref="SettingsLoader.ResolvePresentationKey"/>'s
/// job, composed by <see cref="Codevoid.AgentTaskVoid.Semantics.SemanticEngine"/>'s repo-defaults
/// application (the ONLY call site, gated to the upsert CREATE branch --
/// ERGO-30 AC3).
/// </summary>
public static class RepoSettings
{
    /// <summary>Title text (or, for the repo/env/user-file layers ONLY, a template containing <c>{repo}</c>/<c>{branch}</c> -- see <see cref="ExpandTemplate"/>; a caller's own <c>--title</c> flag is always used verbatim, never templated).</summary>
    public const string KeyTitleTemplate = "title-template";
    public const string KeySubtitle = "subtitle";
    public const string KeyIcon = "icon";
    public const string KeyIconFile = "icon-file";

    /// <summary>ERGO-14's deferred glomming intent, arriving repo-scoped: a truthy value (<c>"true"</c>, case-insensitive) makes every card created while this repo's <c>.git</c> root is the discovered repo root SHARE one exact icon <see cref="Uri"/> (ERGO-13 physics) -- see <c>Codevoid.AgentTaskVoid.Semantics.SemanticEngine</c>'s repo-defaults application.</summary>
    public const string KeyGroup = "group";

    public static readonly IReadOnlyList<string> AllowlistKeys = [KeyTitleTemplate, KeySubtitle, KeyIcon, KeyIconFile, KeyGroup];

    public const string FileName = ".atv.json";

    /// <summary>
    /// ERGO-30's size cap on <c>.atv.json</c>, checked via
    /// <see cref="FileInfo.Length"/> BEFORE the file is read into memory --
    /// same hardening posture as phase 16's <c>IconService.DefaultMaxIconFileBytes</c>.
    /// 64 KiB is generous for a five-key flat string map (real ones are a few
    /// hundred bytes) while bounding worst-case memory/parse cost decisively.
    /// Depth is bounded for free: the file must deserialize as a flat
    /// <see cref="Dictionary{TKey,TValue}"/> of string keys/values (the SAME
    /// <see cref="SettingsJsonContext"/> shape <c>atv-config.json</c> uses) --
    /// any nested object/array value fails that deserialization outright
    /// (<see cref="RepoConfigParseStatus.Malformed"/>), so a maliciously
    /// deep/huge JSON document never gets materialized past its own top-level
    /// scan.
    /// </summary>
    public const long MaxFileBytes = 64 * 1024;

    /// <summary>
    /// Anchor resolution (ERGO-30's "load-bearing bit"): <paramref name="cwdFlag"/>
    /// (the caller-supplied <c>--cwd</c>, already parsed off argv by
    /// <see cref="Codevoid.AgentTaskVoid.Cli.CommandLine"/>) wins when present; otherwise
    /// <paramref name="processCwd"/> (the CALLER's own choice of fallback --
    /// production passes <see cref="Environment.CurrentDirectory"/>, tests pass
    /// an arbitrary string). Then walks UP: at each directory, the nearest
    /// <c>.atv.json</c> wins (first one found, never overwritten by an
    /// ancestor's); the walk STOPS (inclusive of the directory just checked)
    /// at the first <c>.git</c> boundary found, or at the filesystem root
    /// (AC1). Deliberately reads NO host environment variable anywhere in this
    /// method or anything it calls (ERGO-30: "atv stays host-agnostic -- it
    /// only ever sees --cwd, never reads a host env var itself") -- a
    /// structural test greps this very file's source text for
    /// <c>GetEnvironmentVariable</c> to keep that true structurally, not just
    /// behaviorally (AC2).
    /// </summary>
    public static RepoDiscoveryResult Discover(string? cwdFlag, string processCwd)
    {
        string anchor;
        AnchorSource source;
        if (!string.IsNullOrEmpty(cwdFlag))
        {
            anchor = cwdFlag;
            source = AnchorSource.CwdFlag;
        }
        else
        {
            anchor = processCwd;
            source = AnchorSource.ProcessCwd;
        }

        string current;
        try { current = Path.GetFullPath(anchor); }
        catch (Exception) { current = anchor; } // A malformed anchor degrades to "search finds nothing" -- never a throw (FAIL-1).

        string? configPath = null;
        string? repoRootDir = null;

        while (true)
        {
            // A malformed anchor/segment can make even Path.Combine itself throw
            // (e.g. an embedded NUL character) -- the whole iteration is wrapped
            // so that degrades to "stop searching here", never an unhandled
            // exception out of Discover (FAIL-1).
            try
            {
                if (configPath is null)
                {
                    string candidate = Path.Combine(current, FileName);
                    if (SafeFileExists(candidate))
                        configPath = candidate;
                }

                if (SafeDirectoryExists(Path.Combine(current, ".git")) || SafeFileExists(Path.Combine(current, ".git")))
                {
                    repoRootDir = current;
                    break; // .git boundary: this directory is the LAST one checked (AC1).
                }
            }
            catch (Exception)
            {
                break;
            }

            string? parent = SafeGetParent(current);
            if (parent is null)
                break; // Filesystem root: also the last one checked (AC1).
            current = parent;
        }

        (RepoConfigParseStatus status, IReadOnlyDictionary<string, string> allowed, IReadOnlyList<string> disallowed) =
            configPath is null ? (RepoConfigParseStatus.NotFound, EmptyMap, EmptyList) : ReadAndFilter(configPath);

        string? repoName = repoRootDir is null
            ? null
            : Path.GetFileName(repoRootDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string? branch = repoRootDir is null ? null : TryReadBranch(repoRootDir);

        return new RepoDiscoveryResult(anchor, source, configPath, current, status, allowed, disallowed, repoRootDir, repoName, branch);
    }

    /// <summary>ERGO-30's title-template tokens: <c>{repo}</c> (the discovered repo dir name) and <c>{branch}</c> (cheaply read off <c>.git/HEAD</c>, never shelling out). Build-detail choice (AC6): a token with no resolvable value is DROPPED (replaced with an empty string) rather than left literal -- a translator/operator never sees a raw <c>{branch}</c> placeholder show up in a real title.</summary>
    public static string ExpandTemplate(string template, string? repoName, string? branch)
        => template.Replace("{repo}", repoName ?? "").Replace("{branch}", branch ?? "");

    // ---- .atv.json read + allowlist filter -------------------------------------

    private static (RepoConfigParseStatus, IReadOnlyDictionary<string, string>, IReadOnlyList<string>) ReadAndFilter(string path)
    {
        long length;
        try { length = new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (RepoConfigParseStatus.Malformed, EmptyMap, EmptyList); }

        if (length <= 0 || length > MaxFileBytes)
            return (RepoConfigParseStatus.TooLarge, EmptyMap, EmptyList);

        Dictionary<string, string>? raw;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            raw = JsonSerializer.Deserialize(bytes, SettingsJsonContext.Default.DictionaryStringString);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return (RepoConfigParseStatus.Malformed, EmptyMap, EmptyList);
        }

        if (raw is null)
            return (RepoConfigParseStatus.Malformed, EmptyMap, EmptyList);

        var allowSet = new HashSet<string>(AllowlistKeys, StringComparer.OrdinalIgnoreCase);
        var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var disallowed = new List<string>();
        foreach (var kv in raw)
        {
            if (allowSet.Contains(kv.Key)) allowed[kv.Key] = kv.Value;
            else disallowed.Add(kv.Key);
        }

        return (RepoConfigParseStatus.Ok, allowed, disallowed);
    }

    // ---- cheap .git/HEAD branch read (never shells out) ------------------------

    private static string? TryReadBranch(string repoRootDir)
    {
        try
        {
            string gitPath = Path.Combine(repoRootDir, ".git");
            string headFile;
            if (Directory.Exists(gitPath))
            {
                headFile = Path.Combine(gitPath, "HEAD");
            }
            else if (File.Exists(gitPath))
            {
                // A worktree/submodule checkout: .git is a FILE containing "gitdir: <path>".
                string content = File.ReadAllText(gitPath).Trim();
                const string gitdirPrefix = "gitdir:";
                if (!content.StartsWith(gitdirPrefix, StringComparison.Ordinal)) return null;
                string gitDir = content[gitdirPrefix.Length..].Trim();
                if (!Path.IsPathRooted(gitDir)) gitDir = Path.GetFullPath(Path.Combine(repoRootDir, gitDir));
                headFile = Path.Combine(gitDir, "HEAD");
            }
            else
            {
                return null;
            }

            if (!File.Exists(headFile)) return null;
            string head = File.ReadAllText(headFile).Trim();

            const string refPrefix = "ref:";
            if (head.StartsWith(refPrefix, StringComparison.Ordinal))
            {
                string refPath = head[refPrefix.Length..].Trim();
                const string headsPrefix = "refs/heads/";
                return refPath.StartsWith(headsPrefix, StringComparison.Ordinal) ? refPath[headsPrefix.Length..] : refPath;
            }

            // Detached HEAD: a raw commit hash. Shown short (7 chars, the common
            // `git rev-parse --short` convention) -- a build-detail choice, not a
            // re-decision: AC6 only requires SOME graceful resolution.
            if (head.Length >= 7 && IsAllHex(head))
                return head[..7];

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsAllHex(string s)
    {
        foreach (char c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    // ---- defensive filesystem probes (a malformed anchor must never throw) -----

    private static bool SafeFileExists(string path) { try { return File.Exists(path); } catch (Exception) { return false; } }
    private static bool SafeDirectoryExists(string path) { try { return Directory.Exists(path); } catch (Exception) { return false; } }
    private static string? SafeGetParent(string path) { try { return Directory.GetParent(path)?.FullName; } catch (Exception) { return null; } }

    private static readonly Dictionary<string, string> EmptyMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> EmptyList = [];
}
