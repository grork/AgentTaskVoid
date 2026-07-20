using System.Runtime.InteropServices;

namespace Codevoid.AgentTaskVoid.Presence;

/// <summary>
/// Real Win32 presence probe (LIFE-24 §6, "build detail: CsWin32-style
/// interop like the existing icon-rendering interop"): hand-rolled
/// source-generated <see cref="LibraryImportAttribute"/> P/Invoke rather than
/// a CsWin32-generated projection -- see this type's remarks for why. Two
/// checks, both required for "present": (1) recent input, via
/// <c>GetLastInputInfo</c> compared against <see cref="Environment.TickCount"/>;
/// (2) the workstation is unlocked, via the well-known <c>OpenInputDesktop</c>
/// trick (fails when the secure/lock-screen desktop is active). Both are
/// plain USER32 calls -- no COM, fully blittable -- so this needs neither
/// <c>DisableRuntimeMarshalling</c> nor <c>AllowUnsafeBlocks</c> project-wide.
///
/// DEVIATION from the phase file's literal "CsWin32" suggestion (explicitly
/// flagged there as a "build detail," not an acceptance criterion): CsWin32
/// requires adding a whole generator pipeline (package reference,
/// <c>NativeMethods.txt</c>, <c>DisableRuntimeMarshalling</c>/
/// <c>AllowUnsafeBlocks</c>) to a project (<c>src/Atv/Atv.csproj</c>) that
/// today has none of that -- only the CsWinRT projection for
/// <c>Windows.UI.Shell.Tasks</c>. Rather than risk an unverified interaction
/// between two source generators plus a marshalling-mode flag flip on the
/// main exe (or standing up a whole new quarantine project, ERGO-22-style,
/// for two simple non-COM calls), this uses <c>LibraryImport</c> -- itself
/// source-generated and just as AOT-safe as CsWin32's output, with zero
/// project-file changes. Same AOT-safety guarantee, lower blast radius
/// against the already-verified 15A build.
/// </summary>
public sealed partial class Win32PresenceSource : IPresenceSource
{
    /// <summary>
    /// How recent "recent input" means. Not a <see cref="Codevoid.AgentTaskVoid.Config.Settings"/>
    /// tunable (no acceptance criterion calls for one -- matches
    /// <c>Codevoid.AgentTaskVoid.Semantics.FieldBudgets</c>'s own "build-phase default, not
    /// config" precedent): a generous default -- shorter than this and a
    /// still-seated-but-reading user would wrongly read as "away".
    /// </summary>
    public static readonly TimeSpan RecentInputWindow = TimeSpan.FromMinutes(5);

    public bool IsPresent() => IsWorkstationUnlocked() && HasRecentInput(RecentInputWindow);

    /// <summary>
    /// The standard "is the workstation locked" Win32 trick: the lock screen
    /// runs on a separate secure desktop, so <c>OpenInputDesktop</c> (which
    /// only ever opens the CURRENT input desktop) fails while it's active.
    /// Fails OPEN to "not present" on any unexpected failure (FAIL-1: never
    /// wrongly accrue decay against a probe result we can't trust).
    /// </summary>
    private static bool IsWorkstationUnlocked()
    {
        nint desktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_READOBJECTS);
        if (desktop == 0) return false;

        NativeMethods.CloseDesktop(desktop);
        return true;
    }

    /// <summary>
    /// <c>GetLastInputInfo</c> reports the <c>GetTickCount()</c>-basis tick of
    /// the last input event system-wide. Compared via UNSIGNED 32-bit
    /// subtraction against <see cref="Environment.TickCount"/> -- the
    /// standard Win32 idiom, correct across the ~49.7-day tick-count
    /// wraparound without any special-casing. Fails open to "not present" if
    /// the call itself fails.
    /// </summary>
    private static bool HasRecentInput(TimeSpan window)
    {
        var info = new NativeMethods.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
        if (!NativeMethods.GetLastInputInfo(ref info))
            return false;

        uint nowTicks = unchecked((uint)Environment.TickCount);
        uint idleMs = unchecked(nowTicks - info.dwTime);
        return idleMs <= (uint)window.TotalMilliseconds;
    }

    private static partial class NativeMethods
    {
        public const uint DESKTOP_READOBJECTS = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial nint OpenInputDesktop(uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseDesktop(nint hDesktop);
    }
}
