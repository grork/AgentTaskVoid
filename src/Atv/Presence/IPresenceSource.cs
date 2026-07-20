namespace Codevoid.AgentTaskVoid.Presence;

/// <summary>
/// LIFE-24 §6's presence gate for the Ready-&gt;Idle decay clock: whether the
/// interactive user currently "had a chance to act" -- device unlocked AND
/// recent input. Polled fresh, once per <see cref="Codevoid.AgentTaskVoid.Watchdog.WatchdogLoop"/>
/// decay-pass tick (never cached/timer-driven -- LIFE-16's stateless-over-disk
/// precedent applies here too: a respawned watchdog must be able to ask this
/// cold and get a correct answer). <see cref="Win32PresenceSource"/> is the
/// sole real implementation; a test fake drives <see cref="IsPresent"/>
/// directly to prove AC5's decay-accrues-only-while-present behavior without
/// touching any real OS input/lock state.
/// </summary>
public interface IPresenceSource
{
    bool IsPresent();
}
