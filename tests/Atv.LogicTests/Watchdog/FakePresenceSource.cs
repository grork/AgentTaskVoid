using Atv.Presence;

namespace Atv.LogicTests.Watchdog;

/// <summary>Test double for <see cref="IPresenceSource"/> (phase 15B, LIFE-24 §6) -- a test sets <see cref="Present"/> directly to drive AC5's decay-accrues-only-while-present behavior, with no real OS input/lock-state involved.</summary>
internal sealed class FakePresenceSource : IPresenceSource
{
    public bool Present { get; set; } = true;

    public bool IsPresent() => Present;
}
