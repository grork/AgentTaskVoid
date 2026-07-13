using System.Security.Cryptography;
using System.Text;

namespace HostEventRecorder;

/// <summary>
/// Derives the named-mutex name that guards appends to a given log file
/// (phase-14 Part A "Guarded append" spec). Two processes handed the same
/// file via different path spellings (case, relative vs. absolute, slash
/// direction, trailing separators) MUST resolve to the identical mutex
/// name -- that is the whole point: the mutex protects the FILE, not a
/// particular path string naming it.
/// </summary>
public static class MutexNaming
{
    /// <summary>
    /// Canonicalizes <paramref name="filePath"/> (<see cref="Path.GetFullPath(string)"/>,
    /// trailing-separator-trimmed, case-folded via <see cref="string.ToUpperInvariant"/>
    /// since NTFS paths are case-insensitive), hashes it (SHA-256, lowercase
    /// hex), and prefixes <see cref="Constants.MutexNamePrefix"/> (named-mutex
    /// names cannot contain path separators, which a raw path would).
    /// </summary>
    public static string DeriveMutexName(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        string full = Path.GetFullPath(filePath).TrimEnd('\\', '/');
        string normalized = full.ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return Constants.MutexNamePrefix + Convert.ToHexStringLower(hash);
    }
}
