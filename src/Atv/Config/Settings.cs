using Atv.Persistence;

namespace Atv.Config;

/// <summary>
/// INFRA-19's watchdog hosting mode. The CODE default is ALWAYS
/// <see cref="Spawn"/> -- suppression is always explicit, via env/config
/// (never debugger-sniffing); a dev launch profile sets
/// <c>ATV_WATCHDOG_MODE</c> to something else, the code never guesses.
/// </summary>
public enum WatchdogMode
{
    /// <summary>Production default: LIFE-17's detached windowless supervisor process.</summary>
    Spawn,

    /// <summary>Dev/debug hosting mode: the watchdog loop runs on a background thread bound to the invoking process's lifetime. NOT a production-supervision equivalent (dies with the process).</summary>
    InProc,

    /// <summary>No watchdog at all -- also used by real-adapter test runs that must not have a supervisor perturbing assertions.</summary>
    Off,
}

/// <summary>
/// Every tunable this phase carries for later consumers (the watchdog: phase
/// 09; <c>run</c>: phase 11) -- this type itself has no behavior beyond
/// holding resolved values plus <see cref="Default"/>. Resolution
/// (<c>flags &gt; env &gt; file &gt; default</c>, ERGO-17) is
/// <see cref="SettingsLoader"/>'s job; this record is intentionally dumb.
/// </summary>
public sealed record Settings(
    WatchdogMode WatchdogMode,
    TimeSpan IdleRunning,
    TimeSpan IdlePaused,
    TimeSpan IdleNeedsAttention,
    TimeSpan IdleCompleted,
    TimeSpan RecycleBinTtl,
    TimeSpan MutexWaitBudget,
    TimeSpan WatchdogPollInterval,
    long LogMaxBytes,
    TimeSpan LogMaxAge,
    TimeSpan RunUpdateDebounce,
    int RunStepMaxLength,
    TimeSpan RunKeepAliveInterval)
{
    /// <summary>
    /// Built-in defaults -- ERGO-17's bottom precedence tier. Idle periods
    /// are LIFE-22's concrete starting values: Running ~30m (must outlast the
    /// longest realistic single tool call -- the wrapper mode self-heartbeats
    /// so it never hits this); Paused/NeedsAttention ~4h (alive-but-quiet:
    /// held session / an away user); Completed (also covers Failed) ~10m
    /// linger then GC. <see cref="MutexWaitBudget"/> mirrors
    /// <see cref="WriteGate.DefaultTimeout"/> (~2s, INFRA-6) -- this is the
    /// tunable override point that constant's own doc comment anticipates,
    /// kept as one source of truth rather than a second hardcoded "2s"
    /// literal. Log rotation (1 MiB / 14 days) and the <c>run</c> tunables
    /// (update debounce, step max length, silent-child keepalive) are sane
    /// build-phase defaults -- FAIL-3 / ERGO-27 explicitly leave exact
    /// thresholds to implementation.
    /// </summary>
    public static Settings Default { get; } = new(
        WatchdogMode: Config.WatchdogMode.Spawn,
        IdleRunning: TimeSpan.FromMinutes(30),
        IdlePaused: TimeSpan.FromHours(4),
        IdleNeedsAttention: TimeSpan.FromHours(4),
        IdleCompleted: TimeSpan.FromMinutes(10),
        RecycleBinTtl: TimeSpan.FromDays(1),
        MutexWaitBudget: WriteGate.DefaultTimeout,
        WatchdogPollInterval: TimeSpan.FromSeconds(30),
        LogMaxBytes: 1L * 1024 * 1024,
        LogMaxAge: TimeSpan.FromDays(14),
        RunUpdateDebounce: TimeSpan.FromSeconds(2),
        RunStepMaxLength: 200,
        RunKeepAliveInterval: TimeSpan.FromMinutes(5));
}
